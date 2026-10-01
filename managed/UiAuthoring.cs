using System.Globalization;
using System.Text;
using System.Xml;

namespace GameAuthoringLab;

/// <summary>A stable engine diagnostic, independent of RmlUi's best-effort log text.</summary>
internal sealed class UiAuthoringException : Exception
{
    public string Code { get; }
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }
    public string Field { get; }
    public string Cause { get; }

    public UiAuthoringException(string code, string filePath, int line, int column, string field, string cause,
        Exception? inner = null) : base($"{filePath}:{Math.Max(1, line)}:{Math.Max(1, column)} [{code}] {field}: {cause}", inner)
    {
        Code = code; FilePath = filePath; Line = Math.Max(1, line); Column = Math.Max(1, column);
        Field = field; Cause = cause;
    }
}

internal enum UiActionValueKind { None, Text, Integer }
internal readonly record struct UiActionBinding(string ElementId, string Event, string Action, UiActionValueKind ValueKind);

/// <summary>
/// Adapter contract, not reflection binding: listen by exact ID and copy values into the native queue.
/// Text changes mutate only the draft name; range changes mutate draft integer volume [0,100].
/// Apply commits the draft; Reset restores the sample defaults. Both are commands, not inline RML.
/// Model patches set player-name/volume VALUE and status TEXT. The list is authored source only;
/// this first native slice does not expose list mutation. Never interpret model strings as RML,
/// RCSS, selectors, or file names. Name is at most 32 Unicode scalars / 127 UTF-8 payload bytes;
/// status is at most 255 UTF-8 payload bytes (native buffers reserve another byte for NUL). The
/// authored list has at most 64 items of 256 UTF-8 bytes each. Reject invalid UTF-16 and controls.
/// Native ABI action numbers/generations/queue capacity are separately owned by the native adapter.
/// </summary>
internal static class UiSettingsContract
{
    public const string PlayerNameId = "player-name", VolumeId = "volume", ApplyId = "apply", ResetId = "reset",
        StatusId = "status", ItemListId = "item-list";
    public const string DefaultPlayerName = "Player / 玩家";
    public const int DefaultVolume = 65;
    private static readonly UiActionBinding[] Bindings =
    [
        new(PlayerNameId, "change", "changed", UiActionValueKind.Text),
        new(VolumeId, "change", "changed", UiActionValueKind.Integer),
        new(ApplyId, "click", "apply", UiActionValueKind.None),
        new(ResetId, "click", "reset", UiActionValueKind.None)
    ];
    public static ReadOnlySpan<UiActionBinding> Actions => Bindings;

    public static void ValidateModel(string playerName, int volume, string status)
    {
        ValidateText(playerName, 127, 32, "model.player-name");
        if (volume is < 0 or > 100) throw ModelError("model.volume", "Expected an integer from 0 through 100.");
        ValidateText(status, 255, 255, "model.status");
    }

    // The overload also validates a prospective authored list; it does not send native list patches.
    public static void ValidateModel(string playerName, int volume, string status, IReadOnlyList<string> items)
    {
        ValidateModel(playerName, volume, status);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count > 64) throw ModelError("model.item-list", "At most 64 plain-text items are supported.");
        for (int i = 0; i < items.Count; i++) ValidateText(items[i], 256, 256, $"model.item-list[{i}]");
    }

    internal static void ValidateText(string text, int maxBytes, int maxScalars, string field)
    {
        if (text is null) throw ModelError(field, "Text cannot be null.");
        // The UTF-16 length gate bounds both byte counting and scalar enumeration before allocation.
        if (text.Length > maxBytes) throw ModelError(field, $"Text exceeds {maxBytes} UTF-8 bytes.");
        try
        {
            if (UiAuthoring.StrictUtf8.GetByteCount(text) > maxBytes) throw ModelError(field, $"Text exceeds {maxBytes} UTF-8 bytes.");
        }
        catch (EncoderFallbackException) { throw ModelError(field, "Text contains invalid UTF-16."); }
        int scalars = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (Rune.IsControl(rune)) throw ModelError(field, "Control characters are not supported in model text.");
            if (++scalars > maxScalars) throw ModelError(field, $"Text exceeds {maxScalars} Unicode scalar values.");
        }
    }

    private static UiAuthoringException ModelError(string field, string cause) => new("UI_MODEL", "<model>", 1, 1, field, cause);
}

