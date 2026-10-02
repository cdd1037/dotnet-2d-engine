using System.Text;

namespace GameAuthoringLab;

/// <summary>
/// Normal RmlUi markup and styles, with a project-resource and command boundary rather than a
/// widget/property schema. C# owns the model; RmlUi owns read expressions, layout and authored
/// subtrees. This is trusted project authoring, not an arbitrary-untrusted-document sandbox.
/// The shared XML reader bounds source size, depth and element count. Native strict staging
/// must still parse, update and render these exact snapshots before publishing them.
/// </summary>
internal static class UiModelAuthoring
{
    internal static BoundUiDocument ValidateAsset(AssetRoot assets, string logicalPath,
        IReadOnlyList<string> commandNames, IReadOnlyList<string>? declaredImages = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        try { assets.ValidateLogicalPath(logicalPath); }
        catch (AssetException e) { throw new UiAuthoringException("UI_FILE", logicalPath, 1, 1, "$", e.Message, e); }
        string stylesheet = StylesheetFor(logicalPath);
        var files = UiSourceFiles.ReadAssetFiles(assets, logicalPath, stylesheet);
        var source = Validate(files.Rml, files.Rcss, commandNames, files.RmlFile, files.RcssFile, declaredImages);
        return source with { Images = UiImageResources.Read(assets, logicalPath, source.References) };
    }

