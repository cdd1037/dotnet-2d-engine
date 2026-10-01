using System.Globalization;

namespace GameAuthoringLab;

/// <summary>
/// Bounded, schema-driven authoring profile. Binding IDs belong to the caller's copied schema;
/// all other IDs/classes describe static layout only. Native code owns generated list buttons.
/// XML and RCSS use the same bounded tokenizers and finite property grammar as existing profiles.
/// The only resource is the RML file's exact same-basename sibling stylesheet. These immutable
/// source snapshots, not another filesystem read, must be passed to native staging.
/// </summary>
internal static class BoundUiAuthoring
{
    private static readonly HashSet<string> BodyTags = new(StringComparer.Ordinal)
        { "div", "h1", "h2", "p", "label", "button", "input" };
    private static readonly string[] RangeParts = ["slidertrack", "sliderbar", "sliderprogress", "sliderarrowdec", "sliderarrowinc"];
    private static readonly string[] ScrollParts = ["scrollbarvertical", "scrollbarvertical slidertrack", "scrollbarvertical sliderbar",
        "scrollbarvertical sliderarrowdec", "scrollbarvertical sliderarrowinc"];

    public static (string Rml, string Rcss, string Stylesheet) ValidateAsset(
        AssetRoot assets, string logicalPath, IReadOnlyList<UiBindingTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ValidateSchema(targets);
        try { assets.ValidateLogicalPath(logicalPath); }
        catch (AssetException e) { throw new UiAuthoringException("UI_FILE", logicalPath, 1, 1, "$", e.Message, e); }
        string stylesheet = StylesheetFor(logicalPath);
        var files = UiAuthoring.ReadAssetFiles(assets, logicalPath, stylesheet);
        return Validate(files.Rml, files.Rcss, targets, files.RmlFile, files.RcssFile);
    }

