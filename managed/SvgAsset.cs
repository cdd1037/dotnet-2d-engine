using System.Globalization;
using System.Text;
using System.Xml;

namespace GameAuthoringLab;

// This is a deliberately closed, static SVG authoring profile, not a general SVG sanitizer.
// The immutable validated bytes are staged for native revalidation and rasterization; world
// ImageAsset remains raster-only. CSS, text, filters, external resources and entities are absent.
internal sealed class SvgAsset
{
    internal const int MaximumEncodedBytes = 256 * 1024;
    internal const int MaximumDepth = 32;
    internal const int MaximumNodes = 2048;
    internal const int MaximumNumbers = 16384;
    internal const int MaximumPathBytes = 64 * 1024;
    internal const int MaximumExpandedNodes = 16384;
    internal const int MaximumExpandedNumbers = 65536;
    internal const int MaximumExpandedPathBytes = 256 * 1024;
    internal const int MaximumReferenceDepth = 16;
    internal const double MaximumNumber = 1_000_000;
    private readonly byte[] _bytes;
    internal ReadOnlySpan<byte> Bytes => _bytes;
    internal int EncodedLength => _bytes.Length;
    internal BitmapInfo Info { get; }
    internal long DecodedBytes => (long)Info.Width * Info.Height * 4;
    private SvgAsset(byte[] bytes, BitmapInfo info) { _bytes = bytes; Info = info; }