/// <summary>Immutable validated source snapshots. Load these exact strings to avoid a read/validate/load race.</summary>
internal sealed record UiValidatedDocument(string Rml, string Rcss, string RmlFile, string RcssFile,
    string PlayerName, int Volume, string Status, IReadOnlyList<string> Items);

/// <summary>
/// Strict SETTINGS PROFILE v1, deliberately NOT a general XML/RML/CSS validator.
/// Limits: 64 KiB UTF-8 per file, 256 elements, nesting depth 16, 128 rules, 32 declarations/rule.
/// XML 1.0, optional UTF-8 declaration/BOM; only XML built-ins/numeric character references; no DTD, PI, CDATA,
/// namespaces, inline styles, inline on* handlers, data-* bindings, interpolation, templates, images,
/// scripts, or remote resources. The sole link is type=text/rcss href=settings.rcss.
/// Tags/IDs/classes/attributes are the explicit finite sets below; required controls and labels are
/// checked structurally. These restrictions intentionally reject otherwise valid RmlUi documents.
/// RCSS grammar: selector { property: value; ... }, mandatory trailing semicolons, /* comments */.
/// Outside comments, tokens and whitespace are ASCII; comments appear between rules/declarations.
/// Selectors are one supported tag, #registered-id, .registered-class, button/input or their IDs with
/// :hover/:focus/:active, or the explicit generated range/scrollbar selectors below. No selector
/// lists, arbitrary combinators, at-rules, escapes, variables, functions, !important, or nested rules.
/// Properties/values: display block/inline-block/none; position relative/absolute; nonnegative integer
/// dp/px lengths [0,1920] (width/height also 0..100%); padding/margin and side variants 1..4 lengths
/// [0,64]; font-size 8..48dp/px; line-height 100..200%; font-family exactly "Noto Sans CJK SC";
/// font-weight normal; text-align left/center/right; word-break normal/break-all; vertical-align
/// top/middle/bottom; color/background-color/border-color #RRGGBB or transparent; border-width
/// 0..4dp/px; border-radius 0..12dp/px on button selectors ONLY; overflow-x hidden and overflow-y
/// auto/scroll on #item-list ONLY. No transform or clipping masks; the list is rectangular.
/// This proves only this finite grammar/contract. Native strict staging/load/update/render diagnostics
/// remain required; this is no certificate of upstream layout, font coverage, input, or IME behavior.
/// </summary>
internal static class UiAuthoring
{
    public const int MaxFileBytes = 64 * 1024;
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> Tags = new(StringComparer.Ordinal)
        { "rml", "head", "title", "link", "body", "div", "h1", "h2", "p", "label", "input", "button" };
    private static readonly Dictionary<string, string> RequiredIds = new(StringComparer.Ordinal)
    {
        ["settings-panel"] = "div", ["panel-title"] = "h1", ["name-label"] = "label", ["volume-label"] = "label",
        ["player-name"] = "input", ["volume"] = "input", ["apply"] = "button", ["reset"] = "button",
        ["status"] = "p", ["list-title"] = "h2", ["item-list"] = "div"
    };
    private static readonly HashSet<string> Classes = new(StringComparer.Ordinal) { "hint", "actions", "list-item" };
    private static readonly HashSet<string> StyleTags = new(StringComparer.Ordinal) { "body", "div", "h1", "h2", "p", "label", "input", "button" };
    private static readonly HashSet<string> GeneratedSelectors = new(StringComparer.Ordinal)
    {
        "#volume slidertrack", "#volume sliderbar", "#volume sliderprogress", "#volume sliderarrowdec", "#volume sliderarrowinc",
        "#item-list scrollbarvertical", "#item-list scrollbarvertical slidertrack", "#item-list scrollbarvertical sliderbar",
        "#item-list scrollbarvertical sliderarrowdec", "#item-list scrollbarvertical sliderarrowinc"
    };