    internal static (string Rml, string Rcss, string Stylesheet) Validate(ReadOnlySpan<byte> rml,
        ReadOnlySpan<byte> rcss, IReadOnlyList<UiBindingTarget> targets,
        string file = "bound.rml", string cssFile = "bound.rcss")
    {
        var schema = ValidateSchema(targets);
        string stylesheet = StylesheetFor(file);
        if (!string.Equals(Path.GetFileName(cssFile), stylesheet, StringComparison.Ordinal))
            throw new UiAuthoringException("UI_RESOURCE", file, 1, 1, "head/link@href", "Stylesheet must have the RML file's same basename.");
        string markup = UiAuthoring.Decode(rml, file), style = UiAuthoring.Decode(rcss, cssFile);
        var root = UiAuthoring.ParseXml(markup, file).Root
            ?? throw Error("UI_STRUCTURE", file, null, "Missing rml root.");
        Require(root.Name == "rml", "UI_STRUCTURE", file, root, "Expected rml root.");
        RequireChildren(root, ["head", "body"], file);
        var head = root.Element("head")!;
        var body = root.Element("body")!;
        RequireChildren(head, ["title", "link"], file);
        var title = head.Element("title")!;
        var link = head.Element("link")!;
        Require(!title.HasElements && !string.IsNullOrWhiteSpace(title.Value), "UI_STRUCTURE", file, title, "Expected a non-empty plain-text title.");
        Require(link.Attribute("type")?.Value == "text/rcss" && link.Attribute("href")?.Value == stylesheet,
            "UI_RESOURCE", file, link, "Only the exact sibling stylesheet '" + stylesheet + "' is allowed.");

        var ids = new Dictionary<string, UiXmlElement>(StringComparer.Ordinal);
        var classes = new HashSet<string>(StringComparer.Ordinal) { "bound-row", "bound-items" };
        foreach (UiXmlElement node in root.DescendantsAndSelf())
        {
            string tag = node.Name.LocalName;
            bool content = node.Ancestors().Contains(body);
            Require(node.Name.NamespaceName.Length == 0 && (content ? BodyTags.Contains(tag) :
                node == root || node == head || node == body || node == title || node == link),
                "UI_TAG", file, node, "Unknown, misplaced, or namespaced tag.");
            foreach (UiXmlAttribute attribute in node.Attributes())
            {
                string name = attribute.Name.LocalName;
                Require(!attribute.IsNamespaceDeclaration && attribute.Name.NamespaceName.Length == 0,
                    "UI_ATTRIBUTE", file, attribute, "Namespaces are unsupported.");
                Require(!name.StartsWith("on", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("data", StringComparison.OrdinalIgnoreCase),
                    "UI_BINDING", file, attribute, "Inline handlers and data bindings are unsupported.");
                bool allowed = name switch
                {
                    "id" or "class" => content,
                    "for" => content && tag == "label",
                    "href" => node == link,
                    "type" => node == link || content && tag == "input",
                    "value" or "maxlength" or "min" or "max" or "step" or "checked" => content && tag == "input",
                    "disabled" => content && tag is "input" or "button",
                    _ => false
                };
                Require(allowed, "UI_ATTRIBUTE", file, attribute, "Unknown or unsupported attribute.");
                Require(!HasBinding(attribute.Value), "UI_BINDING", file, attribute, "Template interpolation is unsupported.");
            }
            foreach (UiXmlText text in node.Nodes().OfType<UiXmlText>())
                Require(!HasBinding(text.Value), "UI_BINDING", file, text, "Template interpolation is unsupported.");
            if (!node.HasElements)
                Require(!HasBinding(node.Value), "UI_BINDING", file, node, "Template interpolation cannot be split across comments.");
            if (node.Attribute("id") is { } id)
            {
                Require(ValidIdentifier(id.Value), "UI_ID", file, id, "IDs must match [a-z][a-z0-9-]{0,46} (47 ASCII bytes maximum).");
                Require(ids.TryAdd(id.Value, node), "UI_ID", file, id, "Duplicate ID.");
            }
            if (node.Attribute("class") is { } cls)
            {
                string[] names = cls.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Require(names.Length is >= 1 and <= 8 && names.Distinct(StringComparer.Ordinal).Count() == names.Length
                    && names.All(ValidIdentifier), "UI_CLASS", file, cls, "Expected 1..8 unique ASCII class names.");
                Require(!names.Any(name => name is "bound-row" or "bound-items"), "UI_CLASS", file, cls, "The bound-row and bound-items classes are reserved for native list content.");
                foreach (string name in names) classes.Add(name);
            }
            if (tag is "title" or "h1" or "h2" or "p" or "label" or "button")
            {
                Require(!node.HasElements, "UI_STRUCTURE", file, node, "Text elements cannot contain nested markup.");
                CheckText(node.Value.Trim(), 512, 256, file, node);
            }
            if (tag is "input" or "link")
                Require(!node.HasElements && string.IsNullOrWhiteSpace(node.Value), "UI_STRUCTURE", file, node, "This element must be empty.");
            if (tag is "input" or "button")
                Require(node.Attribute("id") is { } controlId && schema.ContainsKey(controlId.Value),
                    "UI_BINDING", file, node, "Every interactive element must have a registered binding target ID.");
            if (node.Attribute("disabled") is { } disabled)
                Require(disabled.Value is "" or "disabled", "UI_VALUE", file, disabled, "Expected an empty disabled attribute or disabled=disabled.");
            if (content && tag == "div")
                foreach (UiXmlText text in node.Nodes().OfType<UiXmlText>())
                    CheckText(text.Value.Trim(), 512, 256, file, text);
        }
        Require(body.Nodes().OfType<UiXmlText>().All(t => string.IsNullOrWhiteSpace(t.Value)),
            "UI_STRUCTURE", file, body, "Place body text inside a supported content element.");

        foreach (var (id, kind) in schema)
        {
            Require(ids.TryGetValue(id, out UiXmlElement? node), "UI_REQUIRED", file, body, "Missing binding target '" + id + "'.");
            ValidateTarget(node!, kind, file);
        }
        foreach (UiXmlElement label in body.Descendants().Where(n => n.Name == "label"))
        {
            UiXmlAttribute? target = label.Attribute("for");
            Require(target is not null && schema.TryGetValue(target.Value, out UiBindingKind kind)
                && kind is UiBindingKind.TextInput or UiBindingKind.Boolean or UiBindingKind.Number,
                "UI_BINDING", file, label, "A label must refer to a registered input target.");
        }
        ValidateStyle(style, cssFile, ids, classes, schema);
        return (markup, style, stylesheet);
    }

    internal static Dictionary<string, UiBindingKind> ValidateSchema(IReadOnlyList<UiBindingTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        int count = targets.Count;
        if (count is < 1 or > 32) throw SchemaError("targets", "Expected 1..32 binding targets.");
        var schema = new Dictionary<string, UiBindingKind>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            var target = targets[i];
            if (!ValidIdentifier(target.ElementId)) throw SchemaError($"targets[{i}].elementId", "IDs must match [a-z][a-z0-9-]{0,46} (47 ASCII bytes maximum).");
            if (target.Kind is < UiBindingKind.Text or > UiBindingKind.List)
                throw SchemaError($"targets[{i}].kind", "Unknown binding kind.");
            if (!schema.TryAdd(target.ElementId, target.Kind)) throw SchemaError($"targets[{i}].elementId", "Duplicate binding target ID.");
        }
        return schema;
    }

