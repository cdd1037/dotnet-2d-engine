using System.Text;
using System.Xml;
namespace GameAuthoringLab;
// Bounded validation records, not a public DOM or an XML syntax parser.
// All tokenization, XML well-formedness, namespaces and entity normalization stay in XmlReader.
internal readonly record struct UiXmlName(string LocalName, string NamespaceName)
{
    public static bool operator ==(UiXmlName name, string value) => name.NamespaceName.Length == 0 && name.LocalName == value;
    public static bool operator !=(UiXmlName name, string value) => !(name == value);
    public override string ToString() => NamespaceName.Length == 0 ? LocalName : "{" + NamespaceName + "}" + LocalName;
}
internal abstract class UiXmlObject(int line, int column) : IXmlLineInfo
{
    public UiXmlElement? Parent { get; internal set; }
    public int LineNumber => line;
    public int LinePosition => column;
    public bool HasLineInfo() => true;
}
internal abstract class UiXmlNode(int line, int column) : UiXmlObject(line, column);
internal sealed class UiXmlText(string value, int line, int column) : UiXmlNode(line, column)
{
    public string Value => value;
}
internal sealed class UiXmlAttribute(UiXmlName name, string value, bool isNamespaceDeclaration, int line, int column) : UiXmlObject(line, column)
{
    public UiXmlName Name => name;
    public string Value => value;
    public bool IsNamespaceDeclaration => isNamespaceDeclaration;
}
internal sealed class UiXmlElement(UiXmlName name, int line, int column) : UiXmlNode(line, column)
{
    private readonly List<UiXmlNode> _nodes = [];
    private readonly List<UiXmlAttribute> _attributes = [];
    public UiXmlName Name => name;
    internal void Add(UiXmlNode node) { node.Parent = this; _nodes.Add(node); }
    internal void AddAttribute(UiXmlAttribute attribute) { attribute.Parent = this; _attributes.Add(attribute); }
    public IEnumerable<UiXmlNode> Nodes() => _nodes;
    public IEnumerable<UiXmlAttribute> Attributes() => _attributes;
    public UiXmlAttribute? Attribute(string name) { foreach(var a in _attributes) if(a.Name == name) return a; return null; }
    public IEnumerable<UiXmlElement> Elements() { foreach(var node in _nodes) if(node is UiXmlElement child) yield return child; }
    public UiXmlElement? Element(string name) { foreach(var child in Elements()) if(child.Name == name) return child; return null; }
    public bool HasElements { get { foreach(var node in _nodes) if(node is UiXmlElement) return true; return false; } }
    public IEnumerable<UiXmlElement> Ancestors() { for(var e=Parent;e is not null;e=e.Parent) yield return e; }
    public IEnumerable<UiXmlElement> AncestorsAndSelf() { yield return this; foreach(var e in Ancestors()) yield return e; }
    public IEnumerable<UiXmlElement> Descendants() { foreach(var e in Elements()) { yield return e; foreach(var descendant in e.Descendants()) yield return descendant; } }
    public IEnumerable<UiXmlElement> DescendantsAndSelf() { yield return this; foreach(var e in Descendants()) yield return e; }
    public string Value { get { var text=new StringBuilder(); AppendText(text); return text.ToString(); } }
    private void AppendText(StringBuilder text) { foreach(var node in _nodes) { if(node is UiXmlText t) text.Append(t.Value); else if(node is UiXmlElement e) e.AppendText(text); } }
}
internal sealed class UiXmlDocument { public UiXmlElement? Root { get; internal set; } }

// Shared structural predicate; each closed profile owns its allowed names and diagnostics.
internal static class UiXmlStructure
{
    internal static bool HasOrderedChildren(UiXmlElement node,ReadOnlySpan<string> names)
    {
        int index=0;
        foreach(var child in node.Elements())
            if(index>=names.Length||child.Name!=names[index++])return false;
        return index==names.Length&&node.Nodes().OfType<UiXmlText>().All(text=>string.IsNullOrWhiteSpace(text.Value));
    }
}
