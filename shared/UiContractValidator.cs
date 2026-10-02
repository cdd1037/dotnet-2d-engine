using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GameAuthoringLab;

/// <summary>
/// Schema-only proof of common authoring mistakes, including templates whose arrays are empty.
/// This is a lexical/path scan, not a second RmlUi expression parser. It never evaluates model
/// values, transforms or event parameters. Unrecognized syntax and dynamic result types still
/// require native strict staging and runtime command checks.
/// </summary>
internal static class UiContractValidator
{
    internal static void Validate(string source, string file, UiContractNode[] schema, UiContractCommand[] commands,
        bool diagnoseUnknownCommands = true)
    {
        UiXmlElement? root = UiXmlParser.Parse(source, file).Root;
        if (root is null) throw new UiContractException("UI_STRUCTURE", file, 1, 1, "$", "Missing rml root.");
        Validate(root, file, schema, commands, diagnoseUnknownCommands);
    }

    internal static void Validate(UiXmlElement root, string file, UiContractNode[] schema, UiContractCommand[] commands,
        bool diagnoseUnknownCommands = true)
    {
        var shape = new Shape(schema);
        var registered = commands.ToDictionary(c => c.Name, StringComparer.Ordinal);
        Visit(root.Element("body") ?? root, new Dictionary<string, Value>(StringComparer.Ordinal));

        void Visit(UiXmlElement element, Dictionary<string, Value> aliases)
        {
            if (element.Attribute("data-for") is { } loop)
            {
                // RmlUi's structural view introduces aliases on the cloned element itself,
                // including its other attributes. The container uses the enclosing scope.
                if (!TryLoop(loop.Value, out string item, out string index, out string container))
                    return; // Unknown loop syntax: don't invent a scope or reject its descendants.
                List<Token>? tokens = Lex(container);
                if (tokens is null || tokens.Count == 0 || !IsWholePath(tokens, 0, tokens.Count)) return;
                Value collection = Resolve(tokens, 0, aliases, loop, out _);
                if (collection.Kind is not (0 or 6))
                    throw At("UI_BINDING", loop, $"Loop container '{container}' is not an array in the registered schema.", shape.Declaration(collection));
                Value entry = collection.Kind == 6 ? shape.Element(collection) : default;
                aliases = new Dictionary<string, Value>(aliases, StringComparer.Ordinal) { [item] = entry, [index] = Value.Number };
            }

            // Native data-alias-* names are local to this element and its descendants. Seed
            // every declared name as unknown before resolving simple paths, so native alias
            // initialization order and alias chains cannot create false unknown-root errors.
            UiXmlAttribute[] declarations = element.Attributes().Where(a =>
                a.Name.LocalName.StartsWith("data-alias-", StringComparison.Ordinal) && IsName(a.Name.LocalName.Substring(11))).ToArray();
            if (declarations.Length != 0)
            {
                var enclosing = aliases;
                aliases = new Dictionary<string, Value>(aliases, StringComparer.Ordinal);
                foreach (UiXmlAttribute declaration in declarations) aliases[declaration.Name.LocalName.Substring(11)] = default;
                var seeded = new Dictionary<string, Value>(aliases, StringComparer.Ordinal);
                foreach (UiXmlAttribute declaration in declarations)
                {
                    List<Token>? tokens = Lex(declaration.Value);
                    if (tokens is not { Count: > 0 } || !IsWholePath(tokens, 0, tokens.Count)) continue;
                    // A same-name alias declaration can read the enclosing alias before replacing
                    // it. Other local aliases remain conservative when native ordering is unknown.
                    string name = declaration.Name.LocalName.Substring(11);
                    var scope = new Dictionary<string, Value>(seeded, StringComparer.Ordinal);
                    if (enclosing.TryGetValue(name, out Value outer)) scope[name] = outer;
                    aliases[name] = Resolve(tokens, 0, scope, declaration, out _);
                }
            }

            foreach (UiXmlAttribute attribute in element.Attributes())
            {
                string name = attribute.Name.LocalName;
                if (name.StartsWith("data-event-", StringComparison.Ordinal)) CheckCommand(attribute, aliases);
                else if (IsReadBinding(name)) CheckRead(attribute.Value, aliases, attribute);
                // Unknown data-* names are ordinary metadata. DataViewText ignores the value
                // of data-text; interpolation is checked in text nodes instead. for/alias use
                // address scopes above, not the expression grammar of DataViewCommon.
            }
            foreach (UiXmlNode node in element.Nodes())
            {
                if (node is UiXmlElement child) Visit(child, aliases);
                // Native textarea initialization uses SetValue, not interpolated text views.
                else if (node is UiXmlText text && element.Name != "textarea") CheckInterpolations(text, aliases);
            }
        }

        void CheckInterpolations(UiXmlText text, Dictionary<string, Value> aliases)
        {
            if (!text.HasRawInterpolation) return;
            string value = text.Value;
            int from = 0;
            while (from < value.Length)
            {
                int opening = value.IndexOf("{{", from, StringComparison.Ordinal);
                if (opening < 0) break;
                int closing = opening + 2;
                for (; closing + 1 < value.Length; closing++)
                {
                    if (value[closing] is '\'' or '"')
                    {
                        int after = EndQuote(value, closing);
                        if (after < 0) return;
                        closing = after - 1;
                    }
                    else if (value[closing] == '}' && value[closing + 1] == '}') break;
                }
                if (closing + 1 >= value.Length) return; // Native owns malformed delimiters.
                CheckRead(value.Substring(opening + 2, closing - opening - 2), aliases, text);
                from = closing + 2;
            }
        }

        void CheckRead(string expression, Dictionary<string, Value> aliases, UiXmlObject source)
        {
            List<Token>? tokens = Lex(expression);
            if (tokens is null) return;
            Scan(tokens, aliases, source);
        }

        void Scan(List<Token> tokens, Dictionary<string, Value> aliases, UiXmlObject source)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Kind != TokenKind.Name || tokens[i].Text is "true" or "false") continue;
                if (i > 0 && tokens[i - 1].Text == ".") continue;
                if (i + 1 < tokens.Count && tokens[i + 1].Text == "(") continue; // Transform name.
                if (i > 0 && tokens[i - 1].Text == "|" && (i < 2 || tokens[i - 2].Text != "|")) continue;
                Resolve(tokens, i, aliases, source, out _);
                // Don't skip bracket contents: a dynamic index may itself contain a provably
                // misspelled schema path. Its selected element/result still stays unknown.
            }
        }

        Value Resolve(List<Token> tokens, int start, Dictionary<string, Value> aliases, UiXmlObject source, out int end)
        {
            end = start + 1;
            string name = tokens[start].Text;
            Value current;
            // Native globals precede aliases. Event fields and the native literal namespace
            // are runtime/native facilities, not user schema roots.
            if (name is "ev" or "literal") current = default;
            else if (!shape.Roots.TryGetValue(name, out current) && !aliases.TryGetValue(name, out current))
                throw At("UI_BINDING", source, $"Unknown model variable or loop alias '{name}'.");
            string path = name;
            while (end < tokens.Count)
            {
                if (tokens[end].Text == "." && end + 1 < tokens.Count && tokens[end + 1].Kind == TokenKind.Name)
                {
                    string member = tokens[end + 1].Text;
                    path += "." + member;
                    if (current.Kind != 0)
                    {
                        Value child = shape.Member(current, member);
                        if (child.Kind == 0) throw At("UI_BINDING", source, $"Model path '{path}' is not present in the registered schema.", shape.Declaration(current));
                        current = child;
                    }
                    end += 2;
                }
                else if (tokens[end].Text == "[" && tokens[end].Match > end)
                {
                    int close = tokens[end].Match;
                    bool fixedIndex = close == end + 2 && tokens[end + 1].Kind == TokenKind.Number &&
                        tokens[end + 1].Text.All(IsAsciiDigit);
                    if (!fixedIndex) current = default; // Dynamic selection remains native-owned.
                    else if (current.Kind != 0)
                    {
                        if (current.Kind != 6) throw At("UI_BINDING", source, $"Model path '{path}' is not an array in the registered schema.", shape.Declaration(current));
                        current = shape.Element(current);
                    }
                    path += "[" + (fixedIndex ? tokens[end + 1].Text : "...") + "]";
                    end = close + 1;
                }
                else break;
            }
            return current;
        }

        void CheckCommand(UiXmlAttribute attribute, Dictionary<string, Value> aliases)
        {
            List<Token>? tokens = Lex(attribute.Value);
            if (tokens is not { Count: > 0 } || tokens[0].Kind != TokenKind.Name) return;
            var arguments = new List<(int Start, int End)>();
            if (tokens.Count != 1)
            {
                if (tokens.Count < 3 || tokens[1].Text != "(" || tokens[1].Match != tokens.Count - 1) return;
                int start = 2, last = tokens.Count - 1;
                for (int i = start; i < last; i++)
                {
                    if (tokens[i].Match > i) { i = tokens[i].Match; continue; }
                    if (tokens[i].Text == ",") { if (i == start) return; arguments.Add((start, i)); start = i + 1; }
                }
                if (start < last) arguments.Add((start, last));
                else if (start != 2) return; // Malformed trailing comma remains native-owned.
            }
            // Only diagnose a command name once the lexical scan proves one complete
            // invocation (or a bare name). Malformed/compound syntax stays native-owned.
            if (!registered.TryGetValue(tokens[0].Text, out UiContractCommand? command))
            {
                if (diagnoseUnknownCommands)
                    throw At("UI_COMMAND", attribute, "Unknown registered command '" + tokens[0].Text + "'.");
                return; // Runtime's authoring capability gate already reports command names.
            }
            if (arguments.Count != command.Arguments.Length)
                throw CommandError(attribute, command, $"Command '{command.Name}' expects {command.Arguments.Length} argument(s), but the invocation supplies {arguments.Count}.");
            if (arguments.Count != 0) Scan(tokens, aliases, attribute);
            for (int i = 0; i < arguments.Count; i++)
            {
                (int start, int end) = arguments[i];
                Value actual = Infer(tokens, start, end, aliases, attribute);
                uint expected = command.Arguments[i];
                if (actual.Kind == 0 || actual.Kind == expected || actual.Kind == 4 && expected == 1) continue;
                // Legacy Add permits canonical key strings from a Text field; actual membership
                // is value-dependent and remains native-checked. Typed On registrations require
                // a direct schema argument to carry Key provenance instead of an incidental Text.
                if (!command.TypedProvenance && expected == 4 && actual.Kind == 1 && actual.Node >= 0) continue;
                // A canonical key literal can only be checked against the current snapshot at dispatch.
                while (start < end && tokens[start].Text == "(" && tokens[start].Match == end - 1) { start++; end--; }
                if (expected == 4 && end == start + 1 && tokens[start].Kind == TokenKind.String && IsKeyLiteral(tokens[start].Text)) continue;
                string kind = KindName(actual.Kind);
                throw CommandError(attribute, command, $"Command '{command.Name}' argument {i + 1} expects {KindName(expected)}, but its expression has schema/literal kind {kind}.");
            }
        }

        Value Infer(List<Token> tokens, int start, int end, Dictionary<string, Value> aliases, UiXmlObject source)
        {
            while (start < end && tokens[start].Text == "(" && tokens[start].Match == end - 1) { start++; end--; }
            if (start >= end) return default;
            if (end == start + 1)
            {
                if (tokens[start].Kind == TokenKind.String) return new(1, -1);
                if (tokens[start].Kind == TokenKind.Number) return Value.Number;
                if (tokens[start].Text is "true" or "false") return new(2, -1);
            }
            if (end == start + 2 && tokens[start].Text == "-" && tokens[start + 1].Kind == TokenKind.Number) return Value.Number;
            if (IsWholePath(tokens, start, end)) return Resolve(tokens, start, aliases, source, out _);
            // Do not guess the result of transforms, operators, ternaries or event parameters.
            return default;
        }

        UiContractException CommandError(UiXmlAttribute source, UiContractCommand command, string cause)
        {
            if (command.Declaration is { } origin) cause += $" Registered at {origin.FilePath}:{origin.Line}.";
            return At("UI_COMMAND", source, cause, command.Declaration);
        }

        UiContractException At(string code, UiXmlObject source, string cause, UiContractDeclaration? declaration = null)
        {
            UiXmlElement? element = source as UiXmlElement ?? source.Parent;
            string field = element is null ? "$" : string.Join("/", element.AncestorsAndSelf().Reverse().Select(e =>
                e.Name.LocalName + (e.Attribute("id") is { } id ? "#" + id.Value.Substring(0, Math.Min(id.Value.Length, 48)) : "")));
            field += source is UiXmlAttribute attribute ? "@" + attribute.Name.LocalName : "/text()";
            // XmlReader's source location is retained. Decoded entity offsets are not original
            // source offsets, and a schema has no authored-data origin to invent here.
            return new UiContractException(code, file, source.LineNumber, source.LinePosition, field, cause, declaration);
        }
    }

    private readonly struct Value
    {
        internal Value(uint kind, int node) { Kind = kind; Node = node; }
        internal uint Kind { get; }
        internal int Node { get; }
        internal static Value Number => new Value(3, -1);
    }

    private sealed class Shape
    {
        private readonly UiContractNode[] nodes;
        private readonly string[] names;
        internal readonly Dictionary<string, Value> Roots = new(StringComparer.Ordinal);
        internal Shape(UiContractNode[] schema)
        {
            nodes = schema;
            names = new string[schema.Length];
            for (int i = 0; i < schema.Length; i++)
            {
                names[i] = schema[i].Name;
                if (schema[i].Parent == -1) Roots.Add(names[i], new(schema[i].Kind, i));
            }
        }
        internal Value Member(Value parent, string name)
        {
            if (parent.Kind == 6 && name == "size") return Value.Number;
            if (parent.Kind != 5 || parent.Node < 0) return default;
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i].Parent == parent.Node && names[i] == name) return new(nodes[i].Kind, i);
            return default;
        }
        internal Value Element(Value parent)
        {
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i].Parent == parent.Node) return new(nodes[i].Kind, i);
            return default;
        }
        internal UiContractDeclaration? Declaration(Value value) =>
            value.Kind != 0 && value.Node >= 0 ? nodes[value.Node].Declaration : null;
    }

    private static bool TryLoop(string expression, out string item, out string index, out string container)
    {
        item = "it"; index = "it_index"; container = expression.Trim();
        int colon = expression.IndexOf(':');
        if (colon < 0) return container.Length != 0;
        if (expression.IndexOf(':', colon + 1) >= 0) return false;
        container = expression.Substring(colon + 1).Trim();
        string[] aliases = expression.Substring(0, colon).Split(',');
        if (aliases.Length is < 1 or > 2 || container.Length == 0) return false;
        item = aliases[0].Trim();
        if (aliases.Length == 2) index = aliases[1].Trim();
        // Pinned DataViewFor falls back to these names for empty aliases.
        if (item.Length == 0) item = "it";
        if (index.Length == 0) index = "it_index";
        return IsName(item) && IsName(index);
    }

    private static bool IsReadBinding(string name)
    {
        if (!name.StartsWith("data-", StringComparison.Ordinal)) return false;
        int modifier = name.IndexOf('-', 5);
        string view = modifier < 0 ? name.Substring(5) : name.Substring(5, modifier - 5);
        // Pinned Factory.cpp registrations using DataViewCommon's expression parser. The
        // authoring capability gate independently excludes rml/value/checked where required.
        return view is "attr" or "attrif" or "class" or "if" or "visible" or "rml" or "style" or "value" or "checked";
    }

    private static bool IsWholePath(List<Token> tokens, int start, int end)
    {
        if (start >= end || tokens[start].Kind != TokenKind.Name || tokens[start].Text is "true" or "false") return false;
        int i = start + 1;
        while (i < end)
        {
            if (tokens[i].Text == "." && i + 1 < end && tokens[i + 1].Kind == TokenKind.Name) i += 2;
            else if (tokens[i].Text == "[" && tokens[i].Match > i && tokens[i].Match < end) i = tokens[i].Match + 1;
            else return false;
        }
        return true;
    }

    private static bool IsKeyLiteral(string text)
    {
        string digits = text.Substring(1, text.Length - 2);
        return digits.Length > 0 && digits[0] != '0' && ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private enum TokenKind { Name, Number, String, Symbol }
    private sealed class Token
    {
        internal Token(TokenKind kind, string text) { Kind = kind; Text = text; }
        internal readonly TokenKind Kind;
        internal readonly string Text;
        internal int Match = -1;
    }

    private static List<Token>? Lex(string expression)
    {
        var tokens = new List<Token>();
        var open = new Stack<int>();
        for (int i = 0; i < expression.Length;)
        {
            char c = expression[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            int start = i++;
            TokenKind kind = TokenKind.Symbol;
            if (IsAsciiLetter(c))
            {
                kind = TokenKind.Name;
                while (i < expression.Length && (IsAsciiLetterOrDigit(expression[i]) || expression[i] == '_')) i++;
            }
            else if (IsAsciiDigit(c))
            {
                kind = TokenKind.Number;
                bool dot = false;
                while (i < expression.Length && (IsAsciiDigit(expression[i]) || expression[i] == '.' && !dot))
                { if (expression[i] == '.') dot = true; i++; }
            }
            else if (c is '\'' or '"')
            {
                kind = TokenKind.String;
                i = EndQuote(expression, start);
                if (i < 0) return null;
            }
            tokens.Add(new(kind, expression.Substring(start, i - start)));
            if (kind != TokenKind.Symbol) continue;
            if (c is '(' or '[') open.Push(tokens.Count - 1);
            else if (c is ')' or ']')
            {
                if (open.Count == 0) return null;
                int match = open.Pop();
                if (tokens[match].Text != (c == ')' ? "(" : "[")) return null;
                tokens[match].Match = tokens.Count - 1;
            }
        }
        return open.Count == 0 ? tokens : null;
    }

    private static int EndQuote(string expression, int start)
    {
        char quote = expression[start];
        for (int i = start + 1; i < expression.Length; i++)
        {
            if (expression[i] == '\\' && i + 1 < expression.Length) i++;
            else if (expression[i] == quote) return i + 1;
        }
        return -1;
    }
    private static bool IsName(string name) => name.Length > 0 && IsAsciiLetter(name[0]) &&
        name.All(c => IsAsciiLetterOrDigit(c) || c == '_');
    private static bool IsAsciiLetter(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';
    private static bool IsAsciiLetterOrDigit(char value) => IsAsciiLetter(value) || IsAsciiDigit(value);
    private static string KindName(uint kind) => kind switch
    {
        1 => "Text", 2 => "Boolean", 3 => "Number", 4 => "Key", 5 => "Record", 6 => "Array", _ => "Unknown"
    };

}
