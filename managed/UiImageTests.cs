using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace GameAuthoringLab;

/// <summary>
/// Static-image contracts need no renderer or codec package. Graphics checks read real
/// GPU captures, including alpha and failed-reload preservation, rather than trusting
/// only UI status. All image fixtures are generated here or embedded below.
/// </summary>
internal static class UiImageTests
{
    private static readonly UiBindingTarget[] Targets = [new("heading", UiBindingKind.Text), new("go", UiBindingKind.Action, 7)];
    private const string BaseStyle = "body { font-family: \"Noto Sans CJK SC\"; font-size: 16px; }";
    private static string Markup(string content, string name = "images") =>
        "<rml><head><title>Image fixture</title><link type=\"text/rcss\" href=\"" + name +
        ".rcss\" /></head><body><p id=\"heading\">Initial</p><button id=\"go\">Go</button>" + content + "</body></rml>";
    private static BoundUiDocument Parse(string content, string css = BaseStyle) =>
        BoundUiAuthoring.Validate(Encoding.UTF8.GetBytes(Markup(content)), Encoding.UTF8.GetBytes(css), Targets, "images.rml", "images.rcss");

    public static int RunContracts()
    {
        int count = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("UI IMAGE CONTRACT: " + why); count++; }
        UiAuthoringException Reject(Action action, string why)
        {
            try { action(); }
            catch (UiAuthoringException error) { count++; return error; }
            throw new Exception("UI IMAGE CONTRACT accepted: " + why);
        }