    internal static BoundUiDocument Validate(ReadOnlySpan<byte> rml, ReadOnlySpan<byte> rcss,
        IReadOnlyList<string> commandNames, string file = "model.rml", string cssFile = "model.rcss",
        IReadOnlyList<string>? declaredImages = null)
    {
        var commands = ValidateCommands(commandNames);
        string stylesheet = StylesheetFor(file);
        if (!string.Equals(Path.GetFileName(cssFile), stylesheet, StringComparison.Ordinal))
            throw new UiAuthoringException("UI_RESOURCE", file, 1, 1, "head/link@href", "Stylesheet must have the RML file's same basename.");
        string markup = UiSourceFiles.Decode(rml, file), style = UiSourceFiles.Decode(rcss, cssFile);
        var root = UiAuthoring.ParseXml(markup, file).Root
            ?? throw Error("UI_STRUCTURE", file, null, "Missing rml root.");
        Require(root.Name == "rml", "UI_STRUCTURE", file, root, "Expected rml root.");
        Require(UiXmlStructure.HasOrderedChildren(root, ["head", "body"]), "UI_STRUCTURE", file, root,
            "Expected one head followed by one body.");
        var head = root.Element("head")!;
        var body = root.Element("body")!;
        Require(head.Nodes().OfType<UiXmlText>().All(t => string.IsNullOrWhiteSpace(t.Value)),
            "UI_STRUCTURE", file, head, "Place document title text inside title.");
        var links = head.Elements().Where(n => n.Name == "link").ToArray();
        Require(links.Length == 1, "UI_RESOURCE", file, head, "Expected exactly one same-basename stylesheet link.");
        var link = links[0];
        Require(link.Attribute("type")?.Value == "text/rcss" && link.Attribute("href")?.Value == stylesheet,
            "UI_RESOURCE", file, link, "Only the exact sibling stylesheet '" + stylesheet + "' is allowed.");
        Require(!link.HasElements && string.IsNullOrWhiteSpace(link.Value), "UI_STRUCTURE", file, link,
            "Stylesheet link must be empty.");
        Require(head.Elements().Count(n => n.Name == "title") <= 1, "UI_STRUCTURE", file, head, "At most one title is allowed.");
        foreach (var child in head.Elements())
        {
            Require(child == link || child.Name == "title", "UI_RESOURCE", file, child,
                "Head may contain only a title and the exact sibling stylesheet link.");
            if (child.Name == "title") Require(!child.HasElements, "UI_STRUCTURE", file, child, "Title must be plain text.");
        }
        Require(body.Attribute("data-model")?.Value == "model", "UI_BINDING", file, body,
            "Body must declare data-model=\"model\"; the C# root is bound as state.");

        var images = new List<UiImageReference>();
        if (declaredImages is { Count: > UiImageResources.MaximumImages })
            throw new UiAuthoringException("UI_RESOURCE", file, 1, 1, "images", $"At most {UiImageResources.MaximumImages} declared images are supported.");
        if (declaredImages is not null)
            for (int i = 0; i < declaredImages.Count; i++)
            {
                string? path = declaredImages[i];
                if (path is null) throw new UiAuthoringException("UI_RESOURCE", file, 1, 1, $"images[{i}]", "Image path cannot be null.");
                AddImage(images, new(path, file, 1, 1, $"images[{i}]"));
            }
        bool hasDeclaredImages = declaredImages is { Count: > 0 };
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (UiXmlElement node in root.DescendantsAndSelf())
        {
            string tag = node.Name.LocalName.ToLowerInvariant();
            bool content = node == body || node.Ancestors().Contains(body);
            Require(node.Name.NamespaceName.Length == 0, "UI_ATTRIBUTE", file, node, "Namespaces are unsupported.");
            Require(tag is not ("script" or "style" or "template") && (tag != "link" || node == link),
                "UI_RESOURCE", file, node, "Scripts, inline stylesheets, templates and additional links are unsupported.");
            Require(node == root || tag != "rml", "UI_STRUCTURE", file, node, "Nested rml roots are unsupported.");
            Require(node == head || tag != "head", "UI_STRUCTURE", file, node, "Nested head elements are unsupported.");
            Require(node == body || tag != "body", "UI_STRUCTURE", file, node, "Nested body elements are unsupported.");
            foreach (UiXmlAttribute attribute in node.Attributes())
            {
                Require(!attribute.IsNamespaceDeclaration && attribute.Name.NamespaceName.Length == 0,
                    "UI_ATTRIBUTE", file, attribute, "Namespaces are unsupported.");
                string name = attribute.Name.LocalName.ToLowerInvariant();
                Require(!name.StartsWith("rmlui-", StringComparison.Ordinal), "UI_ATTRIBUTE", file, attribute, "Reserved RmlUi implementation attributes cannot be authored.");
                Require(!name.StartsWith("on", StringComparison.Ordinal), "UI_BINDING", file, attribute,
                    "Inline handlers are unsupported; use data-event-<event> with a registered command.");
                if (name == "data-model")
                {
                    Require(node == body && attribute.Name.LocalName == "data-model" && attribute.Value == "model",
                        "UI_BINDING", file, attribute, "Only body may declare the model named model.");
                }
                else if (name.StartsWith("data-", StringComparison.Ordinal))
                {
                    Require(content, "UI_BINDING", file, attribute, "Data bindings belong inside body.");
                    ValidateBinding(attribute, commands, hasDeclaredImages, file);
                }
                else if (name == "src")
                {
                    Require(content, "UI_RESOURCE", file, attribute, "Only content image sources are allowed.");
                    AddImage(images, new(attribute.Value, file, attribute.LineNumber, attribute.LinePosition, Field(attribute)));
                }
                else if (name == "style")
                {
                    new StylePolicy(attribute.Value, file, images, attribute).Validate();
                }
                else if (IsResourceAttribute(name))
                {
                    Require(node == link && name == "href", "UI_RESOURCE", file, attribute,
                        "External resources, navigation, templates and resource aliases are unsupported.");
                }
                if (name == "id")
                    Require(attribute.Value.Length != 0 && ids.Add(attribute.Value), "UI_ID", file, attribute,
                        "Authored IDs must be nonempty and unique.");
            }
            // Inline SVG bypasses the validated SVG asset reader, so SVG uses the same finite
            // resource manifest as raster images. Other normal/custom tags have no finite allowlist.
            if (tag == "svg")
                Require(!node.HasElements && string.IsNullOrWhiteSpace(node.Value) &&
                    (node.Attribute("src") is not null || node.Attribute("data-attr-src") is not null),
                    "UI_RESOURCE", file, node, "SVG must be an empty element with a static or manifested dynamic src; inline SVG is unsupported.");
        }
        new StylePolicy(style, cssFile, images).Validate();
        return new(markup, style, stylesheet, images.AsReadOnly(), Array.Empty<UiImageResource>());
    }