    private static void ValidateTarget(UiXmlElement node, UiBindingKind kind, string file)
    {
        string tag = node.Name.LocalName;
        Require(!node.HasElements, "UI_BINDING", file, node, "Binding targets cannot contain nested authored elements.");
        bool correctTag = kind switch
        {
            UiBindingKind.Text => tag is "p" or "h1" or "h2" or "div",
            UiBindingKind.TextInput or UiBindingKind.Boolean or UiBindingKind.Number => tag == "input",
            UiBindingKind.Action => tag == "button",
            UiBindingKind.List => tag == "div",
            _ => false
        };
        Require(correctTag, "UI_BINDING", file, node, "Incorrect element type for registered " + kind + " target.");
        if (kind == UiBindingKind.List)
            Require(string.IsNullOrWhiteSpace(node.Value), "UI_BINDING", file, node, "List targets must be empty; native code owns list rows.");
        if (kind == UiBindingKind.Text) CheckText(node.Value.Trim(), 255, 255, file, node);
        if (tag != "input") return;

        string[] supported = kind switch
        {
            UiBindingKind.TextInput => ["id", "class", "type", "value", "maxlength", "disabled"],
            UiBindingKind.Boolean => ["id", "class", "type", "checked", "disabled"],
            UiBindingKind.Number => ["id", "class", "type", "value", "min", "max", "step", "disabled"],
            _ => []
        };
        foreach (UiXmlAttribute attribute in node.Attributes())
            Require(supported.Contains(attribute.Name.LocalName, StringComparer.Ordinal), "UI_ATTRIBUTE", file, attribute, "Attribute is unsupported for this input kind.");
        string expectedType = kind switch { UiBindingKind.TextInput => "text", UiBindingKind.Boolean => "checkbox", _ => "range" };
        Require(node.Attribute("type")?.Value == expectedType, "UI_VALUE", file, node, "Expected input type=" + expectedType + ".");
        if (kind == UiBindingKind.TextInput)
        {
            string? maximum = node.Attribute("maxlength")?.Value;
            Require(Integer(maximum, 1, 64), "UI_VALUE", file, node, "Text input maxlength must be an integer from 1 through 64.");
            CheckText(node.Attribute("value")?.Value ?? "", 255, int.Parse(maximum!, CultureInfo.InvariantCulture), file, node);
        }
        else if (kind == UiBindingKind.Boolean)
        {
            if (node.Attribute("checked") is { } check)
                Require(check.Value is "" or "checked", "UI_VALUE", file, check, "Expected an empty checked attribute or checked=checked.");
        }
        else
        {
            Require(node.Attribute("min")?.Value == "0" && node.Attribute("max")?.Value == "100" && node.Attribute("step")?.Value == "1",
                "UI_VALUE", file, node, "Number input requires min=0, max=100 and step=1.");
            if (node.Attribute("value") is { } value)
                Require(Integer(value.Value, 0, 100), "UI_VALUE", file, value, "Number input value must be an integer from 0 through 100.");
        }
    }