    internal static SvgAsset Read(AssetRoot assets, string logicalPath)
    {
        string path = assets.Resolve(logicalPath);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is < 1 or > MaximumEncodedBytes)
                throw Error(path, null, "SVG must contain 1..262144 encoded bytes.");
            byte[] bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw Error(path, null, "SVG changed size while reading.");
            return Validate(bytes, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new UiAuthoringException("UI_SVG", path, 1, 1, "$svg", e.Message, e); }
    }

    internal static SvgAsset Validate(ReadOnlySpan<byte> bytes, string file = "image.svg")
    {
        if (bytes.Length is < 1 or > MaximumEncodedBytes) throw Error(file, null, "SVG must contain 1..262144 encoded bytes.");
        string source;
        try { source = UiSourceFiles.StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException e) { throw new UiAuthoringException("UI_SVG", file, 1, 1, "$svg", "SVG requires valid UTF-8.", e); }
        if (source.Length > 0 && source[0] == '\uFEFF') source = source[1..];
        // Even predefined/numeric entities are excluded so native and managed parsers see
        // exactly the same names, paths and numeric tokens. No resolver can ever be called.
        if (source.Contains('&')) throw Error(file, null, "SVG entities, including numeric and predefined entities, are unsupported.");
        var root = Parse(source, file);
        var validation = new Validation(file);
        (double width, double height) = validation.Check(root);
        return new(bytes.ToArray(), new(file, (int)Math.Ceiling(width), (int)Math.Ceiling(height)));
    }

    private static UiXmlElement Parse(string source, string file)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaximumEncodedBytes, MaxCharactersFromEntities = 1, IgnoreComments = false };
        try
        {
            using var reader = XmlReader.Create(new StringReader(source), settings);
            var parents = new Stack<UiXmlElement>();
            UiXmlElement? root = null;
            int nodes = 0;
            while (reader.Read())
            {
                var line = (IXmlLineInfo)reader;
                if (reader.NodeType == XmlNodeType.Element)
                {
                    if (reader.Depth >= MaximumDepth || ++nodes > MaximumNodes)
                        throw Error(file, null, "SVG exceeds depth 32 or 2048 element nodes.");
                    if (reader.Prefix.Length != 0) throw Error(file, null, "Prefixed SVG elements are unsupported.");
                    bool empty = reader.IsEmptyElement;
                    var node = new UiXmlElement(new(reader.LocalName, reader.NamespaceURI), line.LineNumber, line.LinePosition);
                    if (parents.Count != 0) parents.Peek().Add(node); else root = node;
                    if (reader.MoveToFirstAttribute())
                    {
                        do
                        {
                            if (UiSourceFiles.StrictUtf8.GetByteCount(reader.Value) > MaximumPathBytes)
                                throw Error(file, node, "SVG attribute exceeds 65536 normalized UTF-8 bytes.");
                            node.AddAttribute(new(new(reader.LocalName, reader.NamespaceURI), reader.Value,
                                reader.NamespaceURI == "http://www.w3.org/2000/xmlns/", line.LineNumber, line.LinePosition));
                        } while (reader.MoveToNextAttribute());
                        reader.MoveToElement();
                    }
                    if (!empty) parents.Push(node);
                }
                else if (reader.NodeType == XmlNodeType.EndElement) parents.Pop();
                else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    if (parents.Count != 0) parents.Peek().Add(new UiXmlText(reader.Value, line.LineNumber, line.LinePosition));
                }
                else if (reader.NodeType == XmlNodeType.XmlDeclaration)
                {
                    if (reader.GetAttribute("version") != "1.0" || reader.GetAttribute("encoding") is { } encoding && !encoding.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
                        throw Error(file, null, "SVG requires XML 1.0 with UTF-8 encoding.");
                }
                else if (reader.NodeType != XmlNodeType.Comment)
                    throw Error(file, null, "SVG processing instructions, CDATA, DTDs and entities are unsupported.");
            }
            return root ?? throw Error(file, null, "Missing SVG root.");
        }
        catch (XmlException e) { throw new UiAuthoringException("UI_SVG", file, e.LineNumber, e.LinePosition, "$xml", e.Message, e); }
    }

    private sealed class Validation(string file)
    {
        private static readonly HashSet<string> Tags = new(StringComparer.Ordinal)
            { "svg", "g", "defs", "symbol", "use", "clipPath", "mask", "linearGradient", "radialGradient", "stop",
              "rect", "circle", "ellipse", "line", "polyline", "polygon", "path", "title", "desc" };
        private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
            { "id", "transform", "fill", "fill-rule", "fill-opacity", "stroke", "stroke-width", "stroke-opacity", "stroke-linecap",
              "stroke-linejoin", "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset", "opacity", "clip-path", "clip-rule", "mask", "color", "display", "visibility" };
        private readonly Dictionary<string, UiXmlElement> _ids = new(StringComparer.Ordinal);
        private readonly Dictionary<UiXmlElement, List<(string Id, string Kind)>> _references = [];
        private readonly Dictionary<UiXmlElement, (int Numbers, int Path)> _costs = [];
        private readonly Dictionary<UiXmlElement, (double Linear, double Translation)> _transforms = [];
        private int _numbers, _pathBytes;
        private UiXmlElement? _current;

        internal (double Width, double Height) Check(UiXmlElement root)
        {
            Require(root.Name.LocalName == "svg", root, "Expected an SVG root element.");
            foreach (var node in root.DescendantsAndSelf())
            {
                _current = node;
                int before = _numbers, pathBefore = _pathBytes;
                string tag = node.Name.LocalName;
                Require((node.Name.NamespaceName is "" or "http://www.w3.org/2000/svg") && Tags.Contains(tag), node, "Unsupported SVG element or namespace.");
                Require(tag is "title" or "desc" || node.Nodes().OfType<UiXmlText>().All(t => t.Value.All(Space)), node, "Only title and desc may contain text.");
                Require(tag is not ("title" or "desc") || !node.HasElements, node, "SVG title and desc must be plain text.");
                foreach (var attribute in node.Attributes()) CheckAttribute(node, attribute, root);
                if (tag == "use") Require(_references.ContainsKey(node) && _references[node].Any(r => r.Kind == "use"), node, "SVG use requires an internal href.");
                _costs.Add(node, (_numbers - before, _pathBytes - pathBefore));
            }
            foreach (var (node, references) in _references)
                foreach (var reference in references)
                {
                    Require(_ids.TryGetValue(reference.Id, out var target), node, "Unresolved SVG fragment reference #" + reference.Id + ".");
                    string tag = target!.Name.LocalName;
                    bool valid = reference.Kind switch
                    {
                        "use" => tag is "g" or "symbol" or "use" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon" or "path",
                        "gradient" or "paint" => tag is "linearGradient" or "radialGradient",
                        "clip-path" => tag == "clipPath",
                        "mask" => tag == "mask",
                        _ => false
                    };
                    Require(valid, node, "SVG fragment target has an unsupported element type.");
                }
            var active = new HashSet<UiXmlElement>();
            int expandedNodes = 0, expandedNumbers = 0, expandedPaths = 0;
            void Visit(UiXmlElement node, int references, double parentLinear, double parentTranslation)
            {
                Require(references <= MaximumReferenceDepth && active.Add(node), node, "SVG reference cycle or reference depth over 16.");
                var local = _transforms.GetValueOrDefault(node, (Linear: 1.0, Translation: 0.0));
                double linear = parentLinear * local.Linear, translation = parentLinear * local.Translation + parentTranslation;
                Require(double.IsFinite(linear) && double.IsFinite(translation) && linear <= MaximumNumber && translation <= MaximumNumber,
                    node, "SVG cumulative transform bounds exceed 1000000.");
                expandedNodes++;
                expandedNumbers += _costs[node].Numbers;
                expandedPaths += _costs[node].Path;
                Require(expandedNodes <= MaximumExpandedNodes && expandedNumbers <= MaximumExpandedNumbers && expandedPaths <= MaximumExpandedPathBytes,
                    node, "SVG expanded complexity exceeds 16384 nodes, 65536 numbers or 262144 path bytes.");
                foreach (var child in node.Elements()) Visit(child, references, linear, translation);
                if (_references.TryGetValue(node, out var targets)) foreach (var target in targets) Visit(_ids[target.Id], references + 1, linear, translation);
                active.Remove(node);
            }
            Visit(root, 0, 1, 0);
            double[]? view = root.Attribute("viewBox") is { } box ? Numbers(box.Value, box, count: false) : null;
            double Dimension(string name, int index)
            {
                double value = root.Attribute(name) is { } attr ? Length(attr.Value, attr, allowPercent: false, count: false) : view?[index] ?? 0;
                Require(value > 0 && value <= 4096, root, "SVG requires positive intrinsic width/height up to 4096, or a viewBox fallback.");
                return value;
            }
            double width = Dimension("width", 2), height = Dimension("height", 3);
            if (view is not null)
                Require(width / view[2] <= MaximumNumber && height / view[3] <= MaximumNumber, root, "SVG intrinsic/viewBox scaling exceeds 1000000.");
            return (width, height);
        }

        private void CheckAttribute(UiXmlElement node, UiXmlAttribute a, UiXmlElement root)
        {
            string tag = node.Name.LocalName, name = a.Name.LocalName, value = Trim(a.Value);
            if (a.IsNamespaceDeclaration)
            {
                Require(node == root && (name == "xmlns" && value is "" or "http://www.w3.org/2000/svg" || name == "xlink" && value == "http://www.w3.org/1999/xlink"), a, "Only root SVG and xlink namespace declarations are supported.");
                return;
            }
            Require(a.Name.NamespaceName.Length == 0 || a.Name.NamespaceName == "http://www.w3.org/1999/xlink" && name == "href", a, "Unsupported SVG attribute namespace.");
            bool allowed = tag is "title" or "desc" ? name == "id" : Common.Contains(name) || tag switch
            {
                "svg" => name is "x" or "y" or "width" or "height" or "viewBox" or "preserveAspectRatio" or "version",
                "symbol" => name is "x" or "y" or "width" or "height" or "viewBox" or "preserveAspectRatio",
                "use" => name is "x" or "y" or "width" or "height" or "href",
                "clipPath" => name == "clipPathUnits",
                "mask" => name is "x" or "y" or "width" or "height" or "maskUnits" or "maskContentUnits" or "mask-type",
                "linearGradient" => name is "x1" or "y1" or "x2" or "y2" or "gradientUnits" or "gradientTransform" or "spreadMethod" or "href",
                "radialGradient" => name is "cx" or "cy" or "r" or "fx" or "fy" or "gradientUnits" or "gradientTransform" or "spreadMethod" or "href",
                "stop" => name is "offset" or "stop-color" or "stop-opacity",
                "rect" => name is "x" or "y" or "width" or "height" or "rx" or "ry",
                "circle" => name is "cx" or "cy" or "r",
                "ellipse" => name is "cx" or "cy" or "rx" or "ry",
                "line" => name is "x1" or "y1" or "x2" or "y2",
                "polyline" or "polygon" => name == "points",
                "path" => name == "d",
                _ => false
            };
            Require(allowed, a, "Unsupported SVG attribute. Use presentation attributes; SVG CSS/styles are unsupported.");
            if (value == "inherit" && name is "fill" or "stroke" or "color" or "stop-color" or "fill-rule" or "clip-rule"
                or "stroke-linecap" or "stroke-linejoin" or "display" or "visibility" or "opacity" or "fill-opacity" or "stroke-opacity"
                or "stop-opacity" or "stroke-miterlimit" or "stroke-dasharray" or "stroke-width" or "stroke-dashoffset") return;
            switch (name)
            {
                case "id": Require(Identifier(a.Value) && _ids.TryAdd(a.Value, node), a, "SVG IDs must be unique ASCII identifiers up to 64 characters."); break;
                case "href":
                    Require(!node.Attributes().Where(x => x.Name.LocalName == "href").Skip(1).Any(), a, "Specify only one href or xlink:href.");
                    Reference(node, value, tag == "use" ? "use" : "gradient", a); break;
                case "fill": case "stroke":
                    if (value.StartsWith("url(", StringComparison.Ordinal)) UrlReference(node, value, "paint", a);
                    else Require(value == "none" || Color(value, a), a, "Expected a static SVG color, currentColor, none or url(#id).");
                    break;
                case "clip-path": case "mask": if (value != "none") UrlReference(node, value, name, a); break;
                case "color": case "stop-color": Require(Color(value, a), a, "Expected a static SVG color or currentColor."); break;
                case "opacity": case "fill-opacity": case "stroke-opacity": case "stop-opacity": case "offset":
                    double opacity = Length(value, a, allowPx: false); Require(opacity >= 0 && opacity <= (value.EndsWith('%') ? 100 : 1), a, "SVG opacity/offset must be in 0..1 or 0..100%."); break;
                case "fill-rule": case "clip-rule": Require(value is "nonzero" or "evenodd", a, "Expected nonzero or evenodd."); break;
                case "stroke-linecap": Require(value is "butt" or "round" or "square", a, "Invalid SVG line cap."); break;
                case "stroke-linejoin": Require(value is "miter" or "round" or "bevel", a, "Invalid SVG line join."); break;
                case "stroke-miterlimit": Require(Scalar(value, a) >= 1, a, "Miter limit must be at least one."); break;
                case "stroke-dasharray": if (value != "none") { var dash = Numbers(value, a, allowUnits: true); Require(dash.Length is > 0 and <= 64 && dash.All(n => n >= 0) && dash.Any(n => n > 0), a, "Dash array requires nonnegative numbers with a nonzero entry."); } break;
                case "display": Require(value is "inline" or "none", a, "SVG display requires inline or none."); break;
                case "visibility": Require(value is "visible" or "hidden" or "collapse", a, "Invalid SVG visibility."); break;
                case "clipPathUnits": case "maskUnits": case "maskContentUnits": case "gradientUnits": Require(value is "userSpaceOnUse" or "objectBoundingBox", a, "Invalid SVG coordinate units."); break;
                case "mask-type": Require(value is "luminance" or "alpha", a, "Invalid SVG mask type."); break;
                case "spreadMethod": Require(value is "pad" or "reflect" or "repeat", a, "Invalid SVG gradient spread."); break;
                case "version": Require(value == "1.1", a, "SVG version must be 1.1 when specified."); break;
                case "preserveAspectRatio":
                    string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    Require(parts.Length is >= 1 and <= 2 && (parts[0] == "none" || parts[0] is "xMinYMin" or "xMidYMin" or "xMaxYMin" or "xMinYMid" or "xMidYMid" or "xMaxYMid" or "xMinYMax" or "xMidYMax" or "xMaxYMax")
                        && (parts.Length == 1 || parts[0] != "none" && parts[1] is "meet" or "slice"), a, "Invalid SVG preserveAspectRatio."); break;
                case "viewBox":
                    var box = Numbers(value, a); Require(box.Length == 4 && box[2] > 0 && box[2] <= 4096 && box[3] > 0 && box[3] <= 4096, a, "SVG viewBox requires four numbers and positive dimensions up to 4096."); break;
                case "points": var points = Numbers(value, a); Require(points.Length >= 4 && points.Length % 2 == 0, a, "SVG points require coordinate pairs."); break;
                case "transform": case "gradientTransform": Transform(value, a); break;
                case "d": PathData(a.Value, a); break;
                default:
                    double number = Length(value, a, allowPercent: !(node == root && name is "width" or "height"));
                    if (name is "width" or "height" or "r" or "rx" or "ry" or "stroke-width") Require(number >= 0, a, "SVG size cannot be negative.");
                    break;
            }
        }

        private void UrlReference(UiXmlElement node, string value, string kind, UiXmlAttribute a)
        {
            Require(value.StartsWith("url(", StringComparison.Ordinal) && value.EndsWith(')'), a, "SVG URLs must be unquoted internal url(#id) fragments.");
            Reference(node, Trim(value[4..^1]), kind, a);
        }
        private void Reference(UiXmlElement node, string value, string kind, UiXmlAttribute a)
        {
            Require(value.StartsWith('#') && Identifier(value[1..]), a, "SVG href must be an internal #id fragment.");
            if (!_references.TryGetValue(node, out var list)) _references.Add(node, list = []);
            list.Add((value[1..], kind));
        }
        private static bool Identifier(string value) => value.Length is >= 1 and <= 64 && (char.IsAsciiLetter(value[0]) || value[0] == '_')
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');
        private static readonly HashSet<string> ColorNames = new("aliceblue antiquewhite aqua aquamarine azure beige bisque black blanchedalmond blue blueviolet brown burlywood cadetblue chartreuse chocolate coral cornflowerblue cornsilk crimson cyan darkblue darkcyan darkgoldenrod darkgray darkgreen darkgrey darkkhaki darkmagenta darkolivegreen darkorange darkorchid darkred darksalmon darkseagreen darkslateblue darkslategray darkslategrey darkturquoise darkviolet deeppink deepskyblue dimgray dimgrey dodgerblue firebrick floralwhite forestgreen fuchsia gainsboro ghostwhite gold goldenrod gray green greenyellow grey honeydew hotpink indianred indigo ivory khaki lavender lavenderblush lawngreen lemonchiffon lightblue lightcoral lightcyan lightgoldenrodyellow lightgray lightgreen lightgrey lightpink lightsalmon lightseagreen lightskyblue lightslategray lightslategrey lightsteelblue lightyellow lime limegreen linen magenta maroon mediumaquamarine mediumblue mediumorchid mediumpurple mediumseagreen mediumslateblue mediumspringgreen mediumturquoise mediumvioletred midnightblue mintcream mistyrose moccasin navajowhite navy oldlace olive olivedrab orange orangered orchid palegoldenrod palegreen paleturquoise palevioletred papayawhip peachpuff peru pink plum powderblue purple rebeccapurple red rosybrown royalblue saddlebrown salmon sandybrown seagreen seashell sienna silver skyblue slateblue slategray slategrey snow springgreen steelblue tan teal thistle tomato transparent turquoise violet wheat white whitesmoke yellow yellowgreen".Split(' '), StringComparer.Ordinal);
        private bool Color(string value, UiXmlAttribute a)
        {
            if (value == "currentColor" || ColorNames.Contains(value) ||
                value.Length is 4 or 5 or 7 or 9 && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0) return true;
            bool rgba = value.StartsWith("rgba(", StringComparison.Ordinal);
            if ((!rgba && !value.StartsWith("rgb(", StringComparison.Ordinal)) || !value.EndsWith(')')) return false;
            string[] parts = value[(rgba ? 5 : 4)..^1].Split(',');
            if (parts.Length != (rgba ? 4 : 3)) return false;
            bool percentRgb = Trim(parts[0]).EndsWith('%');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = Trim(parts[i]); bool percent = part.EndsWith('%');
                if (i < 3 && percent != percentRgb) return false;
                double n = Length(part, a, allowPx: false);
                if (n < 0 || n > (percent ? 100 : i == 3 ? 1 : 255)) return false;
            }
            return true;
        }

        private static bool Space(char c) => c is ' ' or '\t' or '\r' or '\n';
        private static string Trim(string value) => value.Trim(' ', '\t', '\r', '\n');

        private double Length(string value, UiXmlAttribute a, bool allowPercent = true, bool allowPx = true, bool count = true)
        {
            value = Trim(value);
            bool unit = false;
            if (allowPx && value.EndsWith("px", StringComparison.Ordinal)) { value = value[..^2]; unit = true; }
            else if (allowPercent && value.EndsWith('%')) { value = value[..^1]; unit = true; }
            Require(!unit || value.Length > 0 && !Space(value[^1]), a, "SVG units must immediately follow their number.");
            var numbers = Numbers(value, a, count);
            Require(numbers.Length == 1, a, "Expected one bounded SVG number, optionally px or percent where supported.");
            return numbers[0];
        }
        private double Scalar(string value, UiXmlAttribute a) => Length(value, a, allowPercent: false, allowPx: false);
        private double[] Numbers(string value, UiXmlAttribute a, bool count = true, bool allowUnits = false, List<string>? lexical = null)
        {
            var result = new List<double>();
            int i = 0;
            bool comma = false;
            while (i < value.Length)
            {
                if (Space(value[i])) { i++; continue; }
                if (value[i] == ',') { Require(result.Count > 0 && !comma, a, "Unexpected SVG number separator."); comma = true; i++; continue; }
                int start = i;
                if (value[i] is '+' or '-') i++;
                int digits = 0;
                while (i < value.Length && char.IsAsciiDigit(value[i])) { i++; digits++; }
                if (i < value.Length && value[i] == '.') { i++; while (i < value.Length && char.IsAsciiDigit(value[i])) { i++; digits++; } }
                Require(digits > 0, a, "Malformed SVG number.");
                if (i < value.Length && value[i] is 'e' or 'E')
                {
                    i++; if (i < value.Length && value[i] is '+' or '-') i++;
                    int exponent = i;
                    while (i < value.Length && char.IsAsciiDigit(value[i])) i++;
                    Require(i > exponent && int.TryParse(value.AsSpan(exponent, i - exponent), NumberStyles.None, CultureInfo.InvariantCulture, out int magnitude)
                        && magnitude <= 308, a, "SVG exponent absolute value must be at most 308.");
                }
                Require(i - start <= 64, a, "SVG numeric tokens must be at most 64 characters.");
                Require(double.TryParse(value.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                    && double.IsFinite(number) && Math.Abs(number) <= MaximumNumber, a, "SVG numbers must be finite with absolute value at most 1000000.");
                if (count) Require(++_numbers <= MaximumNumbers, a, "SVG exceeds 16384 numeric tokens.");
                lexical?.Add(value[start..i]);
                result.Add(number); comma = false;
                if (allowUnits)
                {
                    if (value.AsSpan(i).StartsWith("px")) i += 2;
                    else if (i < value.Length && value[i] == '%') i++;
                }
                Require(i == value.Length || Space(value[i]) || value[i] is ',' or '+' or '-' or '.', a, "Unsupported SVG number suffix.");
            }
            Require(!comma, a, "Trailing SVG number separator.");
            return result.ToArray();
        }

        private void Transform(string value, UiXmlAttribute a)
        {
            int i = 0, operations = 0;
            double[] matrix = [1, 0, 0, 1, 0, 0];
            while (i < value.Length)
            {
                while (i < value.Length && Space(value[i])) i++;
                if (i == value.Length) break;
                int start = i;
                while (i < value.Length && char.IsAsciiLetter(value[i])) i++;
                string kind = value[start..i];
                while (i < value.Length && Space(value[i])) i++;
                Require(i < value.Length && value[i++] == '(', a, "Invalid SVG transform function.");
                int end = value.IndexOf(')', i);
                Require(end >= i, a, "Unterminated SVG transform.");
                var n = Numbers(value[i..end], a); i = end + 1;
                Require(kind switch { "matrix" => n.Length == 6, "translate" or "scale" => n.Length is 1 or 2,
                    "rotate" => n.Length is 1 or 3, "skewX" or "skewY" => n.Length == 1, _ => false }, a, "Unsupported SVG transform or argument count.");
                if (kind is "skewX" or "skewY") Require(Math.Abs(Math.Cos(n[0] * Math.PI / 180)) > 0.000001, a, "SVG skew is too close to a singular angle.");
                double[] next = kind switch
                {
                    "matrix" => n,
                    "translate" => [1, 0, 0, 1, n[0], n.Length == 2 ? n[1] : 0],
                    "scale" => [n[0], 0, 0, n.Length == 2 ? n[1] : n[0], 0, 0],
                    "skewX" => [1, 0, Math.Tan(n[0] * Math.PI / 180), 1, 0, 0],
                    "skewY" => [1, Math.Tan(n[0] * Math.PI / 180), 0, 1, 0, 0],
                    _ => Rotation(n)
                };
                matrix = [matrix[0] * next[0] + matrix[2] * next[1], matrix[1] * next[0] + matrix[3] * next[1],
                    matrix[0] * next[2] + matrix[2] * next[3], matrix[1] * next[2] + matrix[3] * next[3],
                    matrix[0] * next[4] + matrix[2] * next[5] + matrix[4], matrix[1] * next[4] + matrix[3] * next[5] + matrix[5]];
                Require(matrix.All(n => double.IsFinite(n) && Math.Abs(n) <= MaximumNumber), a, "SVG composed transform components exceed 1000000.");
                Require(++operations <= 128, a, "SVG transform exceeds 128 operations.");
                while (i < value.Length && Space(value[i])) i++;
                if (i < value.Length && value[i] == ',')
                {
                    i++; while (i < value.Length && Space(value[i])) i++;
                    Require(i < value.Length, a, "Trailing SVG transform comma.");
                }
            }
            Require(operations > 0, a, "SVG transform cannot be empty.");
            double localLinear = Math.Max(Math.Abs(matrix[0]) + Math.Abs(matrix[2]), Math.Abs(matrix[1]) + Math.Abs(matrix[3]));
            double localTranslation = Math.Max(Math.Abs(matrix[4]), Math.Abs(matrix[5]));
            var previous = _transforms.GetValueOrDefault(_current!, (Linear: 1.0, Translation: 0.0));
            _transforms[_current!] = (previous.Linear * localLinear, previous.Linear * localTranslation + previous.Translation);

            static double[] Rotation(double[] n)
            {
                double c = Math.Cos(n[0] * Math.PI / 180), s = Math.Sin(n[0] * Math.PI / 180);
                double x = n.Length == 3 ? n[1] : 0, y = n.Length == 3 ? n[2] : 0;
                return [c, s, -s, c, x - c * x + s * y, y - s * x - c * y];
            }
        }

        private void PathData(string value, UiXmlAttribute a)
        {
            // Match native XML line/attribute normalization; the full raw source
            // still has its independent 256 KiB encoded-byte cap.
            _pathBytes += UiSourceFiles.StrictUtf8.GetByteCount(value);
            Require(_pathBytes <= MaximumPathBytes, a, "SVG exceeds 65536 total path-data bytes.");
            int i = 0;
            bool first = true;
            while (i < value.Length)
            {
                while (i < value.Length && Space(value[i])) i++;
                if (i == value.Length) break;
                char command = value[i++];
                int arity = char.ToUpperInvariant(command) switch { 'M' or 'L' or 'T' => 2, 'H' or 'V' => 1, 'C' => 6, 'S' or 'Q' => 4, 'A' => 7, 'Z' => 0, _ => -1 };
                Require(arity >= 0 && (!first || command is 'M' or 'm'), a, "SVG paths require moveto first and supported path commands.");
                first = false;
                int start = i;
                while (i < value.Length && (!char.IsAsciiLetter(value[i]) || value[i] is 'e' or 'E')) i++;
                var tokens = new List<string>();
                var n = Numbers(value[start..i], a, lexical: tokens);
                Require(arity == 0 ? n.Length == 0 : n.Length >= arity && n.Length % arity == 0, a, "SVG path command has the wrong number of arguments.");
                if (command is 'A' or 'a')
                    for (int j = 0; j < n.Length; j += 7)
                        Require(n[j] >= 0 && n[j + 1] >= 0 && tokens[j + 3] is "0" or "1" && tokens[j + 4] is "0" or "1", a, "SVG arc requires nonnegative radii and zero/one flags.");
            }
        }
        private void Require(bool condition, UiXmlObject? node, string message) { if (!condition) throw Error(file, node ?? _current, message); }
    }

    private static UiAuthoringException Error(string file, UiXmlObject? node, string cause) =>
        new("UI_SVG", file, node?.LineNumber ?? 1, node?.LinePosition ?? 1,
            node is UiXmlAttribute a ? "svg/" + a.Parent?.Name.LocalName + "@" + a.Name.LocalName : "svg/" + (node as UiXmlElement)?.Name.LocalName, cause);
}
