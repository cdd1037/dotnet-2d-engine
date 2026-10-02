using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GameAuthoringLab;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Dotnet2D.Ui.Generator;

/// <summary>Opt-in semantic contract generation. No consumer code is evaluated.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class UiContractGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor Controller = Rule("DUI001", "Invalid UI controller");
    private static readonly DiagnosticDescriptor Command = Rule("DUI002", "Invalid UI command");
    private static readonly DiagnosticDescriptor Model = Rule("DUI003", "Invalid UI model");
    private static readonly DiagnosticDescriptor Document = Rule("DUI004", "UI document is not an AdditionalFile");
    private static readonly DiagnosticDescriptor Binding = Rule("DUI005", "Invalid UI document contract");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contracts = context.SyntaxProvider.ForAttributeWithMetadataName(
                ContractBuilder.ContractAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (attribute, token) => ContractBuilder.Create(attribute, token))
            .WithTrackingName("UiContractDescriptors");
        var documents = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".rml", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, token) => new AdditionalDocument(Normalize(file.Path), file.GetText(token)?.ToString()))
            .WithTrackingName("UiAdditionalDocuments").Collect();
        var projectDirectory = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out string? path) ? Normalize(path) : "");
        var inputs = contracts.Combine(documents).Combine(projectDirectory)
            .Select(static (input, _) => SelectDocument(input.Left.Left, input.Left.Right, input.Right))
            .WithTrackingName("UiSelectedContractInputs");
        context.RegisterSourceOutput(inputs, static (production, input) => Produce(production, input));
    }

    private static ContractInput SelectDocument(ContractDescriptor contract, ImmutableArray<AdditionalDocument> documents, string projectDirectory)
    {
        if (contract.Issues.Length != 0) return new ContractInput(contract, null, null);
        string requested = Normalize(contract.Document);
        if (!requested.EndsWith(".rml", StringComparison.OrdinalIgnoreCase))
            return new ContractInput(contract, null, "UiContract must name an RML document with a .rml extension and include it as an AdditionalFiles item.");
        if (Path.IsPathRooted(requested)) return new ContractInput(contract, null, "UiContract document paths must be project-relative, not absolute: '" + contract.Document + "'.");
        if (string.IsNullOrEmpty(projectDirectory))
            return new ContractInput(contract, null, "MSBuildProjectDirectory is unavailable to the UI generator. Add <CompilerVisibleProperty Include=\"MSBuildProjectDirectory\" /> and include '" + contract.Document + "' as an AdditionalFiles item.");
        string exact;
        try { exact = Normalize(Path.GetFullPath(Path.Combine(projectDirectory, requested))); }
        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
        { return new ContractInput(contract, null, "Invalid project-relative UI document path '" + contract.Document + "': " + e.Message); }
        AdditionalDocument? match = null;
        int count = 0;
        foreach (var document in documents)
        {
            string candidate;
            try { candidate = Normalize(Path.GetFullPath(Path.IsPathRooted(document.Path) ? document.Path : Path.Combine(projectDirectory, document.Path))); }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { continue; }
            if (!string.Equals(candidate, exact, StringComparison.Ordinal)) continue;
            match = document; count++;
        }
        if (count == 0) return new ContractInput(contract, null, "The associated UI document '" + contract.Document + "' is missing from AdditionalFiles. Add <AdditionalFiles Include=\"" + contract.Document + "\" />. Paths and names are case-sensitive.");
        if (count != 1) return new ContractInput(contract, null, "The associated UI document '" + contract.Document + "' resolves to multiple AdditionalFiles entries. Include it exactly once.");
        if (match!.Text is null) return new ContractInput(contract, null, "The AdditionalFiles item '" + contract.Document + "' could not be read by the compiler.");
        return new ContractInput(contract, new AdditionalDocument(exact, match.Text), null);
    }

    private static void Produce(SourceProductionContext context, ContractInput input)
    {
        foreach (var issue in input.Contract.Issues)
            context.ReportDiagnostic(Diagnostic.Create(Descriptor(issue.Id), issue.Site.ToLocation(), issue.Message));
        if (input.Contract.Issues.Length != 0) return;
        if (input.Error is not null)
            context.ReportDiagnostic(Diagnostic.Create(Document, input.Contract.Site.ToLocation(), input.Error));
        if (input.Document is { Text: { } text } document)
        {
            var nodes = input.Contract.Nodes.Select(n => new UiContractNode(n.Name, n.Kind, n.Parent, n.Maximum, Declaration(n.Site))).ToArray();
            var commands = input.Contract.Commands.Select(c => new UiContractCommand(c.Name, c.Kinds, true, Declaration(c.Site))).ToArray();
            try { UiContractValidator.Validate(text, document.Path, nodes, commands); }
            catch (UiContractException error)
            {
                SourceSite declaration = input.Contract.Site;
                if (error.Declaration is { } declared)
                {
                    var command = input.Contract.Commands.FirstOrDefault(c => c.Site.File == declared.FilePath && c.Site.Line == declared.Line);
                    var node = input.Contract.Nodes.FirstOrDefault(n => n.Site.File == declared.FilePath && n.Site.Line == declared.Line);
                    declaration = command?.Site ?? node?.Site ?? declaration;
                }
                var related = declaration.ToLocation();
                string cause = error.Cause;
                if (related != Location.None && cause.IndexOf("Registered at ", StringComparison.Ordinal) < 0)
                    cause += " Declared at " + declaration.File + ":" + declaration.Line + ".";
                context.ReportDiagnostic(Diagnostic.Create(Binding, DocumentLocation(document.Path, text, error.Line, error.Column),
                    related == Location.None ? ImmutableArray<Location>.Empty : ImmutableArray.Create(related),
                    properties: null, messageArgs: new object[] { error.Code + " " + error.Field + ": " + cause }));
            }
        }
        // Generate even when authored RML is invalid, so an author gets the precise RML
        // diagnostic without a cascade of missing generated-method compiler errors.
        context.AddSource(HintName(input.Contract.Identity), SourceText.From(input.Contract.Source, Encoding.UTF8));
    }

    private static UiContractDeclaration Declaration(SourceSite site) => new(site.File, site.Line);
    private static Location DocumentLocation(string path, string text, int line, int column)
    {
        SourceText source = SourceText.From(text, Encoding.UTF8);
        int row = Math.Max(0, Math.Min(source.Lines.Count - 1, line - 1));
        TextLine textLine = source.Lines[row];
        int character = Math.Max(0, Math.Min(textLine.Span.Length, column - 1));
        int start = textLine.Start + character;
        int length = start < source.Length ? 1 : 0;
        return Location.Create(path, new TextSpan(start, length), source.Lines.GetLinePositionSpan(new TextSpan(start, length)));
    }
    private static string HintName(string identity)
    {
        // Stable across machines/processes and short enough for generated-file output
        // even when namespaces or controller names are very long.
        using var hash = SHA256.Create();
        byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(identity));
        var result = new StringBuilder("UiContract_");
        foreach (byte value in digest) result.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return result.Append(".g.cs").ToString();
    }
    private static string Normalize(string path) => path.Replace('\\', '/');
    private static DiagnosticDescriptor Rule(string id, string title) => new(id, title, "{0}", "Dotnet2D.Ui", DiagnosticSeverity.Error, true);
    private static DiagnosticDescriptor Descriptor(string id) => id switch { "DUI001" => Controller, "DUI002" => Command, "DUI003" => Model, "DUI004" => Document, _ => Binding };
}