    public static UiValidatedDocument ValidateFiles(string rmlPath, string? rcssPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rmlPath);
        try
        {
            string expected = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(rmlPath))!, "settings.rcss");
            rcssPath ??= expected;
            // A supplied stylesheet must be the linked sibling, not a different file validated by mistake.
            if (!string.Equals(Path.GetFullPath(rcssPath), expected, StringComparison.Ordinal))
                throw Error("UI_RESOURCE", rmlPath, 1, 1, "head/link@href", "Stylesheet must be the settings.rcss sibling of the RML file.");
        }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException)
        { throw Error("UI_FILE", rmlPath, 1, 1, "$", "Invalid source path: " + e.Message, e); }
        try
        {
            var assets = new AssetRoot(Path.GetDirectoryName(Path.GetFullPath(rmlPath))!);
            return ValidateAsset(assets, Path.GetFileName(rmlPath));
        }
        catch (Exception e) when (e is AssetException or ArgumentException or NotSupportedException)
        { throw Error("UI_FILE", rmlPath, 1, 1, "$", e.Message, e); }
    }

    public static UiValidatedDocument ValidateAsset(AssetRoot assets, string logicalPath)
    {
        var files = ReadAssetFiles(assets, logicalPath, "settings.rcss");
        return Validate(files.Rml, files.Rcss, files.RmlFile, files.RcssFile);
    }

    internal static (byte[] Rml, byte[] Rcss, string RmlFile, string RcssFile) ReadAssetFiles(
        AssetRoot assets, string logicalPath, string stylesheet)
    {
        string file = logicalPath;
        try
        {
            file = assets.Resolve(logicalPath);
            string css = assets.Resolve(assets.Sibling(logicalPath, stylesheet));
            return (ReadBounded(file), ReadBounded(css), file, css);
        }
        catch (AssetException e)
        { throw Error("UI_FILE", file, 1, 1, "$", e.Message, e); }
    }

    public static UiValidatedDocument Validate(ReadOnlySpan<byte> rml, ReadOnlySpan<byte> rcss,
        string rmlFile = "settings.rml", string rcssFile = "settings.rcss")
    {
        string markup = Decode(rml, rmlFile), style = Decode(rcss, rcssFile);
        UiXmlDocument document = ParseXml(markup, rmlFile);
        var ids = ValidateMarkup(document, rmlFile);
        new StyleParser(style, rcssFile).Parse();
        string name = ids["player-name"].Attribute("value")!.Value;
        int volume = int.Parse(ids["volume"].Attribute("value")!.Value, CultureInfo.InvariantCulture);
        string status = ids["status"].Value.Trim();
        string[] items = ids["item-list"].Elements().Select(e => e.Value.Trim()).ToArray();
        return new(markup, style, rmlFile, rcssFile, name, volume, status, Array.AsReadOnly(items));
    }

    internal static byte[] ReadBounded(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > MaxFileBytes) throw Error("UI_SIZE", path, 1, 1, "$", $"File must contain 1..{MaxFileBytes} bytes.");
            byte[] bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw Error("UI_SIZE", path, 1, 1, "$", "File grew while being read.");
            return bytes;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { throw Error("UI_FILE", path, 1, 1, "$", e.Message, e); }
    }

    internal static string Decode(ReadOnlySpan<byte> bytes, string file)
    {
        if (bytes.Length is <= 0 or > MaxFileBytes) throw Error("UI_SIZE", file, 1, 1, "$", $"File must contain 1..{MaxFileBytes} bytes.");
        try
        {
            string value = StrictUtf8.GetString(bytes);
            return value.Length > 0 && value[0] == '\uFEFF' ? value[1..] : value;
        }
        catch (DecoderFallbackException e) { throw Error("UI_UTF8", file, 1, Math.Max(1, e.Index + 1), "$", "Invalid UTF-8 byte sequence.", e); }
    }

    internal static UiXmlDocument ParseXml(string source, string file)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaxFileBytes, MaxCharactersFromEntities = 1024, IgnoreComments = false };
        try
        {
            // Framework XML tokenization with a bounded profile-only model. No general LINQ-to-XML DOM.
            var document = new UiXmlDocument();
            var parents = new Stack<UiXmlElement>();
            string? version = null, encoding = null;
            using var reader = XmlReader.Create(new StringReader(source), settings);
            int elements = 0;
            while (reader.Read())
            {
                var line = (IXmlLineInfo)reader;
                int row = line.LineNumber, column = line.LinePosition;
                if (reader.Depth > 16 || reader.NodeType == XmlNodeType.Element && ++elements > 256)
                    throw Error("UI_LIMIT", file, row, column, "$", "Maximum depth 16 or element count 256 exceeded.");
                if (reader.NodeType is XmlNodeType.ProcessingInstruction or XmlNodeType.CDATA)
                    throw Error("UI_XML_PROFILE", file, row, column, "$", "Processing instructions and CDATA are unsupported.");
                switch (reader.NodeType)
                {
                    case XmlNodeType.XmlDeclaration:
                        version = reader.GetAttribute("version"); encoding = reader.GetAttribute("encoding"); break;
                    case XmlNodeType.Element:
                        bool empty = reader.IsEmptyElement;
                        var element = new UiXmlElement(new(reader.LocalName, reader.NamespaceURI), row, column);
                        if (parents.Count != 0) parents.Peek().Add(element); else document.Root = element;
                        if (reader.MoveToFirstAttribute())
                        {
                            do {
                                element.AddAttribute(new UiXmlAttribute(new(reader.LocalName, reader.NamespaceURI), reader.Value,
                                    reader.NamespaceURI == "http://www.w3.org/2000/xmlns/", line.LineNumber, line.LinePosition));
                            } while (reader.MoveToNextAttribute());
                            reader.MoveToElement();
                        }
                        if (!empty) parents.Push(element);
                        break;
                    case XmlNodeType.EndElement:
                        parents.Pop(); break;
                    case XmlNodeType.Text:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        if (parents.Count != 0) parents.Peek().Add(new UiXmlText(reader.Value, row, column));
                        break;
                    // Comments are permitted but never interpreted. Separate text tokens stay separate.
                }
            }
            if (version is not null && (version != "1.0" || encoding is not null && !encoding.Equals("utf-8", StringComparison.OrdinalIgnoreCase)))
                throw Error("UI_XML_PROFILE", file, 1, 1, "$declaration", "Only XML 1.0 with UTF-8 encoding is supported.");
            return document;
        }
        catch (XmlException e) { throw Error("UI_XML", file, e.LineNumber, e.LinePosition, "$xml", e.Message, e); }
    }

    private static Dictionary<string, UiXmlElement> ValidateMarkup(UiXmlDocument document, string file)
    {
        UiXmlElement root = document.Root ?? throw Error("UI_STRUCTURE", file, 1, 1, "$", "Missing rml root.");
        if (root.Name != "rml") throw NodeError("UI_STRUCTURE", file, root, "Expected rml root.");
        var ids = new Dictionary<string, UiXmlElement>(StringComparer.Ordinal);
        foreach (UiXmlElement e in root.DescendantsAndSelf())
        {
            string tag = e.Name.LocalName;
            if (e.Name.NamespaceName.Length != 0 || !Tags.Contains(tag)) throw NodeError("UI_TAG", file, e, "Unknown or namespaced tag.");
            foreach (UiXmlAttribute a in e.Attributes())
            {
                string attr = a.Name.LocalName;
                if (a.IsNamespaceDeclaration || a.Name.NamespaceName.Length != 0) throw NodeError("UI_ATTRIBUTE", file, a, "Namespaces are unsupported.");
                if (attr.StartsWith("on", StringComparison.OrdinalIgnoreCase) || attr.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
                    throw NodeError("UI_BINDING", file, a, "Inline events and data bindings are unsupported; native listeners use registered IDs.");
                bool allowed = attr switch
                {
                    "id" => tag is "div" or "h1" or "h2" or "p" or "label" or "input" or "button",
                    "class" => tag is "div" or "p",
                    "for" => tag == "label",
                    "href" => tag == "link",
                    "type" => tag is "link" or "input",
                    "value" or "maxlength" or "min" or "max" or "step" => tag == "input",
                    _ => false
                };
                if (!allowed) throw NodeError("UI_ATTRIBUTE", file, a, "Unknown or unsupported attribute.");
                if (HasBinding(a.Value)) throw NodeError("UI_BINDING", file, a, "Template interpolation is unsupported.");
            }
            foreach (UiXmlText text in e.Nodes().OfType<UiXmlText>())
                if (HasBinding(text.Value)) throw NodeError("UI_BINDING", file, text, "Template interpolation is unsupported.");
            if (e.Attribute("id") is { } id)
            {
                if (!RequiredIds.TryGetValue(id.Value, out string? requiredTag) || requiredTag != tag)
                    throw NodeError("UI_ID", file, id, "Unknown ID or incorrect element type for registered ID.");
                if (!ids.TryAdd(id.Value, e)) throw NodeError("UI_ID", file, id, "Duplicate ID.");
            }
            if (e.Attribute("class") is { } cls && (!Classes.Contains(cls.Value) ||
                cls.Value == "hint" && tag != "p" || cls.Value is "actions" or "list-item" && tag != "div"))
                throw NodeError("UI_CLASS", file, cls, "Expected one registered class on its supported element type.");
            if (tag is "title" or "h1" or "h2" or "p" or "label" or "button")
            {
                if (e.HasElements || string.IsNullOrWhiteSpace(e.Value)) throw NodeError("UI_STRUCTURE", file, e, "Expected non-empty plain text with no nested elements.");
                CheckText(e.Value.Trim(), 256, 256, file, e);
            }
            if (tag is "input" or "link" && (e.HasElements || !string.IsNullOrWhiteSpace(e.Value)))
                throw NodeError("UI_STRUCTURE", file, e, "This element must be empty.");
            if (tag is "input" or "button" or "label" && e.Attribute("id") is null)
                throw NodeError("UI_ID", file, e, "Every interactive element and label requires a registered ID.");
        }
        RequireChildren(root, ["head", "body"], file);
        UiXmlElement head = root.Element("head")!, body = root.Element("body")!;
        RequireChildren(head, ["title", "link"], file);
        UiXmlElement link = head.Element("link")!;
        RequireAttribute(link, "type", "text/rcss", file);
        RequireAttribute(link, "href", "settings.rcss", file, "UI_RESOURCE");
        foreach ((string id, string tag) in RequiredIds)
            if (!ids.ContainsKey(id)) throw NodeError("UI_REQUIRED", file, body, $"Missing {tag} with required ID '{id}'.");
        if (body.Elements().Count() != 1 || body.Elements().Single() != ids["settings-panel"])
            throw NodeError("UI_STRUCTURE", file, body, "Body must contain exactly the settings-panel div.");
        foreach (UiXmlElement e in ids.Values)
            if (e != ids["settings-panel"] && !e.Ancestors().Contains(ids["settings-panel"]))
                throw NodeError("UI_STRUCTURE", file, e, "Registered content must be within settings-panel.");
        foreach (UiXmlElement e in body.Descendants())
            if (e.Name.LocalName is "rml" or "head" or "body" or "title" or "link")
                throw NodeError("UI_STRUCTURE", file, e, "Document metadata is not allowed inside body.");
        RequireAttribute(ids["name-label"], "for", "player-name", file);
        RequireAttribute(ids["volume-label"], "for", "volume", file);
        UiXmlElement name = ids["player-name"], volume = ids["volume"];
        RequireAttribute(name, "type", "text", file); RequireAttribute(name, "maxlength", "32", file);
        RejectAttributes(name, ["min", "max", "step"], file);
        UiXmlAttribute nameValue = RequireAttribute(name, "value", null, file);
        CheckText(nameValue.Value, 127, 32, file, nameValue);
        CheckText(ids["status"].Value.Trim(), 255, 255, file, ids["status"]);
        RequireAttribute(volume, "type", "range", file); RequireAttribute(volume, "min", "0", file);
        RequireAttribute(volume, "max", "100", file); RequireAttribute(volume, "step", "1", file);
        RejectAttributes(volume, ["maxlength"], file);
        UiXmlAttribute volumeValue = RequireAttribute(volume, "value", null, file);
        if (!Integer(volumeValue.Value, 0, 100)) throw NodeError("UI_VALUE", file, volumeValue, "Volume must be an integer from 0 through 100.");
        UiXmlElement list = ids["item-list"];
        if (list.Elements().Count() is < 1 or > 64) throw NodeError("UI_LIMIT", file, list, "The initial list must contain 1..64 items.");
        foreach (UiXmlElement item in list.Elements())
        {
            if (item.Name != "div" || item.Attribute("class")?.Value != "list-item" || item.Attributes().Count() != 1 || item.HasElements)
                throw NodeError("UI_STRUCTURE", file, item, "List items must be plain-text divs with only class=list-item.");
            CheckText(item.Value.Trim(), 256, 256, file, item);
        }
        return ids;
    }

    private static void RequireChildren(UiXmlElement e, string[] names, string file)
    {
        if (!e.Elements().Select(child => child.Name.ToString()).SequenceEqual(names) ||
            e.Nodes().OfType<UiXmlText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)))
            throw NodeError("UI_STRUCTURE", file, e, "Expected only ordered children: " + string.Join(", ", names) + ".");
    }
    private static UiXmlAttribute RequireAttribute(UiXmlElement e, string name, string? value, string file, string code = "UI_VALUE")
    {
        UiXmlAttribute? a = e.Attribute(name);
        if (a is null) throw NodeError(code, file, e, $"Required attribute '{name}' is missing.");
        if (value is not null && a.Value != value) throw NodeError(code, file, a, $"Expected exact value '{value}'.");
        return a;
    }
    private static void RejectAttributes(UiXmlElement e, string[] attributes, string file)
    {
        foreach (string name in attributes) if (e.Attribute(name) is { } a) throw NodeError("UI_ATTRIBUTE", file, a, "Attribute is unsupported for this input type.");
    }
    private static bool HasBinding(string value) => value.Contains("{{", StringComparison.Ordinal) || value.Contains("}}", StringComparison.Ordinal);
    private static void CheckText(string text, int bytes, int scalars, string file, UiXmlObject location)
    {
        try { UiSettingsContract.ValidateText(text, bytes, scalars, "text"); }
        catch (UiAuthoringException e) { throw NodeError("UI_VALUE", file, location, e.Cause); }
    }
    private static UiAuthoringException NodeError(string code, string file, UiXmlObject node, string cause)
    {
        var line = (IXmlLineInfo)node;
        UiXmlElement? element = node as UiXmlElement ?? (node as UiXmlAttribute)?.Parent ?? (node as UiXmlNode)?.Parent;
        string field = element is null ? "$" : string.Join("/", element.AncestorsAndSelf().Reverse().Select(e =>
            e.Name.LocalName + (e.Attribute("id") is { } id ? "#" + Clip(id.Value, 64) : "")));
        if (node is UiXmlAttribute attr) field += "@" + attr.Name.LocalName;
        return Error(code, file, line.LineNumber, line.LinePosition, field, cause);
    }
    private static UiAuthoringException Error(string code, string file, int line, int column, string field, string cause, Exception? inner = null)
        => new(code, file, line, column, Clip(field, 256), Clip(cause, 512), inner);
    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "…";
    private static bool Integer(string s, int min, int max) => s.Length is > 0 and <= 5 &&
        s.All(c => c is >= '0' and <= '9') && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= min && n <= max;

    internal static void ValidateGameStyle(string source,string file)=>new StyleParser(source,file,true).Parse();

    internal static void ValidateBoundStyle(string source, string file, Func<string, bool> selectors,
        Func<string, string, string, bool> properties) => new StyleParser(source, file,
            selectorAllowed: selectors, propertyAllowed: properties).Parse();

    private sealed class StyleParser(string source, string file, bool gameProfile=false,
        Func<string, bool>? selectorAllowed=null, Func<string, string, string, bool>? propertyAllowed=null)
    {
        private int _position, _line = 1, _column = 1;
        public void Parse()
        {
            int rules = 0;
            SkipTrivia();
            while (_position < source.Length)
            {
                if (++rules > 128) Fail("UI_LIMIT", "$rcss", "Maximum 128 rules exceeded.");
                int selectorLine = _line, selectorColumn = _column;
                string selector = ReadUntil('{', "selector").Trim();
                if (!(selectorAllowed?.Invoke(selector) ?? (gameProfile?GameUiAuthoring.IsStyleSelector(selector):ValidSelector(selector)))) FailAt("UI_SELECTOR", selectorLine, selectorColumn, selector, "Unsupported selector.");
                Advance(); SkipTrivia();
                var properties = new HashSet<string>(StringComparer.Ordinal);
                while (_position < source.Length && source[_position] != '}')
                {
                    int propertyLine = _line, propertyColumn = _column;
                    string property = ReadUntil(':', selector).Trim(); Advance(); SkipTrivia();
                    string value = ReadUntil(';', selector + "/" + property).Trim(); Advance();
                    if (!properties.Add(property)) FailAt("UI_PROPERTY", propertyLine, propertyColumn, selector + "/" + property, "Duplicate property within one rule.");
                    if (properties.Count > 32) Fail("UI_LIMIT", selector, "Maximum 32 declarations per rule exceeded.");
                    if (!(propertyAllowed?.Invoke(selector, property, value) ?? ValidProperty(selector, property, value))) FailAt("UI_PROPERTY", propertyLine, propertyColumn, selector + "/" + property,
                        "Unknown property or unsupported value in " + (propertyAllowed is null ? "settings" : "bound") + " style profile v1: " + Clip(value, 80));
                    SkipTrivia();
                }
                if (_position == source.Length) Fail("UI_RCSS", selector, "Missing closing brace.");
                if (properties.Count == 0) Fail("UI_RCSS", selector, "Empty rules are unsupported.");
                Advance(); SkipTrivia();
            }
            if (rules == 0) Fail("UI_RCSS", "$rcss", "At least one style rule is required.");
        }
        private string ReadUntil(char delimiter, string field)
        {
            int start = _position;
            while (_position < source.Length && source[_position] != delimiter)
            {
                char c = source[_position];
                if (c > 127) Fail("UI_RCSS", field, "Only ASCII tokens/whitespace are supported outside comments.");
                if (c is '{' or '}' or ';' or ':' or '@' or '\\' or '(' or ')' or '!' or '<' or '>' or ',' ||
                    char.IsControl(c) && c is not '\r' and not '\n' and not '\t')
                {
                    // A single pseudo-class colon is part of an approved selector, not CSS syntax.
                    if (!(delimiter == '{' && c == ':')) Fail("UI_RCSS", field, "Unsupported token or missing delimiter '" + delimiter + "'.");
                }
                // Comments are allowed only between grammar tokens, never spliced into names/values.
                if (c == '/' && _position + 1 < source.Length && source[_position + 1] == '*')
                    Fail("UI_RCSS", field, "Comments must be between rules or declarations.");
                Advance();
            }
            if (_position == source.Length) Fail("UI_RCSS", field, "Missing delimiter '" + delimiter + "'.");
            return source[start.._position];
        }
        private void SkipTrivia()
        {
            while (_position < source.Length)
            {
                if (source[_position] is ' ' or '\t' or '\r' or '\n') { Advance(); continue; }
                if (source[_position] != '/' || _position + 1 == source.Length || source[_position + 1] != '*') return;
                Advance(); Advance();
                while (_position + 1 < source.Length && !(source[_position] == '*' && source[_position + 1] == '/')) Advance();
                if (_position + 1 >= source.Length) Fail("UI_RCSS", "$comment", "Unterminated comment.");
                Advance(); Advance();
            }
        }
        private void Advance()
        {
            if (source[_position++] == '\n') { _line++; _column = 1; } else _column++;
        }
        private void Fail(string code, string field, string cause) => throw Error(code, file, _line, _column, field, cause);
        private void FailAt(string code, int line, int column, string field, string cause) => throw Error(code, file, line, column, field, cause);
    }

    private static bool ValidSelector(string selector)
    {
        if (GeneratedSelectors.Contains(selector)) return true;
        int colon = selector.IndexOf(':');
        if (colon >= 0)
        {
            if (selector[(colon + 1)..] is not ("hover" or "focus" or "active")) return false;
            return selector[..colon] is "button" or "input" or "#apply" or "#reset" or "#player-name" or "#volume";
        }
        return StyleTags.Contains(selector) || selector.StartsWith('#') && RequiredIds.ContainsKey(selector[1..]) ||
            selector.StartsWith('.') && Classes.Contains(selector[1..]);
    }
    internal static bool ValidProperty(string selector, string property, string value)
    {
        switch (property)
        {
            case "display": return value is "block" or "inline-block" or "none";
            case "position": return value is "relative" or "absolute";
            case "width": case "height": return Length(value, 0, 1920, true);
            case "left": case "top": case "right": case "bottom": case "min-width": case "max-width": case "min-height": case "max-height":
                return Length(value, 0, 1920, false);
            case "padding": case "margin": return Lengths(value, 64, 4);
            case "padding-left": case "padding-right": case "padding-top": case "padding-bottom":
            case "margin-left": case "margin-right": case "margin-top": case "margin-bottom": return Lengths(value, 64, 1);
            case "font-size": return Length(value, 8, 48, false);
            case "font-family": return value == "\"Noto Sans CJK SC\"";
            case "font-weight": return value == "normal";
            case "line-height": return value.EndsWith('%') && Integer(value[..^1], 100, 200);
            case "text-align": return value is "left" or "center" or "right";
            case "vertical-align": return value is "top" or "middle" or "bottom";
            case "word-break": return value is "normal" or "break-all";
            case "color": case "background-color": case "border-color": return value == "transparent" ||
                value.Length == 7 && value[0] == '#' && value.Skip(1).All(char.IsAsciiHexDigit);
            case "border-width": return Length(value, 0, 4, false);
            case "border-radius":
                string target = selector.Split(':')[0];
                return target is "button" or "#apply" or "#reset" && Length(value, 0, 12, false);
            case "overflow-x": return selector == "#item-list" && value == "hidden";
            case "overflow-y": return selector == "#item-list" && value is "auto" or "scroll";
            default: return false;
        }
    }
    private static bool Length(string value, int min, int max, bool percentage) =>
        percentage && value.EndsWith('%') ? Integer(value[..^1], 0, 100) :
        value.Length > 2 && (value.EndsWith("dp", StringComparison.Ordinal) || value.EndsWith("px", StringComparison.Ordinal)) && Integer(value[..^2], min, max);
    private static bool Lengths(string value, int max, int count)
    {
        string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 1 && parts.Length <= count && parts.All(v => Length(v, 0, max, false));
    }
}
