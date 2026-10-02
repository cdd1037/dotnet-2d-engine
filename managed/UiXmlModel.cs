using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace GameAuthoringLab;

// Bounded validation records, not a public DOM or an XML syntax parser.
// All tokenization, XML well-formedness, namespaces and entity normalization stay in XmlReader.
// This file is source-shared with the netstandard2.0 build-time validator.
internal readonly struct UiXmlName : IEquatable<UiXmlName>
{
    internal UiXmlName(string localName, string namespaceName) { LocalName = localName; NamespaceName = namespaceName; }
    public string LocalName { get; }
    public string NamespaceName { get; }
    public static bool operator ==(UiXmlName name, string value) => name.NamespaceName.Length == 0 && name.LocalName == value;
    public static bool operator !=(UiXmlName name, string value) => !(name == value);
    public static bool operator ==(UiXmlName left, UiXmlName right) => left.Equals(right);
    public static bool operator !=(UiXmlName left, UiXmlName right) => !left.Equals(right);
    public bool Equals(UiXmlName other) => LocalName == other.LocalName && NamespaceName == other.NamespaceName;
    public override bool Equals(object? obj) => obj is UiXmlName other && Equals(other);
    public override int GetHashCode() => unchecked(((LocalName?.GetHashCode() ?? 0) * 397) ^ (NamespaceName?.GetHashCode() ?? 0));
    public override string ToString() => NamespaceName.Length == 0 ? LocalName : "{" + NamespaceName + "}" + LocalName;
}

internal abstract class UiXmlObject : IXmlLineInfo
{
    protected UiXmlObject(int line, int column) { LineNumber = line; LinePosition = column; }
    public UiXmlElement? Parent { get; internal set; }
    public int LineNumber { get; }
    public int LinePosition { get; }
    public bool HasLineInfo() => true;
}

internal abstract class UiXmlNode : UiXmlObject
{
    protected UiXmlNode(int line, int column) : base(line, column) { }
}

internal sealed class UiXmlText : UiXmlNode
{
    internal UiXmlText(string value, int line, int column, bool hasRawInterpolation = false) : base(line, column)
    { Value = value; HasRawInterpolation = hasRawInterpolation; }
    public string Value { get; }
    // Native chooses its text view before decoding entities. Decoded literal braces alone
    // must not make preflight interpret text that RmlUi leaves literal.
    public bool HasRawInterpolation { get; }
}

internal sealed class UiXmlAttribute : UiXmlObject
{
    internal UiXmlAttribute(UiXmlName name, string value, bool isNamespaceDeclaration, int line, int column) : base(line, column)
    { Name = name; Value = value; IsNamespaceDeclaration = isNamespaceDeclaration; }
    public UiXmlName Name { get; }
    public string Value { get; }
    public bool IsNamespaceDeclaration { get; }
}

internal sealed class UiXmlElement : UiXmlNode
{
    private readonly List<UiXmlNode> _nodes = new List<UiXmlNode>();
    private readonly List<UiXmlAttribute> _attributes = new List<UiXmlAttribute>();
    internal UiXmlElement(UiXmlName name, int line, int column) : base(line, column) { Name = name; }
    public UiXmlName Name { get; }
    internal void Add(UiXmlNode node) { node.Parent = this; _nodes.Add(node); }
    internal void AddAttribute(UiXmlAttribute attribute) { attribute.Parent = this; _attributes.Add(attribute); }
    public IEnumerable<UiXmlNode> Nodes() => _nodes;
    public IEnumerable<UiXmlAttribute> Attributes() => _attributes;
    public UiXmlAttribute? Attribute(string name) { foreach (var a in _attributes) if (a.Name == name) return a; return null; }
    public IEnumerable<UiXmlElement> Elements() { foreach (var node in _nodes) if (node is UiXmlElement child) yield return child; }
    public UiXmlElement? Element(string name) { foreach (var child in Elements()) if (child.Name == name) return child; return null; }
    public bool HasElements { get { foreach (var node in _nodes) if (node is UiXmlElement) return true; return false; } }
    public IEnumerable<UiXmlElement> Ancestors() { for (var e = Parent; e is not null; e = e.Parent) yield return e; }
    public IEnumerable<UiXmlElement> AncestorsAndSelf() { yield return this; foreach (var e in Ancestors()) yield return e; }
    public IEnumerable<UiXmlElement> Descendants() { foreach (var e in Elements()) { yield return e; foreach (var descendant in e.Descendants()) yield return descendant; } }
    public IEnumerable<UiXmlElement> DescendantsAndSelf() { yield return this; foreach (var e in Descendants()) yield return e; }
    public string Value { get { var text = new StringBuilder(); AppendText(text); return text.ToString(); } }
    private void AppendText(StringBuilder text) { foreach (var node in _nodes) { if (node is UiXmlText t) text.Append(t.Value); else if (node is UiXmlElement e) e.AppendText(text); } }
}

internal sealed class UiXmlDocument { public UiXmlElement? Root { get; internal set; } }
