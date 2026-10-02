using System.Text;

namespace GameAuthoringLab;

// CPU-only SVG surface, immutable snapshot, security-profile and finite-budget contracts.
// Real optional-backend rendering and reload ownership are covered by UiSvgTests.
internal static class UiSvgAuthoringTests
{
    private static readonly UiBindingTarget[] Targets = [new("heading", UiBindingKind.Text)];
    private const string BaseStyle = "body { font-family: \"Noto Sans CJK SC\"; font-size: 16px; }";
    private const string Good = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"16\" viewBox=\"0 0 32 16\"><rect width=\"32\" height=\"16\" fill=\"#ffffff\"/></svg>";
    private static string Markup(string content) => "<rml><head><title>SVG fixture</title><link type=\"text/rcss\" href=\"svg.rcss\"/></head><body><p id=\"heading\">Initial</p>" + content + "</body></rml>";
    private static BoundUiDocument Parse(string content, string style = BaseStyle) => BoundUiAuthoring.Validate(
        Encoding.UTF8.GetBytes(Markup(content)), Encoding.UTF8.GetBytes(style), Targets, "svg.rml", "svg.rcss");
    private static SvgAsset Svg(string content) => SvgAsset.Validate(Encoding.UTF8.GetBytes(content));
    private static string Wrap(string content) => "<svg width=\"32\" height=\"16\">" + content + "</svg>";