        var noImages = Parse("");
        Check(noImages.References.Count == 0, "existing bound documents do not acquire image resources");
        var grammar = Parse("<img src=\"images/icon.png\"/><img id=\"logo\" class=\"art\" src=\"images/icon.png\" width=\"16\" height=\"32\"/>",
            BaseStyle + " #logo { decorator: none; } .art { decorator: image(images/background.jpeg); }");
        Check(grammar.References.Select(r => r.Path).SequenceEqual(new[] { "images/icon.png", "images/background.jpeg" }),
            "img and decorator references deduplicate in encounter order");
        Check(Parse("<div><img src=\"images/nested/icon.BMP\" /></div>").References.Count == 1, "static wrapper, nested paths and case-insensitive extension");
        Check(Parse("<img src=\"a.JPG\" width=\"4096\" height=\"1\" />").References.Count == 1, "explicit dimensions at accepted bounds");
        string maximumPath = new('a', 251);
        Check(Parse("<img src=\"" + maximumPath + ".png\" />").References.Count == 1, "255-byte image path accepted");
        foreach (string path in new[] { "", "/a.png", "../a.png", "a/../b.png", "./a.png", "a/./b.png", "a//b.png", "a.png/", "a./b.png",
            "https://example.invalid/a.png", "file:///a.png", "data:image/png;base64,AAAA", "C:/a.png", "a\\b.png", "a%2fb.png", "a.png?x=1", "a.png#x",
            "a b.png", "图.png", "a.svg", "a.gif", "a.webp", "a", new string('a', 252) + ".png" })
        {
            var error = Reject(() => Parse("<img src=\"" + path + "\" />"), "image path " + path);
            Check(error.Line > 0 && error.Column > 0 && error.FilePath == "images.rml", "image diagnostic identifies authored source");
        }
        foreach (string element in new[] { "<img/>", "<img src=\"a.png\">text</img>", "<img src=\"a.png\"><div/></img>",
            "<img src=\"a.png\" alt=\"no\"/>", "<img src=\"a.png\" onclick=\"x\"/>", "<img src=\"a.png\" data-src=\"x\"/>",
            "<img src=\"{{image}}\"/>", "<img src=\"a.png\" width=\"0\"/>", "<img src=\"a.png\" height=\"4097\"/>",
            "<img src=\"a.png\" width=\"16px\"/>", "<img src=\"a.png\" width=\"1.5\"/>", "<img src=\"a.png\" width=\"-1\"/>",
            "<img src=\"a.png\" width=\"+1\"/>", "<img src=\"a.png\" width=\" 1\"/>", "<img src=\"a.png\" height=\"\"/>" })
            Reject(() => Parse(element), "unsupported image markup " + element);
        Reject(() => BoundUiAuthoring.Validate(Encoding.UTF8.GetBytes(Markup("").Replace("<p id=\"heading\">Initial</p>",
            "<div id=\"heading\"><img src=\"a.png\"/></div>", StringComparison.Ordinal)), Encoding.UTF8.GetBytes(BaseStyle), Targets, "images.rml", "images.rcss"),
            "bound target cannot own nested image markup");
        Reject(() => BoundUiAuthoring.Validate(Encoding.UTF8.GetBytes(Markup("").Replace("<body>", "<body>text", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(BaseStyle), Targets, "images.rml", "images.rcss"), "body stays free of direct text");
        foreach (string value in new[] { "url(a.png)", "image(../a.png)", "image(\"a.png\")", "image('a.png')", "image(a.png, b.png)",
            "image(a.png) image(b.png)", "image(a.png) junk", "image()", "Image(a.png)", "image(a.svg)", "image( a.png )" })
            Reject(() => Parse("", BaseStyle + " body { decorator: " + value + "; }"), "decorator grammar " + value);
        Reject(() => Parse("", BaseStyle + " body { background-image: url(a.png); }"), "other resource CSS remains forbidden");
        Reject(() => BoundUiAuthoring.Validate(Encoding.UTF8.GetBytes(Markup("").Replace("images.rcss", "other.rcss", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(BaseStyle), Targets, "images.rml", "other.rcss"), "stylesheet remains a same-basename sibling");
        string thirtyTwo = string.Concat(Enumerable.Range(0, 32).Select(i => "<img src=\"icons/" + i + ".png\"/>"));
        Check(Parse(thirtyTwo + "<img src=\"icons/0.png\"/>").References.Count == 32, "32 unique images plus duplicate accepted");
        Reject(() => Parse(thirtyTwo + "<img src=\"icons/32.png\"/>"), "33rd img rejected");
        Reject(() => Parse(thirtyTwo, BaseStyle + " body { decorator: image(extra.png); }"), "aggregate img and CSS count budget");

        using var fixture = new Fixture();
        fixture.Write();
        var source = BoundUiAuthoring.ValidateAsset(fixture.Assets, Fixture.LogicalPath, Targets);
        Check(source.Images.Count == 4, "nested RML-relative image and stylesheet resources read exactly once");
        Check(source.Images.Single(i => i.Path == "images/alpha.png").Asset.Info.Width == 8 &&
            source.Images.Single(i => i.Path == "images/background.jpg").Asset.Info.Height == 8 &&
            source.Images.Single(i => i.Path == "images/nested/icon.bmp").Asset.Info.Width == 8, "PNG JPEG BMP metadata preflight");
        byte[] alphaSnapshot = source.Images.Single(i => i.Path == "images/alpha.png").Asset.Bytes.ToArray();
        fixture.Put("images/alpha.png", Png(8, 8, (_, _) => new(255, 0, 255, 255)));
        File.Delete(fixture.PathFor("images/background.jpg"));
        Check(source.Images.Single(i => i.Path == "images/alpha.png").Asset.Bytes.SequenceEqual(alphaSnapshot), "owned snapshot survives original replacement");
        string stagePath;
        using (var stage = UiSourceStaging.Create(source))
        {
            stagePath = stage.DirectoryPath;
            Check(File.ReadAllText(stage.DocumentPath) == source.Rml && File.ReadAllText(Path.Combine(stagePath, source.Stylesheet)) == source.Rcss,
                "stage preserves exact validated markup and style");
            Check(File.ReadAllBytes(Path.Combine(stagePath, "images/alpha.png")).SequenceEqual(alphaSnapshot), "stage uses encoded snapshot, not replaced original");
            Check(File.ReadAllBytes(Path.Combine(stagePath, "images/background.jpg")).SequenceEqual(JpegBytes()), "stage includes deleted JPEG source");
            Check(File.Exists(Path.Combine(stagePath, "images/nested/icon.bmp")), "stage creates nested image directories");
        }
        Check(!Directory.Exists(stagePath), "standalone stage disposal removes complete resource tree");
        fixture.Write();
        File.Delete(fixture.PathFor("images/alpha.png"));
        var missing = Reject(() => BoundUiAuthoring.ValidateAsset(fixture.Assets, Fixture.LogicalPath, Targets), "missing referenced image");
        Check(missing.Code == "UI_RESOURCE" && missing.Field.Contains("src", StringComparison.Ordinal) && missing.Message.Contains("alpha.png", StringComparison.Ordinal),
            "missing image diagnostic contains reference and source field");
        fixture.Put("images/alpha.png", [1, 2, 3]);
        Reject(() => BoundUiAuthoring.ValidateAsset(fixture.Assets, Fixture.LogicalPath, Targets), "truncated image header");
        fixture.Put("images/alpha.png", HeaderOnlyPng(4097, 1));
        Reject(() => BoundUiAuthoring.ValidateAsset(fixture.Assets, Fixture.LogicalPath, Targets), "decoded dimension limit");
        fixture.Put("images/alpha.png", HeaderOnlyPng(4096, 4096));
        Reject(() => BoundUiAuthoring.ValidateAsset(fixture.Assets, Fixture.LogicalPath, Targets), "aggregate decoded RGBA budget");
        using (var file = new FileStream(fixture.PathFor("images/alpha.png"), FileMode.Create)) file.SetLength(16 * 1024 * 1024 + 1);
        Reject(() => BoundUiAuthoring.ValidateAsset(fixture.Assets, Fixture.LogicalPath, Targets), "per-file encoded byte limit");
        fixture.Write();
        fixture.WriteSources("<img src=\"images/first.bmp\"/><img src=\"images/second.bmp\"/>", BaseStyle);
        foreach (string name in new[] { "first.bmp", "second.bmp" })
        {
            fixture.Put("images/" + name, Bmp(8, 8, new(255, 255, 0, 255)));
            using var file = new FileStream(fixture.PathFor("images/" + name), FileMode.Open, FileAccess.Write);
            file.SetLength(9 * 1024 * 1024);
        }
        Reject(() => BoundUiAuthoring.ValidateAsset(fixture.Assets, Fixture.LogicalPath, Targets), "aggregate encoded byte limit");
        if (!OperatingSystem.IsWindows())
        {
            fixture.Write();
            string link = fixture.PathFor("images/link.png");
            File.CreateSymbolicLink(link, fixture.PathFor("images/alpha.png"));
            fixture.WriteSources("<img src=\"images/link.png\"/>", BaseStyle);
            Reject(() => BoundUiAuthoring.ValidateAsset(fixture.Assets, Fixture.LogicalPath, Targets), "linked image path rejected beneath asset root");
        }
        // Existing adapters must not silently inherit the new bound-only resource surface.
        var defaults = new AssetRoot();
        var settings = UiAuthoring.ValidateAsset(defaults, "ui/settings.rml");
        Reject(() => UiAuthoring.Validate(Encoding.UTF8.GetBytes(settings.Rml.Replace("</body>", "<img src=\"a.png\"/></body>", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(settings.Rcss)), "settings profile still rejects img");
        Reject(() => UiAuthoring.Validate(Encoding.UTF8.GetBytes(settings.Rml), Encoding.UTF8.GetBytes(settings.Rcss + " body { decorator: image(a.png); }")),
            "settings profile still rejects image decorator");
        var game = GameUiAuthoring.ValidateAsset(defaults, "ui/game.rml");
        Reject(() => GameUiAuthoring.Validate(Encoding.UTF8.GetBytes(game.Rml.Replace("</body>", "<img src=\"a.png\"/></body>", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(game.Rcss)), "game profile still rejects img");
        Reject(() => GameUiAuthoring.Validate(Encoding.UTF8.GetBytes(game.Rml), Encoding.UTF8.GetBytes(game.Rcss + " body { decorator: image(a.png); }")),
            "game profile still rejects image decorator");
        Console.WriteLine($"UI IMAGE CONTRACT PASS assertions={count}");
        return count;
    }

    public static int RunGraphics()
    {
        int count = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("UI IMAGE GRAPHICS: " + why); count++; }
        void Reject<T>(Action action, string why) where T : Exception
        {
            try { action(); }
            catch (T) { count++; return; }
            throw new Exception("UI IMAGE GRAPHICS accepted: " + why);
        }
        HashSet<string> beforeStages = StageDirectories();
        string captures = Path.GetFullPath(Environment.GetEnvironmentVariable("GAL_UI_IMAGE_CAPTURE_DIR") ?? "evidence/ui-images/jit");
        Directory.CreateDirectory(captures);
        using var fixture = new Fixture();
        fixture.Write();
        using var engine = new EngineHost(false, 16, legacyTone: false);
        using var ui = new BoundUiSession<string>(engine, new UiBindings<string>().Text("heading", value => value).Action("go", 7));
        void Render() => engine.Draw(new Camera { Zoom = 1 }, ReadOnlySpan<Sprite>.Empty);
        BitmapPixels Capture(string name)
        {
            string path = Path.Combine(captures, name + ".bmp");
            ui.Capture(path); Render();
            return BitmapPixels.Read(path);
        }
        int Stages() => StageDirectories().Count(path => !beforeStages.Contains(path));
        void Pixel(BitmapPixels bitmap, int x, int y, Rgba expected, string why, int tolerance = 0) =>
            Check(bitmap.Near(x, y, expected, tolerance), why + $" at ({x},{y}), actual {bitmap.At(x, y)} expected {expected}");
        fixture.Put("images/hidden.png", HeaderOnlyPng(8, 8));
        Reject<InvalidOperationException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "first open with invalid hidden image");
        Check(Stages() == 0 && engine.TextureCount == 0, "failed first open releases candidate snapshot and permits retry on same owner");
        fixture.Write();
        ui.LoadAsset(fixture.Assets, Fixture.LogicalPath);
        Check(ui.Status.Pending && !ui.Status.Loaded && Stages() == 1, "successful open owns pending encoded resource tree");
        Reject<InvalidOperationException>(() => ui.Apply("too early"), "pending image document cannot receive a model");
        // Delete and replace originals before the first draw, including both RML and RCSS.
        fixture.Put("images/alpha.png", Png(8, 8, (_, _) => new(255, 0, 255, 255)));
        File.Delete(fixture.PathFor("images/background.jpg"));
        File.Delete(fixture.PathFor("images/hidden.png"));
        File.WriteAllText(fixture.PathFor("images.rcss"), "invalid style");
        File.WriteAllText(fixture.PathFor("images.rml"), "invalid markup");
        Render();
        Check(ui.Status.Loaded && !ui.Status.Pending && Stages() == 1, "first publication keeps immutable snapshot after original mutation/deletion");
        Check(ui.Apply("Published model") && ui.Revision == 1, "first image document accepts model revision");
        Render();
        var original = Capture("images");
        Check(original.Width == 960 && original.Height == 540, "real framebuffer readback dimensions");
        Pixel(original, 17, 17, new(255, 0, 0, 255), "natural PNG opaque quadrant");
        Pixel(original, 22, 17, new(8, 144, 24, 255), "natural PNG straight alpha composite", 1);
        Pixel(original, 17, 22, new(16, 32, 48, 255), "fully transparent PNG preserves background");
        Pixel(original, 22, 22, new(0, 0, 255, 255), "natural PNG lower quadrant");
        Pixel(original, 24, 20, new(16, 32, 48, 255), "natural image stops at intrinsic eight-pixel width");
        Pixel(original, 52, 20, new(255, 0, 0, 255), "explicitly scaled PNG opaque quadrant");
        Pixel(original, 76, 20, new(8, 144, 24, 255), "explicitly scaled PNG alpha", 1);
        Pixel(original, 52, 44, new(16, 32, 48, 255), "scaled transparent quadrant");
        Pixel(original, 76, 44, new(0, 0, 255, 255), "explicitly scaled PNG lower quadrant");
        Pixel(original, 80, 28, new(16, 32, 48, 255), "explicit image width boundary");
        Pixel(original, 128, 32, new(32, 192, 96, 255), "JPEG decorator rendered", 2);
        Pixel(original, 184, 24, new(255, 255, 0, 255), "nested BMP source rendered");
        Pixel(original, 228, 20, new(255, 0, 0, 255), "PNG image decorator opaque quadrant");
        Pixel(original, 252, 20, new(8, 144, 24, 255), "PNG image decorator alpha", 1);
        Check(engine.Textures.Count == 0 && engine.TextureCount == 0, "UI images do not populate managed or native world texture cache");
        int resident = ui.Probe(11, 0).Number;
        Check(resident == 4, "native preloads all four unique image paths including hidden image");
        uint generation = ui.Status.Generation, revision = ui.Revision;
        ui.Probe(1, 1); var oldAction = ui.Poll();
        Check(oldAction.ActionId == 7 && ui.IsCurrent(oldAction), "current copied action before reload failures");
        void Retained(string name)
        {
            Check(ui.Status.Generation == generation && ui.Revision == revision && ui.Status.Loaded && !ui.Status.Pending &&
                ui.Probe(5, 0).Text == "Published model" && ui.IsCurrent(oldAction), name + " preserves model revision generation and copied action");
            Check(Capture(name).Rgb.AsSpan().SequenceEqual(original.Rgb), name + " preserves actual rendered pixels");
            Check(Stages() == 1 && ui.Probe(11, 0).Number == resident, name + " releases candidate resources without retiring live resources");
        }
        fixture.Write();
        File.WriteAllText(fixture.PathFor("images.rml"), "<rml>");
        Reject<UiAuthoringException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "malformed RML reload");
        Retained("failed-markup");
        fixture.Write(); File.Delete(fixture.PathFor("images/alpha.png"));
        Reject<UiAuthoringException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "missing image reload");
        Retained("failed-missing");
        fixture.Write(); fixture.Put("images/alpha.png", HeaderOnlyPng(4097, 1));
        Reject<UiAuthoringException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "oversized image reload");
        Retained("failed-oversize");
        fixture.Write(); fixture.Put("images/hidden.png", HeaderOnlyPng(8, 8));
        Check(BoundUiAuthoring.ValidateAsset(fixture.Assets, Fixture.LogicalPath, Targets).Images.Count == 4,
            "malformed compressed hidden image passes header-only managed preflight");
        Reject<InvalidOperationException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "hidden image decoded before successful native open");
        Retained("failed-hidden");
        fixture.Write(); fixture.Put("images/hover.png", HeaderOnlyPng(8, 8));
        fixture.WriteSources(Fixture.Content, Fixture.Style + " #go:hover { decorator: image(images/hover.png); }");
        Reject<InvalidOperationException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "hover-only decorator decoded before successful native open");
        Retained("failed-hover");

        // A failure before native open preserves an older pending candidate. A failure
        // during native staging may discard it, but must leave the live document intact.
        fixture.Write(); ui.LoadAsset(fixture.Assets, Fixture.LogicalPath);
        Check(ui.Status.Pending && Stages() == 2, "pending reload retains both published and candidate snapshots");
        File.WriteAllText(fixture.PathFor("images.rml"), "<rml>");
        Reject<UiAuthoringException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "managed rejected pending replacement");
        Check(ui.Status.Pending && Stages() == 2 && ui.Status.Generation == generation, "managed validation failure preserves prior pending candidate");
        fixture.Write(); fixture.Put("images/hidden.png", HeaderOnlyPng(8, 8));
        Reject<InvalidOperationException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "native rejected pending replacement");
        Retained("failed-pending");

        fixture.Write(); fixture.Put("images/alpha.png", Png(8, 8, (_, _) => new(255, 0, 255, 255)));
        ui.LoadAsset(fixture.Assets, Fixture.LogicalPath);
        fixture.Write(); ui.LoadAsset(fixture.Assets, Fixture.LogicalPath);
        Check(ui.Status.Pending && Stages() == 2, "new pending candidate replaces and releases older candidate");
        Render();
        Check(ui.Status.Generation != generation && ui.Revision == 0 && !ui.IsCurrent(oldAction) && Stages() == 1,
            "successful replacement publishes generation and releases retired snapshot");
        Check(ui.Apply("Published model"), "replacement reapplies model at new generation");
        Check(Capture("pending-replaced").Rgb.AsSpan().SequenceEqual(original.Rgb), "only newest successful pending candidate is rendered");
        for (int i = 0; i < 12; i++)
        {
            fixture.Write();
            bool purple = i % 2 == 0;
            if (purple) fixture.Put("images/alpha.png", Png(8, 8, (_, _) => new(255, 0, 255, 255)));
            uint previous = ui.Status.Generation;
            ui.LoadAsset(fixture.Assets, Fixture.LogicalPath); Render();
            Check(ui.Status.Generation != previous && ui.Revision == 0 && Stages() == 1, "repeated reload advances generation and retires stage " + i);
            ui.Apply("Published model");
            var frame = Capture("reload-" + i);
            Pixel(frame, 17, 17, purple ? new(255, 0, 255, 255) : new(255, 0, 0, 255), "repeated reload changes decoded pixels " + i);
            Check(ui.Probe(11, 0).Number == resident && engine.TextureCount == 0, "repeated reload bounds native image residency " + i);
        }
        ui.Dispose();
        Check(Stages() == 0, "session close removes current snapshot");
        fixture.Write();
        using (var png = engine.Textures.Acquire(fixture.Assets, "ui/screens/images/alpha.png"))
        using (var jpeg = engine.Textures.Acquire(fixture.Assets, "ui/screens/images/background.jpg"))
        {
            Check(engine.Textures.Count == 2 && engine.TextureCount == 2 && png.Info.Width == 8 && jpeg.Info.Height == 8,
                "world cache separately uploads actual PNG and JPEG textures");
            BitmapPixels WorldCapture(string name, ulong pngHandle)
            {
                SpriteDrawV2 Image(ulong texture, float x) => SpriteDrawV2.Create(new()
                    { M11 = 1, M22 = 1, X = x, Y = 16, Width = 32, Height = 32, R = 1, G = 1, B = 1, A = 1, Texture = texture });
                MaterialDraw[] images = [MaterialDraw.Create(Image(pngHandle, 16)), MaterialDraw.Create(Image(jpeg.Handle, 64))];
                string path = Path.Combine(captures, name + ".bmp");
                Native.Check(UiNative.Capture(engine.NativeContext, path), "world image capture");
                engine.RenderFrame(new[] { RenderPass.Create(0, new Camera { Zoom = 1 }, 0, 2, new(16f / 255, 32f / 255, 48f / 255, 1)) }, images);
                return BitmapPixels.Read(path);
            }
            var world = WorldCapture("world-images", png.Handle);
            Pixel(world, 20, 20, new(255, 0, 0, 255), "world PNG opaque quadrant");
            Pixel(world, 44, 20, new(8, 144, 24, 255), "world PNG alpha over explicit clear", 1);
            Pixel(world, 20, 44, new(16, 32, 48, 255), "world PNG zero alpha preserves clear");
            Pixel(world, 44, 44, new(0, 0, 255, 255), "world PNG lower quadrant");
            Pixel(world, 80, 32, new(32, 192, 96, 255), "world JPEG decoded upload", 2);
            fixture.Put("images/alpha.png", Png(8, 8, (_, _) => new(255, 0, 255, 255)));
            using (var duplicate = engine.Textures.Acquire(fixture.Assets, "ui/screens/images/alpha.png"))
            {
                Check(duplicate.Handle == png.Handle && engine.Textures.Count == 2, "live world lease shares retained image snapshot");
                Check(WorldCapture("world-retained", duplicate.Handle).Rgb.AsSpan().SequenceEqual(world.Rgb), "world lease retains pixels after source replacement");
            }
            png.Dispose();
            using var replacement = engine.Textures.Acquire(fixture.Assets, "ui/screens/images/alpha.png");
            Pixel(WorldCapture("world-reloaded", replacement.Handle), 20, 20, new(255, 0, 255, 255), "world final-lease release permits changed PNG reload");
            fixture.Put("images/invalid.png", HeaderOnlyPng(8, 8));
            Reject<AssetException>(() => engine.Textures.Acquire(fixture.Assets, "ui/screens/images/invalid.png"), "world native decode rejects malformed PNG");
            Check(engine.Textures.Count == 2 && engine.TextureCount == 2, "failed world upload preserves established cache entries");
        }
        Check(engine.Textures.Count == 0 && engine.TextureCount == 0, "world image leases release independently of UI resources");
        using var late = new BoundUiSession<string>(engine, new UiBindings<string>().Text("heading", value => value).Action("go", 7));
        fixture.Write(); late.LoadAsset(fixture.Assets, Fixture.LogicalPath); Render(); late.Apply("late");
        late.LoadAsset(fixture.Assets, Fixture.LogicalPath);
        Check(Stages() == 2, "engine-first cleanup begins with current and pending snapshots");
        engine.Dispose(); late.Dispose();
        Check(late.IsDisposed && Stages() == 0, "engine-first cleanup retires live and pending encoded snapshots");
        Console.WriteLine($"UI IMAGE GRAPHICS PASS assertions={count}; PNG alpha, JPEG/BMP, GPU readback and transactional reload pixels checked; captures={captures}");
        return count;
    }

    private static HashSet<string> StageDirectories() => Directory.EnumerateDirectories(Path.GetTempPath(), "gal-bound-ui-*").ToHashSet(StringComparer.Ordinal);
    private readonly record struct Rgba(byte R, byte G, byte B, byte A);
    private static Rgba Quadrant(int x, int y) => y < 4 ? x < 4 ? new(255, 0, 0, 255) : new(0, 255, 0, 128) : x < 4 ? new(0, 0, 255, 0) : new(0, 0, 255, 255);

    private sealed class Fixture : IDisposable
    {
        internal const string LogicalPath = "ui/screens/images.rml";
        internal const string Content = "<img id=\"natural\" src=\"images/alpha.png\"/><img id=\"explicit\" src=\"images/alpha.png\" width=\"32\" height=\"32\"/>" +
            "<div id=\"jpeg\"></div><img id=\"bitmap\" src=\"images/nested/icon.bmp\" width=\"16\" height=\"16\"/><div id=\"decorated\"></div><img id=\"hidden\" src=\"images/hidden.png\"/>";
        internal const string Style = "body { margin: 0px; width: 100%; height: 100%; font-family: \"Noto Sans CJK SC\"; font-size: 16px; background-color: #102030; }" +
            " #heading { display: none; } #go { display: none; } #hidden { display: none; }" +
            " #natural { position: absolute; left: 16px; top: 16px; } #explicit { position: absolute; left: 48px; top: 16px; }" +
            " #jpeg { position: absolute; left: 112px; top: 16px; width: 32px; height: 32px; decorator: image(images/background.jpg); }" +
            " #bitmap { position: absolute; left: 176px; top: 16px; }" +
            " #decorated { position: absolute; left: 224px; top: 16px; width: 32px; height: 32px; decorator: image(images/alpha.png); }";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "gal-image-test-" + Guid.NewGuid().ToString("N"));
        internal AssetRoot Assets { get; }
        internal Fixture() { Assets = new(_root); Directory.CreateDirectory(Path.Combine(_root, "ui/screens/images/nested")); }
        internal string PathFor(string relative) => Path.Combine(_root, "ui/screens", relative.Replace('/', Path.DirectorySeparatorChar));
        internal void Put(string relative, byte[] data) { string path = PathFor(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, data); }
        internal void WriteSources(string content, string style)
        {
            File.WriteAllText(PathFor("images.rml"), Markup(content));
            File.WriteAllText(PathFor("images.rcss"), style);
        }
        internal void Write()
        {
            WriteSources(Content, Style);
            Put("images/alpha.png", Png(8, 8, Quadrant));
            Put("images/nested/icon.bmp", Bmp(8, 8, new(255, 255, 0, 255)));
            Put("images/background.jpg", JpegBytes());
            Put("images/hidden.png", Png(8, 8, (_, _) => new(0, 255, 255, 255)));
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }

    private static byte[] Png(int width, int height, Func<int, int, Rgba> pixel)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            byte[] row = new byte[checked(width * 4 + 1)];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var color = pixel(x, y); int offset = x * 4 + 1;
                    row[offset] = color.R; row[offset + 1] = color.G; row[offset + 2] = color.B; row[offset + 3] = color.A;
                }
                zlib.Write(row);
            }
        }
        return PngChunks(width, height, compressed.ToArray());
    }
    // Structurally complete metadata with intentionally invalid zlib payload. This is
    // useful for distinguishing managed header preflight from authoritative decoding.
    private static byte[] HeaderOnlyPng(int width, int height) => PngChunks(width, height, [0, 0, 0, 0]);
    private static byte[] PngChunks(int width, int height, byte[] compressed)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        Chunk("IHDR", header); Chunk("IDAT", compressed); Chunk("IEND", []);
        return output.ToArray();
        void Chunk(string type, byte[] data)
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, data.Length); output.Write(number);
            byte[] tag = Encoding.ASCII.GetBytes(type); output.Write(tag); output.Write(data);
            uint crc = uint.MaxValue;
            foreach (byte value in tag) Add(value);
            foreach (byte value in data) Add(value);
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
            void Add(byte value) { crc ^= value; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        }
    }
    private static byte[] Bmp(int width, int height, Rgba color)
    {
        int stride = (width * 3 + 3) & ~3; byte[] bytes = new byte[54 + stride * height];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 24); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), stride * height);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        { int p = 54 + y * stride + x * 3; bytes[p] = color.B; bytes[p + 1] = color.G; bytes[p + 2] = color.R; }
        return bytes;
    }

    // Original, uniform 8x8 RGB (32,192,96) fixture, generated offline for this test
    // with Pillow Image.new(...).save(format="JPEG", quality=100, subsampling=0).
    // The tests require no Pillow, SDL_image, network asset, or runtime JPEG encoder.
    private static byte[] JpegBytes() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/2wBDAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/wAARCAAIAAgDAREAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDqK/lc/wCb8//Z");

    private sealed record BitmapPixels(int Width, int Height, byte[] Rgb)
    {
        internal Rgba At(int x, int y) { int i = (y * Width + x) * 3; return new(Rgb[i], Rgb[i + 1], Rgb[i + 2], 255); }
        internal bool Near(int x, int y, Rgba expected, int tolerance)
        {
            var actual = At(x, y);
            return Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance && Math.Abs(actual.B - expected.B) <= tolerance;
        }
        internal static BitmapPixels Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 54 || bytes[0] != 'B' || bytes[1] != 'M') throw new Exception("Expected complete BMP capture: " + path);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10));
            int width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18));
            int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22));
            int height = Math.Abs(signedHeight), bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28));
            uint compression = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(30));
            if (width <= 0 || height <= 0 || bits is not (24 or 32) || compression is not (0 or 3)) throw new Exception("Unsupported capture BMP format: " + path);
            int stride = checked(((width * bits + 31) / 32) * 4), step = bits / 8;
            if (offset < 54 || (long)offset + (long)stride * height > bytes.Length) throw new Exception("Truncated capture BMP: " + path);
            byte[] rgb = new byte[checked(width * height * 3)];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int source = offset + (signedHeight > 0 ? height - 1 - y : y) * stride + x * step, target = (y * width + x) * 3;
                rgb[target] = bytes[source + 2]; rgb[target + 1] = bytes[source + 1]; rgb[target + 2] = bytes[source];
            }
            return new(width, height, rgb);
        }
    }
}