    private static HashSet<string> ValidateCommands(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Count > 32) throw new UiAuthoringException("UI_SCHEMA", "<commands>", 1, 1, "commands", "At most 32 commands are supported.");
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < names.Count; i++)
        {
            string? name = names[i];
            if (name is not { Length: >= 1 and <= 47 } || name[0] is not (>= 'a' and <= 'z') ||
                !name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
                throw new UiAuthoringException("UI_SCHEMA", "<commands>", 1, 1, $"commands[{i}]",
                    "Command names must match [a-z][a-z0-9_]{0,46}.");
            if (!result.Add(name)) throw new UiAuthoringException("UI_SCHEMA", "<commands>", 1, 1, $"commands[{i}]", "Duplicate command name.");
        }
        return result;
    }

    private static void ValidateBinding(UiXmlAttribute attribute, HashSet<string> commands, bool hasDeclaredImages, string file)
    {
        string name = attribute.Name.LocalName.ToLowerInvariant();
        string view = name[5..].Split('-', 2)[0];
        Require(view is not ("rml" or "value" or "checked"), "UI_BINDING", file, attribute,
            "Markup injection and two-way controllers are unsupported. Use plain text, data-attr-value or data-attrif-checked and a change command.");
        if (view == "event")
        {
            Require(name.StartsWith("data-event-", StringComparison.Ordinal) && name.Length > 11,
                "UI_BINDING", file, attribute, "Specify an event after data-event-.");
            ValidateCommand(attribute, commands, file);
            return;
        }
        if (view is "attr" or "attrif")
        {
            string prefix = "data-" + view + "-";
            Require(name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length,
                "UI_BINDING", file, attribute, "Specify the attribute being bound.");
            string target = name[prefix.Length..];
            Require(!target.StartsWith("on", StringComparison.Ordinal) && !target.StartsWith("data-", StringComparison.Ordinal) && !target.StartsWith("rmlui-", StringComparison.Ordinal)
                && target is not ("style" or "xmlns") && !target.Contains(':') && !IsResourceAttribute(target),
                "UI_BINDING", file, attribute, "A binding cannot create code, another binding, namespaces, inline styles or external-resource attributes.");
            if (target == "src")
                Require(view == "attr" && hasDeclaredImages, "UI_RESOURCE", file, attribute,
                    "Dynamic src requires a nonempty declared image manifest; only those validated images may load.");
        }
        if (view == "style")
        {
            string property = name.StartsWith("data-style-", StringComparison.Ordinal) ? name[11..] : "";
            Require(property.Length != 0 && !IsResourceProperty(property) && property is not ("decorator" or "mask-image")
                && !property.StartsWith("--", StringComparison.Ordinal), "UI_RESOURCE", file, attribute,
                "Dynamic styles cannot supply resource declarations or CSS variables; author resource styles statically.");
        }
        ValidateReadExpression(attribute.Value, file, attribute);
    }

    private static void ValidateCommand(UiXmlAttribute attribute, HashSet<string> commands, string file)
    {
        string expression = attribute.Value.Trim();
        int end = 0;
        while (end < expression.Length && (char.IsAsciiLetterOrDigit(expression[end]) || expression[end] == '_')) end++;
        string command = expression[..end];
        Require(commands.Contains(command), "UI_COMMAND", file, attribute, "Unknown registered command '" + command + "'.");
        string arguments = expression[end..].Trim();
        if (arguments.Length == 0) return; // RmlUi also permits a bare zero-argument command.
        Require(arguments[0] == '(' && arguments[^1] == ')', "UI_COMMAND", file, attribute,
            "An event must be exactly one registered command invocation.");
        // The balanced-expression scan also ensures this final ')' is the invocation's only
        // outer closing parenthesis. Assignment, sequencing and extra invocations are rejected.
        ValidateReadExpression(arguments, file, attribute, commandArguments: true);
    }

    private static void ValidateReadExpression(string expression, string file, UiXmlAttribute attribute, bool commandArguments = false)
    {
        Require(!string.IsNullOrWhiteSpace(expression), "UI_BINDING", file, attribute, "Binding expression cannot be empty.");
        var delimiters = new Stack<char>();
        char quote = '\0';
        for (int i = 0; i < expression.Length; i++)
        {
            char c = expression[i];
            if (quote != '\0')
            {
                if (c == '\\' && i + 1 < expression.Length) { i++; continue; }
                if (c == quote) quote = '\0';
                continue;
            }
            if (c is '\'' or '"') { quote = c; continue; }
            Require(c != ';', "UI_COMMAND", file, attribute, "Multiple statements and semicolons are unsupported.");
            if (c == '=')
            {
                bool comparison = i + 1 < expression.Length && expression[i + 1] == '=' ||
                    i > 0 && expression[i - 1] is '=' or '!' or '<' or '>';
                Require(comparison, "UI_COMMAND", file, attribute, "Assignments are unsupported; C# owns model changes.");
            }
            if (c is '(' or '[') delimiters.Push(c);
            else if (c is ')' or ']')
            {
                Require(delimiters.Count != 0 && delimiters.Pop() == (c == ')' ? '(' : '['),
                    "UI_BINDING", file, attribute, "Unbalanced expression delimiters.");
                if (commandArguments && delimiters.Count == 0)
                    Require(i == expression.Length - 1, "UI_COMMAND", file, attribute,
                        "An event must contain exactly one command invocation.");
            }
        }
        Require(quote == '\0' && delimiters.Count == 0, "UI_BINDING", file, attribute, "Unterminated string or expression delimiter.");
    }

    private static bool IsResourceAttribute(string name) => name is "href" or "srcset" or "poster" or "action" or "formaction"
        or "background" or "template" or "srcdoc" or "code" or "codebase" or "archive" or "profile" or "manifest"
        or "rmlui-svgdata-id";
    private static bool IsResourceProperty(string name) => name is "src" or "source" or "fill-image" or "background-image"
        || name.EndsWith("-src", StringComparison.Ordinal);
    private static void AddImage(List<UiImageReference> images, UiImageReference reference) =>
        UiImageResources.Add(images, reference, reference.Path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A capability scan, not a replacement RCSS grammar. Leave selectors, properties, values,
    /// media queries, keyframes and native expression syntax to RmlUi. Resource-loading syntax
    /// is narrower: explicit image()/svg() paths or fill-image, no imports/fonts/indirect resource
    /// decorators. Sources are snapshotted once and still face native manifest enforcement.
    /// </summary>
    private sealed class StylePolicy(string source, string file, List<UiImageReference> images, UiXmlAttribute? attribute = null)
    {
        private string text = "";
        private readonly List<int> sourceOffsets = [];

        internal void Validate()
        {
            var normalized = new StringBuilder(source.Length);
            // Upstream removes comments and LF characters while tokenizing, even between
            // identifier characters. Mirror that for the guard, retaining original positions.
            for (int i = 0; i < source.Length; i++)
            {
                if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
                {
                    int close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (close < 0) throw At("UI_STYLE", i, "Unterminated RCSS comment.", originalOffset: true);
                    i = close + 1;
                }
                else if (source[i] != '\n') { sourceOffsets.Add(i); normalized.Append(source[i]); }
            }
            text = normalized.ToString();
            for (int i = 0; i < text.Length;)
            {
                char c = text[i];
                if (c == '\\') throw At("UI_RESOURCE", i, "RCSS escapes are unsupported at the resource boundary.");
                if (c == '@')
                {
                    int end = IdentifierEnd(i + 1);
                    string rule = text[(i + 1)..end].ToLowerInvariant();
                    if (rule is "import" or "font-face" or "spritesheet" or "decorator")
                        throw At("UI_RESOURCE", i, "Imports, font loading, spritesheets and named decorator resources are unsupported; use explicit image()/svg() resources.");
                }
                if (!IdentifierChar(c)) { i++; continue; }
                int tokenStart = i;
                int tokenEnd = IdentifierEnd(i);
                string token = text[i..tokenEnd].ToLowerInvariant();
                int next = SkipWhitespace(tokenEnd);
                if (next < text.Length && text[next] == '(')
                {
                    if (token is "url" or "tiled-horizontal" or "tiled-vertical" or "tiled-box" or "ninepatch" or "shader")
                        throw At("UI_RESOURCE", tokenStart, "Only explicit image()/svg() functions may introduce external resources; shader code is unsupported.");
                    if (token is "image" or "svg") ReadImageFunction(token, next, tokenStart);
                }
                if (next < text.Length && text[next] == ':' && IsResourceProperty(token) && IsDeclaration(tokenStart, next))
                {
                    if (token != "fill-image") throw At("UI_RESOURCE", tokenStart, "External source descriptors are unsupported; use explicit image()/svg() resources.");
                    int valueStart = SkipWhitespace(next + 1);
                    var (path, end) = ReadValue(valueStart);
                    if (path != "none") AddReference(path, tokenStart);
                    int after = SkipWhitespace(end);
                    if (after < text.Length && text[after] is not (';' or '}'))
                        throw At("UI_RESOURCE", after, "fill-image requires one literal manifest image path.");
                }
                i = tokenEnd;
            }
        }

        private bool IsDeclaration(int start, int colon)
        {
            int previous = start - 1;
            while (previous >= 0 && char.IsWhiteSpace(text[previous])) previous--;
            if (previous < 0 ? attribute is null : text[previous] is not ('{' or ';')) return false;
            // A resource-like tag or class in a selector is not a source descriptor.
            // In particular, allow ordinary source:hover and fill-image:focus selectors.
            char quote = '\0';
            for (int i = colon + 1; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c is '\'' or '"') { quote = c; continue; }
                if (c == '{') return false;
                if (c is ';' or '}') return true;
            }
            return attribute is not null;
        }

        private void ReadImageFunction(string kind, int open, int location)
        {
            int index = SkipWhitespace(open + 1);
            var (path, end) = ReadValue(index);
            if (!UiImageResources.ValidPath(path, svg: kind == "svg"))
                throw At("UI_RESOURCE", index, "Expected one literal relative " + (kind == "svg" ? "SVG" : "BMP/PNG/JPEG") + " image path.");
            AddReference(path, location);
            index = SkipWhitespace(end);
            while (index < text.Length && text[index] != ')')
            {
                var (modifier, modifierEnd) = ReadValue(index);
                // These are resource-instancer argument forms, not a CSS property allowlist.
                // In particular another path, var(), URL or source cannot replace the checked path.
                bool valid = kind == "svg" ? modifier is "crop-none" or "crop-to-content" :
                    modifier is "none" or "flip-horizontal" or "flip-vertical" or "rotate-180" or "fill" or "contain" or "cover"
                        or "scale-none" or "scale-down" or "repeat" or "repeat-x" or "repeat-y" or "left" or "center" or "right"
                        or "top" or "bottom" || IsLength(modifier);
                if (!valid || modifierEnd == index) throw At("UI_RESOURCE", index, "Unsupported image resource argument; use one literal path and native fit/alignment options.");
                index = SkipWhitespace(modifierEnd);
            }
            if (index == text.Length) throw At("UI_STYLE", open, "Unterminated image decorator.");
        }

        private static bool IsLength(string value)
        {
            string number = value;
            if (value.EndsWith("px", StringComparison.Ordinal) || value.EndsWith("dp", StringComparison.Ordinal)) number = value[..^2];
            else if (value.EndsWith('%')) number = value[..^1];
            return number.Length != 0 && double.TryParse(number, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double n) && double.IsFinite(n);
        }
        private (string Value, int End) ReadValue(int start)
        {
            if (start >= text.Length) return ("", start);
            char quote = text[start];
            if (quote is '\'' or '"')
            {
                int end = text.IndexOf(quote, start + 1);
                if (end < 0) throw At("UI_STYLE", start, "Unterminated resource string.");
                return (text[(start + 1)..end], end + 1);
            }
            int position = start;
            while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] is not (')' or ';' or '}' or ',')) position++;
            return (text[start..position], position);
        }
        private void AddReference(string path, int index)
        {
            var error = At("UI_RESOURCE", index, "");
            AddImage(images, new(path, file, error.Line, error.Column, error.Field));
        }
        private static bool IdentifierChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';
        private int IdentifierEnd(int index) { while (index < text.Length && IdentifierChar(text[index])) index++; return index; }
        private int SkipWhitespace(int index) { while (index < text.Length && char.IsWhiteSpace(text[index])) index++; return index; }
        private UiAuthoringException At(string code, int index, string cause, bool originalOffset = false)
        {
            int offset = originalOffset ? index : index < sourceOffsets.Count ? sourceOffsets[index] : source.Length;
            int line = attribute?.LineNumber ?? 1, column = attribute?.LinePosition ?? 1;
            for (int i = 0; i < offset; i++) { if (source[i] == '\n') { line++; column = 1; } else column++; }
            return new(code, file, line, column, attribute is null ? "$style" : Field(attribute), cause);
        }
    }

    private static string StylesheetFor(string file)
    {
        if (!file.EndsWith(".rml", StringComparison.Ordinal))
            throw new UiAuthoringException("UI_RESOURCE", file, 1, 1, "$", "Model documents require an .rml filename.");
        return Path.GetFileName(file)[..^4] + ".rcss";
    }
    private static void Require(bool condition, string code, string file, UiXmlObject? node, string cause)
    { if (!condition) throw Error(code, file, node, cause); }
    private static string Field(UiXmlObject? node)
    {
        UiXmlElement? element = node as UiXmlElement ?? node?.Parent;
        string field = element is null ? "$" : string.Join("/", element.AncestorsAndSelf().Reverse().Select(e =>
            e.Name.LocalName + (e.Attribute("id") is { } id ? "#" + id.Value[..Math.Min(id.Value.Length, 48)] : "")));
        return node is UiXmlAttribute attribute ? field + "@" + attribute.Name.LocalName : field;
    }
    private static UiAuthoringException Error(string code, string file, UiXmlObject? node, string cause) =>
        new(code, file, node?.LineNumber ?? 1, node?.LinePosition ?? 1, Field(node), cause);
}
