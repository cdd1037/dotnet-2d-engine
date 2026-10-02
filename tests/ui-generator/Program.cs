using System.Collections.Immutable;
using System.Text;
using Dotnet2D.Ui.Generator;
using GameAuthoringLab;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

internal static class Program
{
    private const string CsPath = "/fixtures/project/Controller.cs";
    private const string RmlPath = "/fixtures/project/Ui/menu.rml";
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);
    private static readonly ImmutableArray<MetadataReference> References =
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? throw new Exception("No platform references."))
        .Split(Path.PathSeparator).Append(typeof(UiContractAttribute).Assembly.Location)
        .Distinct(StringComparer.Ordinal).Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using GameAuthoringLab;
        namespace Fixtures;
        [UiModel]
        public sealed class State
        {
            public string Title { get; set; } = "Menu";
            public bool Visible { get; set; } = true;
            public int Count { get; set; }
            public double Amount { get; set; }
            public ulong Id { get; set; } = 1;
            [UiField(Maximum = 4)]
            public IReadOnlyList<Row> Rows { get; set; } = Array.Empty<Row>();
        }
        [UiModel]
        public sealed class Row
        {
            public ulong Id { get; set; }
            public string Title { get; set; } = "";
            public Leaf[] Children { get; set; } = Array.Empty<Leaf>();
        }
        [UiModel]
        public sealed class Leaf
        {
            public string Text { get; set; } = "";
        }
        [UiContract(typeof(State), "Ui/menu.rml")]
        public partial class Controller
        {
            [UiCommand] private void Ping() { }
            [UiCommand] private void Pick(ulong id) { }
            [UiCommand] private void Set(bool visible, string text) { }
            [UiCommand] private void Adjust(double amount, bool visible, string text) { }
            [UiCommand] private void Apply(string text, bool visible, double amount, ulong id) { }
        }
        """;

    private const string Rml = """
        <rml>
          <head><title>Generator fixture</title></head>
          <body data-model="model">
            <p>{{ state.Title }}</p>
            <button data-event-click="Ping()">Ping</button>
            <button data-event-click="Pick(state.Id)">Pick</button>
            <button data-event-click="Set(state.Visible, state.Title)">Set</button>
            <button data-event-click="Adjust(state.Amount, state.Visible, state.Title)">Adjust</button>
            <button data-event-click="Apply(state.Title, state.Visible, state.Amount, state.Id)">Apply</button>
            <div data-for="row, i : state.Rows">
              <span>{{ row.Title }} {{ i }} {{ state.Rows.size }}</span>
              <button data-event-click="Pick(row.Id)">Pick row</button>
              <div data-for="leaf : row.Children"><span>{{ leaf.Text }}</span></div>
            </div>
          </body>
        </rml>
        """;

    private static int Main()
    {
        (string Name, Action Run)[] tests =
        [
            ("generated API, static getters and automatic typed registrations", GeneratedApi),
            ("all supported scalar and collection shapes compile", SupportedShapes),
            ("exact PascalCase names and explicit migration aliases", ExactNamesAndAliases),
            ("explicit packet IDs are rejected", ExplicitIds),
            ("keyword identifiers and explicit aliases retain CLR identity", KeywordIdentifiers),
            ("ordinary unannotated types are ignored", UnannotatedTypes),
            ("field rename retains original RML and C# provenance", FieldRename),
            ("method rename retains original RML and C# provenance", MethodRename),
            ("unknown command is rejected at its RML use site", UnknownCommand),
            ("Key and Number mismatch retains command declaration", KeyNumberMismatch),
            ("wrong command arity retains command declaration", CommandArity),
            ("nested empty arrays are validated from declared schema", EmptyNestedArrays),
            ("source positions survive XML entities and CRLF", SourcePositions),
            ("unsupported command signatures are actionable", UnsupportedCommands),
            ("unsupported models are actionable", UnsupportedModels),
            ("recursive model shapes are rejected", ModelCycles),
            ("expanded acyclic model shapes stop at the schema bound", ExpandedModelBound),
            ("duplicate names and explicit IDs are rejected", Duplicates),
            ("generated member names are reserved", ReservedMembers),
            ("native command names are reserved without banning model fields", ReservedCommandNames),
            ("controller declaration requirements are diagnosed", InvalidControllers),
            ("missing and ambiguous AdditionalFiles are diagnosed", MissingDocuments),
            ("unrelated AdditionalFiles are never read", UnrelatedDocuments),
            ("native-owned expression cases remain conservative", NativeOwnedExpressions),
            ("known paths inside dynamic expressions are still checked", KnownDynamicPaths),
            ("unchanged incremental inputs use cached steps", IncrementalUnchanged),
            ("unrelated semantic edits preserve descriptor equality", IncrementalUnrelatedEdit),
            ("one RML edit changes diagnostics without registration churn", IncrementalRmlEdit),
        ];
        int failures = 0;
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + test.Name + "\n" + error); }
        }
        Console.WriteLine($"UI generator regressions: {tests.Length - failures}/{tests.Length} passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void GeneratedApi()
    {
        RunResult run = Run();
        Success(run);
        INamedTypeSymbol controller = run.Output.GetTypeByMetadataName("Fixtures.Controller")!;
        IMethodSymbol schema = controller.GetMembers("CreateUiSchema").OfType<IMethodSymbol>().Single();
        IMethodSymbol commands = controller.GetMembers("CreateUiCommands").OfType<IMethodSymbol>().Single();
        Check(schema.IsStatic && schema.DeclaredAccessibility == Accessibility.Private && schema.Parameters.IsEmpty,
            "Generated schema entry point must be private static and parameterless.");
        Equal("GameAuthoringLab.UiRecord<Fixtures.State>", schema.ReturnType.ToDisplayString(), "Typed schema return type");
        Check(!commands.IsStatic && commands.DeclaredAccessibility == Accessibility.Private && commands.Parameters.IsEmpty,
            "Generated command entry point must be private instance and parameterless.");
        Equal("GameAuthoringLab.UiCommands", commands.ReturnType.ToDisplayString(), "Command return type");

        var generated = GeneratedRoot(run);
        var getters = generated.DescendantNodes().OfType<LambdaExpressionSyntax>().ToArray();
        Check(getters.Length >= 10 && getters.All(g => g.Modifiers.Any(SyntaxKind.StaticKeyword)),
            "Every generated DTO getter must be a static lambda.");
        var calls = Calls(run, "On");
        Equal(5, calls.Length, "Zero-to-four-argument command registrations");
        var names = calls.Select(c => StringArgument(c, 0)).Order().ToArray();
        Equal("Adjust,Apply,Pick,Ping,Set", string.Join(',', names), "Automatic command names preserve declarations");
        foreach (var call in calls)
        {
            var symbol = run.Output.GetSemanticModel(call.SyntaxTree).GetSymbolInfo(call).Symbol as IMethodSymbol;
            Check(symbol is { Name: "On" } && symbol.ContainingType.ToDisplayString() == "GameAuthoringLab.UiCommands",
                "Every command must bind the public typed UiCommands.On overload.");
            Check(call.ArgumentList.Arguments.Any(a => a.Expression is MemberAccessExpressionSyntax
                { Expression: ThisExpressionSyntax }), "Registration must call a direct controller method group.");
            Check(call.ArgumentList.Arguments.Any(a => a.NameColon?.Name.Identifier.ValueText == "file" &&
                a.Expression is LiteralExpressionSyntax value && value.Token.ValueText == CsPath), "Registration keeps source file.");
            Check(call.ArgumentList.Arguments.All(a => a.Expression is not LiteralExpressionSyntax n ||
                !n.IsKind(SyntaxKind.NumericLiteralExpression) || a.NameColon is not null),
                "Automatic registrations must not bake explicit numeric packet IDs.");
        }
        string text = generated.ToFullString();
        foreach (string forbidden in new[] { "System.Reflection", "GetProperty(", "GetMethod(", "Activator.", "Assembly.Load", "JsonSerializer", "Expression.Compile", "dynamic " })
            Check(!text.Contains(forbidden, StringComparison.Ordinal), "Generated code must not contain " + forbidden);
        using var pe = new MemoryStream();
        var emit = run.Output.Emit(pe);
        Check(emit.Success, "Generated fixture must emit without loading the assembly: " + Describe(emit.Diagnostics));
    }

    private static void SupportedShapes()
    {
        string source = Source.Replace("public int Count { get; set; }", """
            public int Count { get; set; }
            public sbyte SignedByte { get; set; }
            public byte Byte { get; set; }
            public short Short { get; set; }
            public ushort UShort { get; set; }
            public uint UInt { get; set; }
            public float Float { get; set; }
            public string? OptionalText { get; set; }
            public string PublicField = "";
            public string[] Labels { get; set; } = Array.Empty<string>();
            public List<double> Numbers { get; set; } = new();
            public IReadOnlyList<bool> Flags { get; set; } = Array.Empty<bool>();
            public ulong[] Keys { get; set; } = Array.Empty<ulong>();
            public Leaf Detail { get; set; } = new();
            """);
        Success(Run(source));
        Success(Run(Source.Replace("public sealed class State", "internal sealed class State")));
        Success(Run(Source.Replace("public sealed class Leaf", "public struct Leaf").Replace("public string Text { get; set; } = \"\";", "public string Text { get; set; }")));
        Success(Run(Source.Replace("public sealed class Leaf", "public sealed record Leaf")));
        string nested = Source.Replace("public partial class Controller", "public partial class Outer { public partial class Controller") + "\n}";
        // Place the attribute on Controller, not on the containing declaration.
        nested = nested.Replace("[UiContract(typeof(State), \"Ui/menu.rml\")]\npublic partial class Outer { public partial class Controller",
            "public partial class Outer { [UiContract(typeof(State), \"Ui/menu.rml\")] public partial class Controller");
        Success(Run(nested));
    }

    private static void ExactNamesAndAliases()
    {
        RunResult wrongCase = Run(rml: Rml.Replace("state.Title", "state.title"));
        RmlDiagnostic(wrongCase, "title", LineOf(Rml, "state.Title"));
        string source = Source.Replace("public string Title { get; set; } = \"Menu\";",
            "[UiField(Name = \"legacy_title\")] public string Heading { get; set; } = \"Menu\";")
            .Replace("[UiCommand] private void Pick", "[UiCommand(Name = \"legacy_pick\")] private void Choose");
        RunResult run = Run(source, Rml.Replace("state.Title", "state.legacy_title").Replace("Pick(", "legacy_pick("));
        Success(run);
        Check(Calls(run, "Text").Any(c => StringArgument(c, 0) == "legacy_title"), "Field alias must be emitted exactly.");
        Check(Calls(run, "On").Any(c => StringArgument(c, 0) == "legacy_pick"), "Command alias must be emitted exactly.");
        Check(GeneratedRoot(run).DescendantNodes().OfType<MemberAccessExpressionSyntax>().Any(m => m.Name.Identifier.ValueText == "Heading"),
            "Aliased field getter must use its renamed CLR declaration.");
    }

    private static void KeywordIdentifiers()
    {
        string source = Source.Replace("public string Title { get; set; } = \"Menu\";",
            "[UiField(Name = \"Title\")] public string @class { get; set; } = \"Menu\";")
            .Replace("[UiCommand] private void Pick", "[UiCommand(Name = \"Pick\")] private void @event");
        RunResult run = Run(source);
        Success(run);
        var members = GeneratedRoot(run).DescendantNodes().OfType<MemberAccessExpressionSyntax>().ToArray();
        Check(members.Any(m => m.Name.Identifier.ValueText == "class"), "Escaped field getter must preserve CLR identifier.");
        Check(members.Any(m => m.Name.Identifier.ValueText == "event"), "Escaped command method group must preserve CLR identifier.");
    }

    private static void UnannotatedTypes()
    {
        RunResult run = Run("public class Ordinary { public string Text { get; set; } = \"\"; }");
        Check(run.Diagnostics.IsEmpty && run.Result.GeneratedTrees.IsEmpty, "An ordinary class must not become a generated contract.");
    }

    private static void ExplicitIds()
    {
        Check(typeof(UiCommandAttribute).GetProperty("Id") is null, "Explicit packet IDs are outside the public generated contract.");
        RunResult run = Run(); Success(run);
        Check(Calls(run, "On").All(call => call.ArgumentList.Arguments[1].Expression is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.NumericLiteralExpression)),
            "Generated registration never emits an explicit numeric wire ID.");
    }

    private static void FieldRename()
    {
        RunResult run = Run(Source.Replace("public string Title { get; set; } = \"Menu\";", "public string Heading { get; set; } = \"Menu\";"));
        RmlDiagnostic(run, "Title", LineOf(Rml, "state.Title"), related: true);
    }

    private static void MethodRename()
    {
        RunResult run = Run(Source.Replace("void Pick(", "void Choose("));
        RmlDiagnostic(run, "Pick", LineOf(Rml, "Pick(state.Id)"), related: true);
    }

    private static void UnknownCommand() => RmlDiagnostic(Run(rml: Document("<button data-event-click=\"Missing()\"/>")), "Missing", 3);

    private static void KeyNumberMismatch()
    {
        Diagnostic key = RmlDiagnostic(Run(rml: Document("<button data-event-click=\"Pick(state.Amount)\"/>")), "Key", 3, related: true);
        Contains(key.GetMessage(), "Number", "Number cannot masquerade as a Key");
        RelatedLine(key, LineOf(Source, "void Pick("));
        Diagnostic number = RmlDiagnostic(Run(rml: Document("<button data-event-click=\"Adjust(state.Id, true, 'x')\"/>")), "Number", 3, related: true);
        Contains(number.GetMessage(), "Key", "Key cannot be passed to Number");
        RmlDiagnostic(Run(rml: Document("<button data-event-click=\"Pick(state.Title)\"/>")), "Text", 3, related: true);
    }

    private static void CommandArity()
    {
        Diagnostic diagnostic = RmlDiagnostic(Run(rml: Document("<button data-event-click=\"Apply(state.Title)\"/>")), "expects 4", 3, related: true);
        RelatedLine(diagnostic, LineOf(Source, "void Apply("));
    }

    private static void EmptyNestedArrays()
    {
        string rml = Document("""
            <div data-for="row : state.Rows">
              <div data-for="leaf : row.Children"><span>{{ leaf.Missing }}</span></div>
            </div>
            """);
        Diagnostic diagnostic = RmlDiagnostic(Run(rml: rml), "leaf.Missing", 4, related: true);
        RelatedLine(diagnostic, LineOf(Source, "public Leaf[] Children"));
        RmlDiagnostic(Run(rml: Document("<p>{{ state.Rows[0].Children[0].Missing }}</p>")), "Missing", 3, related: true);
    }

    private static void SourcePositions()
    {
        string rml = Document("<p>before &amp; {{ state.Missing }}</p>").Replace("\n", "\r\n");
        Diagnostic diagnostic = RmlDiagnostic(Run(rml: rml), "Missing", 3, related: true);
        Check(diagnostic.Location.SourceSpan.Start > 0, "RML diagnostic must have a real source offset.");
    }

    private static void UnsupportedCommands()
    {
        string[] signatures =
        [
            "private int Ping() => 1;",
            "private static void Ping() { }",
            "private async void Ping() { await System.Threading.Tasks.Task.Yield(); }",
            "private void Ping<T>() { }",
            "private void Ping(int count) { }",
            "private void Ping(ref double value) { }",
            "private void Ping(out double value) { value = 0; }",
            "private void Ping(params string[] values) { }",
            "private void Ping(string a, string b, string c, string d, string e) { }",
        ];
        foreach (string signature in signatures)
            CsDiagnostic(Run(Source.Replace("private void Ping() { }", signature), Document("<p/>")), "DUI002");
        CsDiagnostic(Run(Source.Replace("[UiCommand] private void Ping", "[UiCommand(Name = \"\")] private void Ping"), Document("<p/>")), "DUI002");
        Success(Run(Source.Replace("[UiCommand] private void Ping", "[UiCommand(Name = null)] private void Ping")));
    }

    private static void UnsupportedModels()
    {
        foreach (string type in new[] { "decimal", "long", "double?", "DateTime", "Dictionary<string, string>", "Func<string>", "int[]", "string[][]" })
        {
            string source = Source.Replace("public int Count { get; set; }", $"public {type} Unsupported {{ get; set; }} = default!;");
            CsDiagnostic(Run(source, Document("<p/>")), "DUI003");
        }
        foreach (string type in new[] { "string?[]", "List<Row?>", "IReadOnlyList<Row?>" })
        {
            string source = Source.Replace("public int Count { get; set; }", $"public {type} NullableElements {{ get; set; }} = default!;");
            Diagnostic diagnostic = CsDiagnostic(Run(source, Document("<p/>")), "DUI003");
            Contains(diagnostic.GetMessage(), "nonnullable", "Nullable collection elements need an actionable projection remedy");
        }
        CsDiagnostic(Run(Source.Replace("[UiModel]\npublic sealed class State", "public sealed class State"), Document("<p/>")), "DUI003");
        CsDiagnostic(Run(Source.Replace("public int Count { get; set; }", "public string this[int index] => \"\";"), Document("<p/>")), "DUI003");
        CsDiagnostic(Run(Source.Replace("Maximum = 4", "Maximum = 0"), Document("<p/>")), "DUI003");
        CsDiagnostic(Run(Source.Replace("Maximum = 4", "Maximum = 65"), Document("<p/>")), "DUI003");
        CsDiagnostic(Run(Source.Replace("public int Count { get; set; }", "[UiField(Maximum = 4)] public int Count { get; set; }"), Document("<p/>")), "DUI003");
        CsDiagnostic(Run(Source.Replace("public int Count { get; set; }", "[UiField(Maximum = 4)] public Leaf Detail { get; set; } = new();"), Document("<p/>")), "DUI003");
        CsDiagnostic(Run(Source.Replace("public int Count { get; set; }", "[UiField(Name = \"\")] public int Count { get; set; }"), Document("<p/>")), "DUI003");
        Success(Run(Source.Replace("public int Count { get; set; }", "[UiField(Name = null)] public int Count { get; set; }")));
    }

    private static void ModelCycles()
    {
        string self = Source.Replace("public int Count { get; set; }", "public State Parent { get; set; } = null!;");
        Contains(CsDiagnostic(Run(self, Document("<p/>")), "DUI003").GetMessage(), "cycle", "Self-recursive model explanation");
        string collection = Source.Replace("public string Text { get; set; } = \"\";", "public IReadOnlyList<State> Parents { get; set; } = Array.Empty<State>();");
        Contains(CsDiagnostic(Run(collection, Document("<p/>")), "DUI003").GetMessage(), "cycle", "Cycle through arrays explanation");
    }

    private static void ExpandedModelBound()
    {
        var source = new StringBuilder(Source.Replace("public int Count { get; set; }", "public Node0 Expanded { get; set; } = new();"));
        for (int i = 0; i < 10; i++)
            source.Append($"\n[UiModel] public sealed class Node{i} {{ public Node{i + 1} Left {{ get; set; }} = new(); public Node{i + 1} Right {{ get; set; }} = new(); }}");
        source.Append("\n[UiModel] public sealed class Node10 { public string Text { get; set; } = \"\"; }");
        Contains(CsDiagnostic(Run(source.ToString(), Document("<p/>")), "DUI003").GetMessage(), "128", "Expanded-schema bound explanation");
    }

    private static void Duplicates()
    {
        CsDiagnostic(Run(Source.Replace("[UiCommand] private void Pick", "[UiCommand(Name = \"Ping\")] private void Pick"), Document("<p/>")), "DUI002");
        CsDiagnostic(Run(Source.Replace("public int Count { get; set; }", "[UiField(Name = \"Title\")] public int Count { get; set; }"), Document("<p/>")), "DUI003");
    }

    private static void ReservedMembers()
    {
        foreach (string name in new[] { "CreateUiCommands", "CreateUiSchema" })
            CsDiagnostic(Run(Source.Replace("private void Ping()", "private void " + name + "()"), Document("<p/>")), "DUI001");
    }

    private static void ReservedCommandNames()
    {
        foreach (string reserved in new[] { "it", "it_index", "ev", "true", "false", "size", "literal", "IT_INDEX", "Size", "Literal" })
            Contains(CsDiagnostic(Run(Source.Replace("[UiCommand] private void Ping", "[UiCommand(Name = \"" + reserved + "\")] private void Ping"),
                Document("<p/>")), "DUI002").GetMessage(), "reserved", "Native command-name restriction");
        Success(Run(Source.Replace("public int Count { get; set; }", "public int Size { get; set; }"), Document("<p>{{ state.Size }}</p>")));
    }

    private static void InvalidControllers()
    {
        CsDiagnostic(Run(Source.Replace("public partial class Controller", "public class Controller")), "DUI001");
        CsDiagnostic(Run(Source.Replace("public partial class Controller", "public partial class Controller<T>")), "DUI001");
        CsDiagnostic(Run(Source.Replace("public partial class Controller", "public partial record Controller")), "DUI001");
        CsDiagnostic(Run(Source.Replace("public partial class Controller", "file partial class Controller")), "DUI001");
        CsDiagnostic(Run(Source.Replace("public partial class Controller", "public static partial class Controller")
            .Replace("private void", "private static void")), "DUI001");
    }

    private static void MissingDocuments()
    {
        CsDiagnostic(Run(documents: []), "DUI004");
        CsDiagnostic(Run(documents: [new MemoryText("/fixtures/project/Ui/other.rml", Rml)]), "DUI004");
        CsDiagnostic(Run(Source.Replace("Ui/menu.rml", ""), documents: []), "DUI004");
        CsDiagnostic(Run(documents: [new MemoryText("/fixtures/a/Ui/menu.rml", Rml), new MemoryText("/fixtures/b/Ui/menu.rml", Rml)]), "DUI004");
        Contains(CsDiagnostic(Run(documents: [new MemoryText(RmlPath, Rml), new MemoryText(RmlPath, Rml)]), "DUI004").GetMessage(),
            "multiple", "Duplicate physical AdditionalFiles entries");
        CsDiagnostic(Run(documents: [new MemoryText("/fixtures/project/Ui/Menu.rml", Rml)]), "DUI004");
        CsDiagnostic(Run(Source.Replace("Ui/menu.rml", RmlPath)), "DUI004");
        CsDiagnostic(Run(Source.Replace("Ui/menu.rml", "Ui/menu.txt"), documents: [new MemoryText("/fixtures/project/Ui/menu.txt", Rml)]), "DUI004");
        Contains(CsDiagnostic(Run(documents: [new MemoryText(RmlPath, null)]), "DUI004").GetMessage(), "could not be read", "Unreadable AdditionalFile");
        RunResult configured = Run();
        GeneratorDriver unconfigured = CSharpGeneratorDriver.Create([new UiContractGenerator().AsSourceGenerator()], configured.Documents, ParseOptions);
        Contains(CsDiagnostic(Run(configured.Input, configured.Documents, unconfigured), "DUI004").GetMessage(),
            "MSBuildProjectDirectory", "Missing compiler-visible project directory");
    }

    private static void UnrelatedDocuments()
    {
        var unrelated = new MemoryText("/fixtures/project/Content/payload.json", "irrelevant", failOnRead: true);
        RunResult run = Run(documents: [new MemoryText(RmlPath, Rml), unrelated]);
        Success(run);
        Equal(0, unrelated.ReadCount, "Non-RML AdditionalFiles must be filtered before content is read");
    }

    private static void NativeOwnedExpressions()
    {
        string[] fragments =
        [
            "<button data-event-click=\"Pick(ev.value)\"/>",
            "<button data-event-click=\"Pick(literal.value)\"/>",
            "<button data-event-click=\"Pick(state.Amount + 1)\"/>",
            "<button data-event-click=\"Pick(state.Visible ? state.Id : state.Amount)\"/>",
            "<button data-event-click=\"Pick(state.Title | format)\"/>",
            "<button data-event-click=\"Pick('9007199254740993')\"/>",
            "<p>{{ state.Rows[state.Count].RuntimeMember }}</p>",
            "<p data-custom-metadata=\"unknown_root\" data-text=\"ignored_by_native\">{{ state.Title }}</p>",
            "<textarea>{{ unknown_native_text }}</textarea>",
            "<p>&#123;&#123; unknown_entity_text &#125;&#125;</p>",
            "<div data-alias-row=\"state.Rows[0]\"><p>{{ row.Title }}</p></div>",
            "<div data-alias-choice=\"state.Visible ? state.Title : state.Amount\"><p>{{ choice }}</p></div>",
            "<div data-for=\"row, index : state.Rows\"><p>{{ index }} {{ row.Title }}</p></div>",
            "<div data-for=\"state.Rows\"><p>{{ it.Title }} {{ it_index }}</p></div>",
            "<p>{{ state.Title | format(2) }}</p>",
            "<p>{{ state.Title</p>",
        ];
        foreach (string fragment in fragments) Success(Run(rml: Document(fragment)));
    }

    private static void KnownDynamicPaths()
    {
        RmlDiagnostic(Run(rml: Document("<p>{{ state.Rows[state.Missing].Title }}</p>")), "Missing", 3, related: true);
        RmlDiagnostic(Run(rml: Document("<p>{{ state.Missing | format }}</p>")), "Missing", 3, related: true);
        RmlDiagnostic(Run(rml: Document("<button data-event-click=\"Pick(state.Missing + 1)\"/>")), "Missing", 3, related: true);
        RmlDiagnostic(Run(rml: Document("<div data-alias-row=\"state.Rows[0]\"><p>{{ row.Missing }}</p></div>")), "Missing", 3, related: true);
    }

    private static void IncrementalUnchanged()
    {
        RunResult first = Run();
        Success(first);
        RunResult second = Run(first.Input, first.Documents, first.Driver);
        Success(second);
        Equal(RegistrationSignature(first), RegistrationSignature(second), "Unchanged generated registrations");
        AssertCached(second, "UiContractDescriptors");
        AssertCached(second, "UiAdditionalDocuments");
    }

    private static void IncrementalUnrelatedEdit()
    {
        RunResult first = Run();
        Success(first);
        var unrelated = CSharpSyntaxTree.ParseText("namespace Unrelated; public sealed class Helper { public int Count => 42; }",
            ParseOptions, "/fixtures/project/Unrelated.cs");
        RunResult second = Run(first.Input.AddSyntaxTrees(unrelated), first.Documents, first.Driver);
        Success(second);
        Equal(RegistrationSignature(first), RegistrationSignature(second), "Unrelated semantic edits cannot change registrations");
        var reasons = StepOutputs(second, "UiContractDescriptors");
        Check(reasons.Length > 0 && reasons.All(r => r is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged),
            "Unrelated semantic edits must preserve descriptor equality: " + string.Join(", ", reasons));
        Check(first.Result.GeneratedTrees.Single().GetText().ContentEquals(second.Result.GeneratedTrees.Single().GetText()),
            "Unrelated semantic edits must preserve generated content.");
    }

    private static void IncrementalRmlEdit()
    {
        string source = Source + """

            [UiContract(typeof(State), "Ui/other.rml")]
            public partial class OtherController
            {
                [UiCommand] private void Ping() { }
            }
            """;
        var menu = new MemoryText(RmlPath, Rml);
        var other = new MemoryText("/fixtures/project/Ui/other.rml", Document("<button data-event-click=\"Ping()\"/>"));
        RunResult first = Run(source, documents: [menu, other]);
        Success(first);
        var edited = new MemoryText(RmlPath, Rml.Replace("state.Title", "state.Typo"));
        GeneratorDriver changedDriver = first.Driver.ReplaceAdditionalText(menu, edited);
        RunResult second = Run(first.Input, [edited, other], changedDriver);
        RmlDiagnostic(second, "Typo", LineOf(Rml, "state.Title"), related: true);
        Check(second.Diagnostics.All(d => d.Location.GetLineSpan().Path != other.Path), "Untouched document must not acquire diagnostics.");
        Equal(RegistrationSignature(first), RegistrationSignature(second), "RML-only edit cannot alter generated registrations");
        AssertCached(second, "UiContractDescriptors");
        var documentSteps = StepOutputs(second, "UiAdditionalDocuments");
        Check(documentSteps.Any(r => r == IncrementalStepRunReason.Modified), "Changed RML text must invalidate its descriptor.");
        Check(documentSteps.Any(r => r == IncrementalStepRunReason.Cached), "Untouched RML text must remain cached.");
    }

    private static RunResult Run(string source = Source, string rml = Rml, MemoryText[]? documents = null)
    {
        var tree = CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), ParseOptions, CsPath);
        var compilation = CSharpCompilation.Create("GeneratedUiFixture", [tree], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Check(!compilation.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), "Invalid test input: " + Describe(compilation.GetDiagnostics()));
        return Run(compilation, documents ?? [new MemoryText(RmlPath, rml)]);
    }

    private static RunResult Run(CSharpCompilation compilation, MemoryText[] documents, GeneratorDriver? driver = null)
    {
        driver ??= CSharpGeneratorDriver.Create([new UiContractGenerator().AsSourceGenerator()], documents, ParseOptions,
            optionsProvider: new ProjectOptions(),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        var result = driver.GetRunResult();
        Check(result.Results.All(r => r.Exception is null), "Generator must not throw: " + string.Join("\n", result.Results.Select(r => r.Exception)));
        return new(compilation, (CSharpCompilation)output, documents, driver, result, diagnostics);
    }

    private static void Success(RunResult run)
    {
        Check(!run.Diagnostics.Any(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning),
            "Unexpected generator diagnostic: " + Describe(run.Diagnostics));
        Check(!run.Output.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error),
            "Generated compilation errors: " + Describe(run.Output.GetDiagnostics()));
        Check(run.Result.GeneratedTrees.Length != 0, "Expected generated source.");
    }

    private static Diagnostic CsDiagnostic(RunResult run, string id)
    {
        var diagnostic = run.Diagnostics.FirstOrDefault(d => d.Id == id) ??
            throw new Exception("Expected " + id + ", got: " + Describe(run.Diagnostics));
        Equal(DiagnosticSeverity.Error, diagnostic.Severity, "Contract violations must fail the build");
        Equal(CsPath, diagnostic.Location.GetLineSpan().Path, "C# declaration diagnostic path");
        Check(diagnostic.Location.GetLineSpan().StartLinePosition.Line >= 0, "C# diagnostic has a real line.");
        return diagnostic;
    }

    private static Diagnostic RmlDiagnostic(RunResult run, string message, int line, bool related = false)
    {
        var diagnostic = run.Diagnostics.FirstOrDefault(d => d.Location.GetLineSpan().Path == RmlPath && d.GetMessage().Contains(message, StringComparison.Ordinal)) ??
            throw new Exception("Expected RML diagnostic containing '" + message + "', got: " + Describe(run.Diagnostics));
        Equal("DUI005", diagnostic.Id, "RML contract diagnostic ID");
        Equal(DiagnosticSeverity.Error, diagnostic.Severity, "RML contract violation severity");
        Check(!run.Output.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error),
            "An RML error must not cause generated C# compiler-error cascades: " + Describe(run.Output.GetDiagnostics()));
        Equal(line, diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1, "Original RML source line");
        if (related) Check(diagnostic.AdditionalLocations.Any(l => l.GetLineSpan().Path == CsPath), "RML diagnostic must include related C# declaration.");
        return diagnostic;
    }

    private static void RelatedLine(Diagnostic diagnostic, int expected) => Check(diagnostic.AdditionalLocations.Any(l =>
        l.GetLineSpan().Path == CsPath && l.GetLineSpan().StartLinePosition.Line + 1 == expected), "Expected related C# declaration line " + expected + ": " + diagnostic);
    private static SyntaxNode GeneratedRoot(RunResult run) => run.Result.GeneratedTrees.Single().GetRoot();
    private static InvocationExpressionSyntax[] Calls(RunResult run, string method) => run.Result.GeneratedTrees
        .SelectMany(t => t.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        .Where(i => i.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText == method).ToArray();
    private static string StringArgument(InvocationExpressionSyntax call, int index) => ((LiteralExpressionSyntax)call.ArgumentList.Arguments[index].Expression).Token.ValueText;
    private static string RegistrationSignature(RunResult run) => string.Join("\n", Calls(run, "On").Select(c => c.ArgumentList.NormalizeWhitespace().ToFullString()).Order(StringComparer.Ordinal));
    private static IncrementalStepRunReason[] StepOutputs(RunResult run, string name)
    {
        var steps = run.Result.Results.Single().TrackedSteps;
        Check(steps.ContainsKey(name), "Missing tracked step '" + name + "'; available: " + string.Join(", ", steps.Keys));
        return steps[name].SelectMany(s => s.Outputs).Select(o => o.Reason).ToArray();
    }
    private static void AssertCached(RunResult run, string name)
    {
        var reasons = StepOutputs(run, name);
        Check(reasons.Length > 0 && reasons.All(r => r == IncrementalStepRunReason.Cached),
            name + " should be cached: " + string.Join(", ", reasons));
    }
    private static string Document(string body) => "<rml>\n  <body data-model=\"model\">\n" + body + "\n  </body>\n</rml>";
    private static int LineOf(string text, string needle)
    {
        int offset = text.IndexOf(needle, StringComparison.Ordinal);
        Check(offset >= 0, "Fixture text not found: " + needle);
        return text.AsSpan(0, offset).Count('\n') + 1;
    }
    private static string Describe(IEnumerable<Diagnostic> diagnostics) => string.Join("\n", diagnostics.Select(d => d.ToString()));
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual, string description) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{description}: expected {expected}, got {actual}.");
    private static void Contains(string text, string value, string description) => Check(text.Contains(value, StringComparison.OrdinalIgnoreCase), description + ": " + text);
    private sealed record RunResult(CSharpCompilation Input, CSharpCompilation Output, MemoryText[] Documents,
        GeneratorDriver Driver, GeneratorDriverRunResult Result, ImmutableArray<Diagnostic> Diagnostics);
    private sealed class ProjectOptions : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options();
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
        private sealed class Options : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                value = key == "build_property.MSBuildProjectDirectory" ? "/fixtures/project" : "";
                return value.Length != 0;
            }
        }
    }
    private sealed class MemoryText(string path, string? content, bool failOnRead = false) : AdditionalText
    {
        private readonly SourceText? text = content is null ? null : SourceText.From(content, Encoding.UTF8);
        public int ReadCount { get; private set; }
        public override string Path => path;
        public override SourceText? GetText(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (failOnRead) throw new InvalidOperationException("Unrelated AdditionalFile was read: " + Path);
            return text;
        }
    }
}