    internal static int RunContracts()
    {
        int count = 0;
        void Check(bool condition, string why) { if (!condition) throw new Exception("UI SVG CONTRACT: " + why); count++; }
        UiAuthoringException Reject(Action action, string why)
        {
            try { action(); } catch (UiAuthoringException e) { count++; return e; }
            throw new Exception("UI SVG CONTRACT accepted: " + why);
        }
        var parsed = Parse("<svg src=\"icons/glyph.SVG\" width=\"32\" height=\"16\" id=\"icon\"/><img src=\"tile.png\"/>",
            BaseStyle + " #icon { decorator: svg(icons/glyph.SVG); image-color: #8040c0; opacity: 0.5; } body { decorator: image(tile.png); }");
        Check(parsed.References.Select(r => r.Path).SequenceEqual(new[] { "icons/glyph.SVG", "tile.png" }), "SVG and raster references share bounded deduplicated ownership");
        Check(Parse("<div><svg src=\"icons/glyph.svg\"/></div>").References.Count == 1, "file-backed SVG may have a static wrapper");
        foreach (string content in new[] { "<svg/>", "<svg width=\"32\" height=\"16\"><rect/></svg>", "<svg src=\"glyph.svg\">inline</svg>",
            "<svg src=\"glyph.svg\"><div/></svg>", "<svg src=\"glyph.png\"/>", "<img src=\"glyph.svg\"/>", "<svg src=\"glyph.svg\" viewBox=\"0 0 32 16\"/>",
            "<svg src=\"../glyph.svg\"/>", "<svg src=\"https://example.invalid/glyph.svg\"/>", "<svg src=\"data:image/svg+xml,bad\"/>",
            "<svg src=\"glyph.svg#id\"/>", "<svg src=\"glyph.svg\" width=\"0\"/>", "<svg src=\"glyph.svg\" height=\"4097\"/>", "<svg src=\"glyph.svg\" width=\"5px\"/>" })
            Reject(() => Parse(content), "unsupported RML SVG: " + content);
        foreach (string value in new[] { "svg(tile.png)", "image(glyph.svg)", "svg(../glyph.svg)", "svg(\"glyph.svg\")", "svg( glyph.svg )", "svg(glyph.svg) svg(other.svg)",
            "url(glyph.svg)", "SVG(glyph.svg)", "svg(glyph.svg#id)", "svg(data:image/svg+xml,bad)" })
            Reject(() => Parse("", BaseStyle + " body { decorator: " + value + "; }"), "unsupported RCSS SVG: " + value);
        foreach (string value in new[] { "-0.1", "1.1", "NaN", "Infinity", "0.5junk", "url(foo)", "1e0" })
            Reject(() => Parse("", BaseStyle + " body { opacity: " + value + "; }"), "invalid UI opacity " + value);
        Reject(() => Parse(string.Concat(Enumerable.Range(0, 33).Select(i => "<svg src=\"" + i + ".svg\"/>"))), "mixed resource unique-count cap applies to SVG");

        var good = Svg(Good);
        Check(good.Info.Width == 32 && good.Info.Height == 16 && good.DecodedBytes == 2048, "intrinsic SVG metadata preflight");
        Check(Svg("<svg viewBox=\"0 0 24 12\"/>").Info.Width == 24, "viewBox-only intrinsic fallback");
        Check(Svg("<svg width=\"24px\" viewBox=\"0 0 48 12\"/>").Info.Height == 12, "one missing root dimension uses viewBox");
        Check(Svg("<svg width=\"1.5\" height=\"2.5\"/>").DecodedBytes == 24, "fractional intrinsic dimensions reserve ceiling RGBA budget");
        Check(Svg(Wrap("<rect fill=\"rgba(100%, 0%, 50%, .5)\" stroke=\"rgb(255, 128, 0)\" stroke-dasharray=\"1px,2%,3\" opacity=\"inherit\"/>")).Info.Width == 32,
            "safe RGB colors, length dash units and presentation inheritance");
        Check(Svg(Wrap("<rect fill=\" rebeccapurple \" stroke=\"currentColor\" x=\" 1px \" y=\"1e-308\"/>")).Info.Width == 32,
            "named colors, ASCII outer whitespace and bounded exponents");
        Svg("<?xml version=\"1.0\" encoding=\"UTF-8\"?><svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 32 16\">" +
            "<title>Static art</title><desc>Shapes only</desc><defs>" +
            "<linearGradient id=\"paint\" x1=\"0%\" x2=\"100%\"><stop offset=\"0\" stop-color=\"red\"/><stop offset=\"100%\" stop-color=\"blue\"/></linearGradient>" +
            "<linearGradient id=\"copy\" xlink:href=\"#paint\" gradientTransform=\"rotate(20)\"/>" +
            "<radialGradient id=\"radial\" cx=\"50%\" cy=\"50%\" r=\"50%\"><stop offset=\"0.5\" stop-color=\"#ffffff\"/></radialGradient>" +
            "<clipPath id=\"cut\"><circle cx=\"8\" cy=\"8\" r=\"8\"/></clipPath><mask id=\"fade\" maskUnits=\"userSpaceOnUse\"><rect width=\"32\" height=\"16\" fill=\"white\"/></mask>" +
            "<symbol id=\"shape\" viewBox=\"0 0 32 16\"><rect width=\"32\" height=\"16\" fill=\"currentColor\"/></symbol></defs>" +
            "<g color=\"#8844cc\" transform=\"translate(1,2) scale(.5)\" opacity=\".75\"><use xlink:href=\"#shape\" width=\"32\" height=\"16\"/>" +
            "<rect width=\"32\" height=\"16\" fill=\"url(#copy)\" clip-path=\"url(#cut)\" mask=\"url(#fade)\"/>" +
            "<ellipse cx=\"4\" cy=\"5\" rx=\"2\" ry=\"3\"/><line x1=\"0\" y1=\"0\" x2=\"8\" y2=\"8\" stroke=\"white\"/>" +
            "<polyline points=\"0,0 4,4 8,0\"/><polygon points=\"0,0 4,4 8,0\"/>" +
            "<path d=\"M0 0 H8 V8 h-4 v-4 L4 4 l1 1 C1 2 3 4 5 6 S7 8 9 10 Q1 2 3 4 T5 6 A2 3 20 0 1 8 9 z\" fill=\"none\" stroke=\"url(#radial)\"/></g></svg>");
        count++;
        foreach (string tag in new[] { "script", "style", "text", "image", "filter", "foreignObject", "animate", "animateTransform", "set", "pattern", "a", "feGaussianBlur" })
            Reject(() => Svg(Wrap("<" + tag + "/>")), "excluded SVG tag " + tag);
        foreach (string attr in new[] { "style=\"fill:red\"", "class=\"art\"", "onclick=\"run()\"", "onload=\"run()\"", "data-x=\"x\"", "filter=\"url(#fx)\"", "font-family=\"x\"", "xml:base=\"https://example.invalid\"" })
            Reject(() => Svg(Wrap("<rect width=\"1\" height=\"1\" " + attr + "/>")), "excluded SVG attr " + attr);
        foreach (string target in new[] { "https://example.invalid/x.svg#id", "other.svg#id", "data:image/svg+xml,x", "file:///tmp/x", "#missing", "#bad%20id", "#" })
        {
            Reject(() => Svg(Wrap("<use href=\"" + target + "\"/>")), "unsafe/unresolved href " + target);
            Reject(() => Svg(Wrap("<rect fill=\"url(" + target + ")\"/>")), "unsafe/unresolved paint URL " + target);
        }
        foreach (string content in new[] { "<!DOCTYPE svg><svg width=\"1\" height=\"1\"/>",
            "<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><svg width=\"1\" height=\"1\"><title>&x;</title></svg>",
            Wrap("<title>&amp;</title>"), Wrap("<title>&#65;</title>"), Wrap("<rect fill=\"&#35;fff\"/>"), Wrap("<![CDATA[ignored]]>"), Wrap("<?work ignored?>"),
            "<svg width=\"32\" height=\"16\" xmlns=\"https://example.invalid/svg\"/>", "<svg:svg xmlns:svg=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"1\"/>",
            "<svg width=\"1\" height=\"1\"><rect></svg>", "<svg width=\"1\" height=\"1\"/>trailing", "<?xml version=\"1.0\" encoding=\"UTF-16\"?><svg width=\"1\" height=\"1\"/>" })
            Reject(() => Svg(content), "unsafe or malformed XML");
        Reject(() => SvgAsset.Validate([0xff, 0xfe, 0x01]), "invalid UTF-8 rejected");
        foreach (string content in new[] { "<svg/>", "<svg width=\"0\" height=\"1\"/>", "<svg width=\"4097\" height=\"1\"/>", "<svg width=\"100%\" height=\"1\"/>",
            "<svg viewBox=\"0 0 -1 2\"/>", "<svg viewBox=\"0 0 4097 1\"/>", "<svg width=\"NaN\" height=\"1\"/>", "<svg width=\"1e309\" height=\"1\"/>",
            Wrap("<rect x=\"1000001\"/>"), Wrap("<rect x=\"1junk\"/>"), Wrap("<path d=\"L0 0\"/>"), Wrap("<path d=\"M0 0 L1\"/>"), Wrap("<path d=\"M0 0 A1 1 0 2 0 3 3\"/>"),
            Wrap("<path d=\"M0 0 X1 2\"/>"), Wrap("<g transform=\"scale()\"/>"), Wrap("<g transform=\"unknown(1)\"/>"), Wrap("<g transform=\"skewX(90)\"/>"),
            Wrap("<path d=\"M0 0 A1 1 0 1.0 0 3 3\"/>"), Wrap("<path d=\"M0 0 A1 1 0 +1 0 3 3\"/>"), Wrap("<path d=\"M0 0 A1 1 0 01 0 3 3\"/>"),
            Wrap("<rect x=\"1e-309\"/>"), Wrap("<rect x=\"1 px\"/>"), Wrap("<rect x=\"" + new string('0', 65) + "\"/>"),
            Wrap("<rect fill=\"rgb(101%,0%,0%)\"/>"), Wrap("<rect fill=\"rgb(255,0%,0)\"/>"), Wrap("<rect fill=\"rgba(255,0,0,2)\"/>"),
            Wrap("<g transform=\",scale(1)\"/>"), Wrap("<g transform=\"scale(1),\"/>"), Wrap("<g transform=\"scale(1),,scale(1)\"/>"),
            Wrap("<g transform=\"scale(1000000)\"><g transform=\"scale(2)\"/></g>"),
            Wrap("<g transform=\"translate(1000000)\"><g transform=\"translate(1)\"/></g>"),
            Wrap("<defs><path id=\"large\" transform=\"scale(1000000)\" d=\"M0 0\"/></defs><use href=\"#large\" transform=\"scale(2)\"/>"),
            "<svg width=\"1\" height=\"1\" viewBox=\"0 0 1e-308 1\"/>",
            Wrap("<g transform=\"scale(1000000) scale(2)\"/>"), Wrap("<rect x=\"1\u00a0\"/>"), Wrap("<g>\u00a0</g>"),
            Wrap("<rect opacity=\"2\"/>"), Wrap("<polygon points=\"0 1 2\"/>"), Wrap("<rect stroke-dasharray=\"0 0\"/>"), Wrap("<title><g/></title>"), Wrap("unwrapped text") })
            Reject(() => Svg(content), "malformed/unsupported SVG value");
        foreach (string content in new[] { "<use id=\"a\" href=\"#a\"/>", "<use id=\"a\" href=\"#b\"/><use id=\"b\" href=\"#a\"/>",
            "<g id=\"a\"><use href=\"#a\"/></g>", "<linearGradient id=\"a\" href=\"#b\"/><linearGradient id=\"b\" href=\"#a\"/>",
            "<clipPath id=\"a\"><rect clip-path=\"url(#a)\"/></clipPath>", "<mask id=\"a\"><rect mask=\"url(#a)\"/></mask>",
            "<rect id=\"a\"/><circle id=\"a\"/>", "<rect id=\"a\"/><rect fill=\"url(#a)\"/>", "<linearGradient id=\"a\"/><use href=\"#a\"/>",
            "<rect id=\"a\"/><use href=\"#a\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" xlink:href=\"#a\"/>" })
            Reject(() => Svg(Wrap(content)), "cyclic/invalid fragment graph");

        Check(Svg(Wrap(string.Concat(Enumerable.Repeat("<g/>", 2047)))).Info.Width == 32, "inclusive element-node budget");
        Reject(() => Svg(Wrap(string.Concat(Enumerable.Repeat("<g/>", 2048)))), "element-node budget exceeded");
        Check(Svg(Wrap(string.Concat(Enumerable.Repeat("<g>", 31)) + string.Concat(Enumerable.Repeat("</g>", 31)))).Info.Width == 32, "inclusive XML depth budget");
        Reject(() => Svg(Wrap(string.Concat(Enumerable.Repeat("<g>", 32)) + string.Concat(Enumerable.Repeat("</g>", 32)))), "XML depth budget exceeded");
        Check(Svg(Wrap("<path d=\"M" + string.Join(' ', Enumerable.Repeat("0", 16382)) + "\"/>")).Info.Width == 32, "inclusive numeric budget includes root dimensions");
        Reject(() => Svg(Wrap("<path d=\"M" + string.Join(' ', Enumerable.Repeat("0", 16384)) + "\"/>")), "numeric budget exceeded");
        Check(Svg(Wrap("<path d=\"M0 0" + new string(' ', 65532) + "\"/>")).Info.Width == 32, "inclusive path-data byte budget");
        Reject(() => Svg(Wrap("<path d=\"M0 0" + new string(' ', 65533) + "\"/>")), "path-data byte budget exceeded");
        Check(Svg(Wrap("<path d=\"M0 0" + string.Concat(Enumerable.Repeat("\r\n", 32767)) + "\"/>")).Info.Width == 32,
            "path-byte budget uses XML-normalized attributes under the raw encoded-file cap");
        Check(Svg(Wrap("<title>图形</title><rect\r\n x=\"1\" width=\"2\" height=\"3\"/>")).Info.Width == 32, "XML normalization supports Unicode text and CRLF attribute positions");
        string padded = Good + "<!--" + new string(' ', SvgAsset.MaximumEncodedBytes - Encoding.UTF8.GetByteCount(Good) - 7) + "-->";
        Check(Svg(padded).EncodedLength == SvgAsset.MaximumEncodedBytes, "inclusive encoded byte budget");
        Reject(() => Svg(padded + " "), "encoded byte budget exceeded");
        string expansion = "<defs><path id=\"n0\" d=\"M0 0L1 1\"/>" + string.Concat(Enumerable.Range(1, 14).Select(i =>
            "<g id=\"n" + i + "\"><use href=\"#n" + (i - 1) + "\"/><use href=\"#n" + (i - 1) + "\"/></g>")) + "</defs><use href=\"#n14\"/>";
        Reject(() => Svg(Wrap(expansion)), "acyclic exponential use expansion");
        string chain = "<defs><path id=\"n0\" d=\"M0 0\"/>" + string.Concat(Enumerable.Range(1, 17).Select(i =>
            "<use id=\"n" + i + "\" href=\"#n" + (i - 1) + "\"/>")) + "</defs><use href=\"#n17\"/>";
        Reject(() => Svg(Wrap(chain)), "reference-depth budget");

        string directory = Path.Combine(Path.GetTempPath(), "gal-svg-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "ui/icons"));
        try
        {
            var assets = new AssetRoot(directory);
            File.WriteAllText(assets.FilePath("ui/svg.rml"), Markup("<svg src=\"icons/glyph.svg\"/>"), Encoding.UTF8);
            File.WriteAllText(assets.FilePath("ui/svg.rcss"), BaseStyle, Encoding.UTF8);
            File.WriteAllText(assets.FilePath("ui/icons/glyph.svg"), Good, new UTF8Encoding(false));
            var source = BoundUiAuthoring.ValidateAsset(assets, "ui/svg.rml", Targets);
            Check(source.Images.Count == 1 && source.Images[0].Asset.IsSvg && source.Images[0].Asset.Info.Width == 32, "file-backed validated SVG snapshot and metadata");
            File.WriteAllText(assets.FilePath("ui/icons/glyph.svg"), "<script/>");
            using (var stage = UiSourceStaging.Create(source))
                Check(File.ReadAllText(Path.Combine(stage.DirectoryPath, "icons/glyph.svg")) == Good, "staging consumes exact owned SVG bytes after source replacement");
            var diagnostic = Reject(() => BoundUiAuthoring.ValidateAsset(assets, "ui/svg.rml", Targets), "invalid SVG source before staging");
            Check(diagnostic.Code == "UI_SVG" && diagnostic.FilePath.EndsWith("glyph.svg", StringComparison.Ordinal) && diagnostic.Line >= 1, "SVG diagnostics identify file and source location");
            File.Delete(assets.FilePath("ui/icons/glyph.svg"));
            Reject(() => BoundUiAuthoring.ValidateAsset(assets, "ui/svg.rml", Targets), "missing SVG source");
            File.WriteAllText(assets.FilePath("ui/icons/glyph.svg"), Good);
            try { assets.ReadImageInfo("ui/icons/glyph.svg"); throw new Exception("UI SVG CONTRACT world image accepted SVG"); }
            catch (AssetException e) { Check(e.Code == "ASSET_IMAGE_FORMAT", "world image surface remains raster-only"); }
            if (!OperatingSystem.IsWindows())
            {
                File.CreateSymbolicLink(assets.FilePath("ui/icons/link.svg"), assets.FilePath("ui/icons/glyph.svg"));
                File.WriteAllText(assets.FilePath("ui/svg.rml"), Markup("<svg src=\"icons/link.svg\"/>"));
                Reject(() => BoundUiAuthoring.ValidateAsset(assets, "ui/svg.rml", Targets), "SVG symlink path rejected");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
        var defaults = new AssetRoot();
        var settings = UiAuthoring.ValidateAsset(defaults, "ui/settings.rml");
        Reject(() => UiAuthoring.Validate(Encoding.UTF8.GetBytes(settings.Rml.Replace("</body>", "<svg src=\"glyph.svg\"/></body>", StringComparison.Ordinal)), Encoding.UTF8.GetBytes(settings.Rcss)), "settings profile rejects SVG element");
        Reject(() => UiAuthoring.Validate(Encoding.UTF8.GetBytes(settings.Rml), Encoding.UTF8.GetBytes(settings.Rcss + " body { decorator: svg(glyph.svg); }")), "settings profile rejects SVG decorator");
        var game = GameUiAuthoring.ValidateAsset(defaults, "ui/game.rml");
        Reject(() => GameUiAuthoring.Validate(Encoding.UTF8.GetBytes(game.Rml.Replace("</body>", "<svg src=\"glyph.svg\"/></body>", StringComparison.Ordinal)), Encoding.UTF8.GetBytes(game.Rcss)), "game profile rejects SVG element");
        Reject(() => GameUiAuthoring.Validate(Encoding.UTF8.GetBytes(game.Rml), Encoding.UTF8.GetBytes(game.Rcss + " body { decorator: svg(glyph.svg); }")), "game profile rejects SVG decorator");
        Console.WriteLine($"UI SVG CONTRACT PASS assertions={count}");
        return count;
    }
}
