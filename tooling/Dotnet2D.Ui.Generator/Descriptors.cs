using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Dotnet2D.Ui.Generator;

// These values deliberately do not retain Compilation, ISymbol, SyntaxNode, or SourceText.
// Equality covers generated text, validation shape, source locations, and diagnostics, so
// unrelated semantic changes may be transformed without invalidating source production.
internal sealed class ContractDescriptor : IEquatable<ContractDescriptor>
{
    internal ContractDescriptor(string identity, string source, string document, SourceSite site,
        NodeDescriptor[] nodes, CommandDescriptor[] commands, Issue[] issues)
    {
        Identity = identity; Source = source; Document = document; Site = site;
        Nodes = nodes; Commands = commands; Issues = issues;
        var key = new StringBuilder();
        Add(key, identity); Add(key, source); Add(key, document); site.AppendKey(key);
        foreach (var node in nodes) { Add(key, node.Name); Add(key, node.Kind.ToString()); Add(key, node.Parent.ToString()); Add(key, node.Maximum.ToString()); node.Site.AppendKey(key); }
        key.Append('|');
        foreach (var command in commands) { Add(key, command.Name); foreach (uint kind in command.Kinds) Add(key, kind.ToString()); key.Append('|'); command.Site.AppendKey(key); }
        key.Append('|');
        foreach (var issue in issues) { Add(key, issue.Id); Add(key, issue.Message); issue.Site.AppendKey(key); }
        Key = key.ToString();
    }
    internal string Identity { get; }
    internal string Source { get; }
    internal string Document { get; }
    internal SourceSite Site { get; }
    internal NodeDescriptor[] Nodes { get; }
    internal CommandDescriptor[] Commands { get; }
    internal Issue[] Issues { get; }
    private string Key { get; }
    public bool Equals(ContractDescriptor? other) => other is not null && Key == other.Key;
    public override bool Equals(object? obj) => Equals(obj as ContractDescriptor);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);
    internal static void Add(StringBuilder target, string text) => target.Append(text.Length).Append(':').Append(text);
}

internal readonly struct SourceSite
{
    internal SourceSite(string file, int start, int length, int startLine, int startColumn, int endLine, int endColumn)
    { File = file; Start = start; Length = length; StartLine = startLine; StartColumn = startColumn; EndLine = endLine; EndColumn = endColumn; }
    internal string File { get; }
    internal int Start { get; }
    internal int Length { get; }
    internal int StartLine { get; }
    internal int StartColumn { get; }
    internal int EndLine { get; }
    internal int EndColumn { get; }
    internal int Line => StartLine + 1;
    internal static SourceSite From(Location? location)
    {
        if (location is null || !location.IsInSource) return new SourceSite("", 0, 0, 0, 0, 0, 0);
        var line = location.GetLineSpan(); var span = location.SourceSpan;
        return new SourceSite(line.Path, span.Start, span.Length, line.StartLinePosition.Line, line.StartLinePosition.Character, line.EndLinePosition.Line, line.EndLinePosition.Character);
    }
    internal Location ToLocation() => string.IsNullOrEmpty(File) ? Location.None : Location.Create(File, new TextSpan(Start, Length), new LinePositionSpan(new LinePosition(StartLine, StartColumn), new LinePosition(EndLine, EndColumn)));
    internal void AppendKey(StringBuilder key)
    { ContractDescriptor.Add(key, File ?? ""); key.Append(Start).Append(',').Append(Length).Append(',').Append(StartLine).Append(',').Append(StartColumn).Append(',').Append(EndLine).Append(',').Append(EndColumn).Append(';'); }
}

internal sealed class NodeDescriptor
{
    internal NodeDescriptor(string name, uint kind, int parent, uint maximum, SourceSite site)
    { Name = name; Kind = kind; Parent = parent; Maximum = maximum; Site = site; }
    internal string Name { get; }
    internal uint Kind { get; }
    internal int Parent { get; }
    internal uint Maximum { get; }
    internal SourceSite Site { get; }
}
internal sealed class CommandDescriptor
{
    internal CommandDescriptor(string name, uint[] kinds, SourceSite site) { Name = name; Kinds = kinds; Site = site; }
    internal string Name { get; }
    internal uint[] Kinds { get; }
    internal SourceSite Site { get; }
}
internal sealed class Issue
{
    internal Issue(string id, string message, SourceSite site) { Id = id; Message = message; Site = site; }
    internal string Id { get; }
    internal string Message { get; }
    internal SourceSite Site { get; }
}
internal sealed class AdditionalDocument : IEquatable<AdditionalDocument>
{
    internal AdditionalDocument(string path, string? text) { Path = path; Text = text; }
    internal string Path { get; }
    internal string? Text { get; }
    public bool Equals(AdditionalDocument? other) => other is not null && Path == other.Path && Text == other.Text;
    public override bool Equals(object? obj) => Equals(obj as AdditionalDocument);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Path) ^ (Text is null ? 0 : StringComparer.Ordinal.GetHashCode(Text));
}
internal sealed class ContractInput : IEquatable<ContractInput>
{
    internal ContractInput(ContractDescriptor contract, AdditionalDocument? document, string? error)
    { Contract = contract; Document = document; Error = error; }
    internal ContractDescriptor Contract { get; }
    internal AdditionalDocument? Document { get; }
    internal string? Error { get; }
    public bool Equals(ContractInput? other) => other is not null && Contract.Equals(other.Contract) && Equals(Document, other.Document) && Error == other.Error;
    public override bool Equals(object? obj) => Equals(obj as ContractInput);
    public override int GetHashCode() => Contract.GetHashCode() ^ (Document?.GetHashCode() ?? 0) ^ (Error?.GetHashCode() ?? 0);
}