    private static void ValidateStyle(string style, string file, Dictionary<string, UiXmlElement> ids,
        HashSet<string> classes, Dictionary<string, UiBindingKind> schema)
    {
        var generated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, kind) in schema)
            foreach (string part in kind == UiBindingKind.Number ? RangeParts : kind == UiBindingKind.List ? ScrollParts : [])
                generated.Add("#" + id + " " + part);
        bool ButtonSelector(string selector) => selector == "button" || selector == ".bound-row" ||
            selector.StartsWith('#') && ids.TryGetValue(selector[1..], out var node) && node.Name == "button";
        bool Selector(string selector)
        {
            if (generated.Contains(selector)) return true;
            int colon = selector.IndexOf(':');
            if (colon >= 0)
            {
                string target = selector[..colon], pseudo = selector[(colon + 1)..];
                if (target == ".bound-row" && pseudo is "selected" or "disabled") return true;
                if (pseudo == "checked") return target == "input" || target.StartsWith('#') &&
                    schema.TryGetValue(target[1..], out var kind) && kind == UiBindingKind.Boolean;
                return pseudo is "hover" or "focus" or "active" or "disabled" &&
                    (ButtonSelector(target) || target == "input" || target.StartsWith('#') &&
                    ids.TryGetValue(target[1..], out var node) && node.Name == "input");
            }
            return selector == "body" || BodyTags.Contains(selector) ||
                selector.StartsWith('#') && ids.ContainsKey(selector[1..]) ||
                selector.StartsWith('.') && classes.Contains(selector[1..]);
        }
        bool Property(string selector, string property, string value)
        {
            if (property is "overflow-x" or "overflow-y")
                return selector.StartsWith('#') && schema.TryGetValue(selector[1..], out var kind) && kind == UiBindingKind.List
                    && UiAuthoring.ValidProperty("#item-list", property, value);
            if (property == "border-radius")
                return ButtonSelector(selector.Split(':')[0]) && UiAuthoring.ValidProperty("button", property, value);
            return UiAuthoring.ValidProperty(selector, property, value);
        }
        UiAuthoring.ValidateBoundStyle(style, file, Selector, Property);
    }

    private static string StylesheetFor(string file)
    {
        if (!file.EndsWith(".rml", StringComparison.Ordinal))
            throw new UiAuthoringException("UI_RESOURCE", file, 1, 1, "$", "Bound documents require an .rml filename.");
        return Path.GetFileName(file)[..^4] + ".rcss";
    }
    private static bool ValidIdentifier(string? value) => value is { Length: >= 1 and <= 47 }
        && value[0] is >= 'a' and <= 'z' && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    private static bool HasBinding(string value) => value.Contains("{{", StringComparison.Ordinal)
        || value.Contains("}}", StringComparison.Ordinal) || value.Contains("${", StringComparison.Ordinal);
    private static bool Integer(string? value, int min, int max) => value is { Length: >= 1 and <= 3 }
        && value.All(c => c is >= '0' and <= '9') && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
        && number >= min && number <= max;
    private static void RequireChildren(UiXmlElement node, string[] names, string file) =>
        Require(node.Elements().Select(child => child.Name.ToString()).SequenceEqual(names)
            && node.Nodes().OfType<UiXmlText>().All(t => string.IsNullOrWhiteSpace(t.Value)),
            "UI_STRUCTURE", file, node, "Expected only ordered children: " + string.Join(", ", names) + ".");
    private static void CheckText(string text, int maxBytes, int maxScalars, string file, UiXmlObject node)
    {
        try { UiSettingsContract.ValidateText(text, maxBytes, maxScalars, "text"); }
        catch (UiAuthoringException e) { throw Error("UI_VALUE", file, node, e.Cause); }
    }
    private static void Require(bool condition, string code, string file, UiXmlObject? node, string cause)
    { if (!condition) throw Error(code, file, node, cause); }
    private static UiAuthoringException SchemaError(string field, string cause) => new("UI_SCHEMA", "<bindings>", 1, 1, field, cause);
    private static UiAuthoringException Error(string code, string file, UiXmlObject? node, string cause)
    {
        UiXmlElement? element = node as UiXmlElement ?? node?.Parent;
        string field = element is null ? "$" : string.Join("/", element.AncestorsAndSelf().Reverse().Select(e =>
            e.Name.LocalName + (e.Attribute("id") is { } id ? "#" + id.Value[..Math.Min(id.Value.Length, 48)] : "")));
        if (node is UiXmlAttribute attribute) field += "@" + attribute.Name.LocalName;
        return new(code, file, node?.LineNumber ?? 1, node?.LinePosition ?? 1, field, cause);
    }
}
