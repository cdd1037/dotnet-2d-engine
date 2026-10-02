using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace GameAuthoringLab;

/// <summary>
/// Exercises the optional file-backed SVG profile against real GPU readback. The
/// fixtures deliberately distinguish premultiplied alpha, true SVG semantics and
/// retained cache ownership from merely accepting an SVG resource name.
/// </summary>
internal static unsafe class UiSvgTests
{
    private static readonly Rgb Background = new(16, 32, 48);

    public static int RunDisabled()
    {
        int count = 0;
        void Check(bool condition, string why)
        {
            if (!condition) throw new InvalidOperationException("UI SVG DISABLED: " + why);
            count++;
        }
        var beforeStages = StageDirectories();
        int Stages() => StageDirectories().Count(path => !beforeStages.Contains(path));
        string captures = Path.GetFullPath(Environment.GetEnvironmentVariable("GAL_UI_SVG_CAPTURE_DIR") ?? "evidence/ui-svg/disabled");
        Directory.CreateDirectory(captures);
        using var fixture = new Fixture();
        fixture.Write();
        fixture.WriteSources("<div id=\"panel\"></div>", Fixture.BaseStyle +
            " #panel { position: absolute; left: 16px; top: 16px; width: 32px; height: 32px; background-color: #ff8000; }");
        using var engine = new EngineHost(false, 16, legacyTone: false);
        using var ui = new BoundUiSession<string>(engine, Bindings());
        void Render() => engine.Draw(new Camera { Zoom = 1 }, ReadOnlySpan<Sprite>.Empty);
        BitmapPixels Capture(string name)
        {
            string path = Path.Combine(captures, name + ".bmp"); ui.Capture(path); Render(); return BitmapPixels.Read(path);
        }
        ui.LoadAsset(fixture.Assets, Fixture.LogicalPath); Render();
        Check(ui.Apply("SVG off model") && ui.Status.Loaded && !ui.Status.Pending, "ordinary bound document works in SVG-off build");
        var original = Capture("before-rejected-svg");
        Check(original.Near(24, 24, new(255, 128, 0), 0), "SVG-off baseline has actual bound UI pixels");
        uint generation = ui.Status.Generation, revision = ui.Revision;
        ui.Probe(1, 1); var oldAction = ui.Poll();
        Check(oldAction.ActionId == 7 && ui.IsCurrent(oldAction), "ordinary copied action is current before disabled feature request");
        fixture.Write();
        string rejection = "";
        try { ui.LoadAsset(fixture.Assets, Fixture.LogicalPath); }
        catch (InvalidOperationException error) { rejection = error.Message; }
        Check(rejection.Contains("SVG support is disabled", StringComparison.Ordinal) && rejection.Contains("GAL_ENABLE_SVG", StringComparison.Ordinal),
            "valid managed SVG profile receives explicit optional-native-feature diagnostic");
        Check(ui.Status.Loaded && !ui.Status.Pending && ui.Status.Generation == generation && ui.Revision == revision && Stages() == 1,
            "disabled SVG request preserves live generation/revision and cleans rejected stage");
        Check(ui.Probe(5, 0).Text == "SVG off model" && ui.IsCurrent(oldAction), "disabled SVG request preserves model and copied action");
        Check(Capture("after-rejected-svg").Rgb.AsSpan().SequenceEqual(original.Rgb), "disabled SVG request preserves exact live pixels");
        Check(ui.Apply("Still usable") && ui.Revision == revision + 1, "existing owner remains usable after feature rejection");

        // The optional switch must not remove the existing file-backed image
        // surface. A tiny generated BMP needs no encoder or system dependency.
        File.WriteAllBytes(fixture.PathFor("images/raster.bmp"), SolidBmp(new(64, 128, 255)));
        fixture.WriteSources("<img id=\"raster\" src=\"images/raster.bmp\" width=\"32\" height=\"32\"/>",
            Fixture.BaseStyle + " #raster { position: absolute; left: 16px; top: 16px; }");
        ui.LoadAsset(fixture.Assets, Fixture.LogicalPath); Render();
        Check(ui.Status.Generation != generation && ui.Revision == 0 && !ui.IsCurrent(oldAction), "raster-only reload succeeds after disabled SVG request");
        ui.Apply("Raster remains supported");
        Check(Capture("raster-retry").Near(24, 24, new(64, 128, 255), 0), "SVG-off raster fallback renders actual pixels");
        Check(ui.Probe(11, 0).Number == 1 && engine.TextureCount == 0 && Stages() == 1, "raster fallback retains document-scoped ownership");
        ui.Dispose();
        Check(Stages() == 0, "SVG-off owner releases source snapshot");
        Console.WriteLine($"UI SVG DISABLED PASS assertions={count}; explicit feature diagnostic, live model/action/pixels preserved and raster retry verified; captures={captures}");
        return count;
    }

    public static int RunGraphics()
    {
        int count = 0;
        void Check(bool condition, string why)
        {
            if (!condition) throw new InvalidOperationException("UI SVG GRAPHICS: " + why);
            count++;
        }
        void Reject<T>(Action action, string why) where T : Exception
        {
            try { action(); }
            catch (T) { count++; return; }
            throw new InvalidOperationException("UI SVG GRAPHICS accepted: " + why);
        }
        var beforeStages = StageDirectories();
        int Stages() => StageDirectories().Count(path => !beforeStages.Contains(path));
        string captures = Path.GetFullPath(Environment.GetEnvironmentVariable("GAL_UI_SVG_CAPTURE_DIR") ?? "evidence/ui-svg/jit");
        Directory.CreateDirectory(captures);
        using var fixture = new Fixture();
        fixture.Write();
        using var engine = new EngineHost(false, 16, legacyTone: false);
        using var ui = new BoundUiSession<string>(engine, Bindings());
        void Render() => engine.Draw(new Camera { Zoom = 1 }, ReadOnlySpan<Sprite>.Empty);
        BitmapPixels Capture(string name)
        {
            string path = Path.Combine(captures, name + ".bmp");
            ui.Capture(path); Render();
            return BitmapPixels.Read(path);
        }
        void Pixel(BitmapPixels bitmap, int x, int y, Rgb expected, string why, int tolerance = 0) =>
            Check(bitmap.Near(x, y, expected, tolerance), why + $" at ({x},{y}), actual {bitmap.At(x, y)} expected {expected}");
        void Density(double value)
        {
            // The ordinary typed probe uses integer Number. This private test ABI
            // intentionally sends a double to cover fractional software density.
            BoundAction action = new() { Size = (uint)sizeof(BoundAction), Number = value };
            Native.Check(BoundUiNative.Test(engine.NativeContext, 14, &action), "SVG density probe");
        }
        int OpenNative(params string[] paths)
        {
            BoundTarget* targets = stackalloc BoundTarget[2];
            targets[0] = new() { Size = (uint)sizeof(BoundTarget), Kind = (uint)UiBindingKind.Text };
            targets[1] = new() { Size = (uint)sizeof(BoundTarget), Kind = (uint)UiBindingKind.Action, Action = 7 };
            UiNative.Put(targets[0].Id, 48, "heading"); UiNative.Put(targets[1].Id, 48, "go");
            byte** nativePaths = stackalloc byte*[paths.Length];
            int allocated = 0;
            try
            {
                foreach (string path in paths) nativePaths[allocated++] = (byte*)Marshal.StringToCoTaskMemUTF8(path);
                return BoundUiNative.OpenImages(engine.NativeContext, fixture.PathFor("svg.rml"),
                    Environment.GetEnvironmentVariable("GAL_UI_FONT") ?? "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc", targets, 2,
                    nativePaths, (uint)paths.Length);
            }
            finally { for (int i = 0; i < allocated; i++) Marshal.FreeCoTaskMem((nint)nativePaths[i]); }
        }

        fixture.Put("images/hidden.svg", "<svg>");
        Reject<UiAuthoringException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "invalid hidden SVG before first open");
        Check(Stages() == 0 && engine.TextureCount == 0, "failed first open leaves no stage or world texture and permits same-owner retry");
        fixture.Write();
        ui.LoadAsset(fixture.Assets, Fixture.LogicalPath);
        Check(ui.Status.Pending && !ui.Status.Loaded && Stages() == 1, "accepted SVG document owns one pending immutable source tree");
        Reject<InvalidOperationException>(() => ui.Apply("too early"), "pending SVG document cannot receive a model");

        // Native rendering must use the accepted snapshots, including sources
        // which were not visible when the document was accepted.
        fixture.Put("images/alpha.svg", Solid("#ff00ff", 8, 8));
        File.Delete(fixture.PathFor("images/gradient.svg"));
        File.Delete(fixture.PathFor("images/hidden.svg"));
        fixture.Put("svg.rml", "invalid markup");
        fixture.Put("svg.rcss", "invalid stylesheet");
        Render();
        Check(ui.Status.Loaded && !ui.Status.Pending && Stages() == 1, "first draw publishes the source snapshot after original edits and deletion");
        Check(ui.Apply("Published SVG model") && ui.Revision == 1, "published SVG accepts first model revision");
        Render();
        var original = Capture("svg-features");
        Check(original.Width == 960 && original.Height == 540, "actual baseline framebuffer readback dimensions");
        Pixel(original, 17, 17, new(255, 0, 0), "intrinsic SVG opaque red quadrant");
        Pixel(original, 22, 17, new(8, 144, 24), "SVG half alpha is premultiplied exactly once", 1);
        Pixel(original, 17, 22, Background, "zero-alpha SVG leaves background unchanged");
        Pixel(original, 22, 22, new(0, 0, 255), "intrinsic SVG blue quadrant");
        Pixel(original, 24, 20, Background, "intrinsic SVG has eight-pixel width");
        Pixel(original, 52, 20, new(255, 0, 0), "explicit SVG width/height scales artwork");
        Pixel(original, 76, 20, new(8, 144, 24), "scaled SVG half alpha", 1);
        Pixel(original, 52, 44, Background, "scaled SVG transparent quadrant");
        Pixel(original, 76, 44, new(0, 0, 255), "scaled SVG lower quadrant");
        Pixel(original, 80, 28, Background, "explicit SVG width boundary");
        Pixel(original, 100, 20, new(255, 0, 0), "SVG decorator opaque quadrant");
        Pixel(original, 124, 20, new(8, 144, 24), "SVG decorator uses premultiplied alpha", 1);
        Pixel(original, 148, 20, new(255, 0, 0), "CSS dimensions override authored SVG width/height");
        Pixel(original, 184, 36, new(0, 0, 255), "non-square CSS dimensions reach bottom-right quadrant");
        Pixel(original, 192, 20, Background, "CSS SVG width stops at forty-eight pixels");
        Pixel(original, 152, 40, Background, "CSS SVG height stops at twenty-four pixels");
        Pixel(original, 224, 32, new(44, 40, 84), "SVG white half-alpha multiplied by image-color and opacity", 2);
        Pixel(original, 272, 32, new(44, 40, 84), "SVG decorator tint and opacity match element", 2);
        Pixel(original, 308, 32, new(219, 0, 36), "linear gradient red end", 3);
        Pixel(original, 332, 32, new(28, 0, 227), "linear gradient blue end", 3);
        Pixel(original, 360, 32, new(255, 255, 0), "clipPath preserves inside geometry");
        Pixel(original, 376, 32, Background, "clipPath removes outside geometry");
        Pixel(original, 408, 32, new(0, 255, 0), "luminance mask preserves white region");
        Pixel(original, 424, 32, Background, "luminance mask removes black region");
        Pixel(original, 460, 28, new(224, 128, 32), "internal use reference and translation render");
        Pixel(original, 450, 18, Background, "use translation leaves unpainted margin");
        Pixel(original, 512, 32, new(64, 160, 224), "SVG internal currentColor does not inherit RML text color");
        Pixel(original, 560, 20, Background, "non-square translated viewBox meet preserves top letterbox");
        Pixel(original, 560, 32, new(0, 255, 255), "non-square translated viewBox maps artwork");
        Pixel(original, 560, 44, Background, "viewBox meet preserves bottom letterbox");
        Pixel(original, 608, 20, new(0, 255, 255), "preserveAspectRatio none fills the viewport");
        Pixel(original, 648, 32, new(136, 16, 24), "group opacity composites once", 1);
        Pixel(original, 664, 32, new(136, 16, 24), "overlapping group children do not double-apply opacity", 1);
        Pixel(original, 688, 16, Background, "zero-size SVG is benign and paints no pixel");
        Pixel(original, 752, 32, new(28, 72, 68), "different tint geometry shares the same source-size raster", 2);
        Check(engine.Textures.Count == 0 && engine.TextureCount == 0, "SVG resources do not enter world texture caches");
        var residency = ui.Probe(12, 0);
        Check(residency.Number == 12 && residency.RowId == 45824,
            $"same source/size and differently tinted uses share retained raster variants; actual {residency.Number} variants/{residency.RowId} bytes");
        Check(ui.Probe(13, 0).Number == 11, "manifest preloads all eleven unique SVG sources, including hidden artwork");
        Check(ui.Probe(11, 0).Number == 0, "SVG generated rasters are distinct from raster-file image residency");
        uint generation = ui.Status.Generation, revision = ui.Revision;
        ui.Probe(1, 1); var oldAction = ui.Poll();
        Check(oldAction.ActionId == 7 && ui.IsCurrent(oldAction), "copied bound action is current before failed SVG reloads");
        void Retained(string name)
        {
            Check(ui.Status.Generation == generation && ui.Revision == revision && ui.Status.Loaded && !ui.Status.Pending &&
                ui.Probe(5, 0).Text == "Published SVG model" && ui.IsCurrent(oldAction), name + " preserves model revision generation and copied action");
            Check(Capture(name).Rgb.AsSpan().SequenceEqual(original.Rgb), name + " preserves actual live pixels");
            var current = ui.Probe(12, 0);
            Check(Stages() == 1 && current.Number == residency.Number && current.RowId == residency.RowId,
                name + " releases rejected candidate resources without retiring live SVGs");
        }
        void RejectCandidate(string why)
        {
            bool threw = false;
            try { ui.LoadAsset(fixture.Assets, Fixture.LogicalPath); Render(); }
            catch (InvalidOperationException) { threw = true; }
            // Late rendering errors are allowed to reject the staged document
            // through status while completing the frame with the older live UI.
            var status = ui.Status;
            Check(threw || !status.Pending && status.Generation == generation && status.Diagnostic.Length != 0, why);
        }
        fixture.Write(); fixture.Put("svg.rml", "<rml>");
        Reject<UiAuthoringException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "malformed RML reload");
        Retained("failed-markup");
        fixture.Write(); File.Delete(fixture.PathFor("images/alpha.svg"));
        Reject<UiAuthoringException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "missing SVG reload");
        Retained("failed-missing");
        fixture.Write(); fixture.Put("images/hidden.svg", "<svg>");
        Reject<UiAuthoringException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "malformed hidden SVG reload");
        Retained("failed-hidden");
        fixture.Write(); fixture.Put("images/hidden.svg", Svg("<image href=\"https://example.invalid/no.png\"/>", 8, 8));
        Reject<UiAuthoringException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "external image in hidden SVG");
        Retained("failed-external");
        fixture.Write(); fixture.Put("images/hover.svg", "<svg>");
        fixture.WriteSources(Fixture.Content, Fixture.Style + " #go:hover { decorator: svg(images/hover.svg); }");
        Reject<UiAuthoringException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "malformed hover-only decorator source");
        Retained("failed-hover");
        fixture.Write();
        fixture.Put("images/hidden.svg", new string(' ', 256 * 1024 + 1));
        Reject<UiAuthoringException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "per-source byte budget on hidden SVG");
        Retained("failed-source-budget");

        // Bypass managed authoring deliberately, as a direct native C ABI caller
        // could. Every manifest source is independently checked before LunaSVG,
        // even when its only element is display:none.
        var unsafeSources = new[]
        {
            "<!DOCTYPE svg [<!ENTITY external SYSTEM \"https://example.invalid/no.svg\">]>" + Solid("#ffffff", 8, 8),
            Svg("<image href=\"https://example.invalid/no.png\"/>", 8, 8),
            Svg("<rect width=\"8\" height=\"8\" fill=\"url(https://example.invalid/no.svg#paint)\"/>", 8, 8),
            Svg("<script>throw 1;</script>", 8, 8)
        };
        for (int i = 0; i < unsafeSources.Length; i++)
        {
            fixture.Write(); fixture.Put("images/hidden.svg", unsafeSources[i]);
            fixture.WriteSources("<svg id=\"hidden\" src=\"images/hidden.svg\"/>", Fixture.BaseStyle + " #hidden { display: none; }");
            Check(OpenNative("images/hidden.svg") != 0, "native independently rejects unsafe hidden SVG manifest source " + i);
            Retained("failed-native-hidden-" + i);
        }
        fixture.Write(); fixture.WriteSources("<svg src=\"images/alpha.svg\"/>", Fixture.BaseStyle);
        Check(OpenNative() != 0, "native plugin refuses an SVG source absent from the validated manifest");
        Retained("failed-native-unmanifested");

        // All 129 variants are on screen. The last is tiny, so rejection must be
        // the retained-variant bound rather than memory, dimensions or visibility.
        fixture.Write();
        fixture.WriteSources(string.Concat(Enumerable.Range(1, 129).Select(i =>
            "<svg class=\"variant\" src=\"images/alpha.svg\" width=\"" + i + "\" height=\"1\"/>")),
            Fixture.BaseStyle + " .variant { position: absolute; left: 16px; top: 16px; }");
        RejectCandidate("129 distinct retained raster variants are rejected");
        Retained("failed-variant-budget");

        fixture.Write();
        fixture.WriteSources("<svg class=\"variant\" src=\"images/alpha.svg\" width=\"4096\" height=\"4096\"/>" +
            "<svg class=\"variant\" src=\"images/white.svg\" width=\"1\" height=\"1\"/>",
            Fixture.BaseStyle + " .variant { position: absolute; left: 16px; top: 16px; }");
        RejectCandidate("64 MiB retained SVG raster budget plus another variant is rejected");
        Retained("failed-raster-byte-budget");

        fixture.Write();
        fixture.WriteSources("<svg id=\"limit\" src=\"images/alpha.svg\"/>",
            Fixture.BaseStyle + " #limit { position: absolute; left: 16px; top: 16px; width: 1920dp; height: 1dp; }");
        Density(3);
        RejectCandidate("resolved raster width above 4096 is rejected before allocation");
        Density(1); Render();
        Retained("failed-raster-dimension");

        // A managed rejection preserves an earlier pending candidate. Replacing
        // it with a good candidate retires exactly the superseded snapshot.
        fixture.Write(); ui.LoadAsset(fixture.Assets, Fixture.LogicalPath);
        Check(ui.Status.Pending && Stages() == 2, "pending reload retains published and candidate snapshots");
        fixture.Put("images/hidden.svg", "<svg>");
        Reject<UiAuthoringException>(() => ui.LoadAsset(fixture.Assets, Fixture.LogicalPath), "managed rejected pending replacement");
        Check(ui.Status.Pending && Stages() == 2 && ui.Status.Generation == generation, "managed failure preserves older accepted pending candidate");
        fixture.Write(); fixture.Put("images/alpha.svg", Solid("#ff00ff", 8, 8));
        ui.LoadAsset(fixture.Assets, Fixture.LogicalPath);
        Check(ui.Status.Pending && Stages() == 2, "new accepted pending candidate retires the previous candidate");
        fixture.Write(); ui.LoadAsset(fixture.Assets, Fixture.LogicalPath); Render();
        Check(ui.Status.Generation != generation && ui.Revision == 0 && !ui.IsCurrent(oldAction) && Stages() == 1,
            "successful replacement advances generation and retires old snapshot/action");
        ui.Apply("Published SVG model");
        Check(Capture("pending-replaced").Rgb.AsSpan().SequenceEqual(original.Rgb), "only newest accepted pending SVG candidate publishes");
        for (int i = 0; i < 8; i++)
        {
            fixture.Write(); bool purple = i % 2 == 0;
            if (purple) fixture.Put("images/alpha.svg", Solid("#ff00ff", 8, 8));
            uint previous = ui.Status.Generation;
            ui.LoadAsset(fixture.Assets, Fixture.LogicalPath); Render();
            Check(ui.Status.Generation != previous && ui.Revision == 0 && Stages() == 1, "reload advances generation and retires source tree " + i);
            ui.Apply("Published SVG model");
            Pixel(Capture("reload-" + i), 17, 17, purple ? new(255, 0, 255) : new(255, 0, 0), "reload reads changed SVG rather than stale path cache " + i);
            var current = ui.Probe(12, 0);
            Check(current.Number == residency.Number && current.RowId == residency.RowId && engine.TextureCount == 0,
                "repeated SVG reload bounds retained variants and bytes " + i);
        }

        // Density is a deterministic RmlUi context probe, not a claim that this
        // automated run moved a physical window between high-DPI monitors.
        fixture.Write(); fixture.Put("images/solid.svg", Solid("#2080e0", 8, 8));
        fixture.WriteSources("<svg id=\"density\" src=\"images/solid.svg\" width=\"8\" height=\"8\"/>",
            Fixture.BaseStyle + " #density { position: absolute; left: 16px; top: 16px; width: 24dp; height: 16dp; }");
        ui.LoadAsset(fixture.Assets, Fixture.LogicalPath); Render(); ui.Apply("Density");
        foreach (double ratio in new[] { 1.0, 1.5, 2.0, 1.5, 1.0 })
        {
            Density(ratio); Render();
            int width = (int)(24 * ratio), height = (int)(16 * ratio);
            var frame = Capture("density-" + ratio.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            Pixel(frame, 16 + width - 2, 16 + height - 2, new(32, 128, 224), "density scales SVG content box " + ratio);
            Pixel(frame, 16 + width, 20, Background, "density SVG right boundary " + ratio);
            Pixel(frame, 20, 16 + height, Background, "density SVG bottom boundary " + ratio);
            var current = ui.Probe(12, 0);
            Check(current.Number == 1 && current.RowId == (ulong)(width * height * 4), "density change releases previous raster size " + ratio);
        }
        Density(1);
        fixture.WriteSources("<svg id=\"fluid\" src=\"images/solid.svg\"/>",
            Fixture.BaseStyle + " #fluid { position: absolute; left: 16px; top: 16px; width: 50%; height: 24px; }");
        ui.LoadAsset(fixture.Assets, Fixture.LogicalPath); Render(); ui.Apply("Resize");
        foreach (var size in new[] { (640, 360), (384, 288), (960, 540) })
        {
            ui.Probe(15, 0, row: (ulong)size.Item2, number: size.Item1);
            var input = engine.PollInput(); Render();
            Check(input.Drawable && input.WindowWidth == size.Item1 && input.WindowHeight == size.Item2, "real SDL window resize is observed by input");
            var frame = Capture("resize-" + size.Item1);
            Check(frame.Width == input.PixelWidth && frame.Height == input.PixelHeight, "capture follows drawable framebuffer after resize");
            int width = frame.Width / 2;
            Pixel(frame, 16 + width - 2, 24, new(32, 128, 224), "percentage SVG rerasterizes after actual window resize");
            Pixel(frame, 16 + width, 24, Background, "percentage SVG right boundary after resize");
            var current = ui.Probe(12, 0);
            Check(current.Number == 1 && current.RowId == (ulong)(width * 24 * 4), "window resize releases retired SVG raster size");
        }

        // Managed staging has generation-unique paths. Exercise the public native
        // boundary with the exact same original path while the older document is
        // alive, so a source-path-only upstream cache would return stale artwork.
        fixture.WriteSources("<svg id=\"native\" src=\"images/native.svg\" width=\"32\" height=\"32\"/>",
            Fixture.BaseStyle + " #native { position: absolute; left: 16px; top: 16px; }");
        fixture.Put("images/native.svg", Solid("#ff0000", 8, 8));
        Check(OpenNative("images/native.svg") == 0 && ui.Status.Pending, "direct native caller stages an SVG using the original source pathname");
        fixture.Put("images/native.svg", Solid("#0000ff", 8, 8));
        Render(); ui.Apply("First direct native snapshot");
        Pixel(Capture("native-original-snapshot"), 24, 24, new(255, 0, 0), "native manifest snapshot survives replacement before first draw");
        uint directGeneration = ui.Status.Generation;
        ui.Probe(1, 1); var directAction = ui.Poll();
        Check(ui.IsCurrent(directAction), "direct native publication still supports managed bindings and copied actions");
        Check(OpenNative("images/native.svg") == 0 && ui.Status.Pending && ui.Status.Generation == directGeneration,
            "direct native replacement stages while same-path predecessor is still live");
        fixture.Put("images/native.svg", Solid("#00ff00", 8, 8));
        File.Delete(fixture.PathFor("svg.rml")); File.Delete(fixture.PathFor("svg.rcss"));
        Render();
        Check(ui.Status.Generation != directGeneration && ui.Revision == 0 && !ui.IsCurrent(directAction),
            "direct native replacement publishes a fresh generation and invalidates old action");
        ui.Apply("Second direct native snapshot");
        Pixel(Capture("native-same-path-reload"), 24, 24, new(0, 0, 255), "render-manager scoped SVG cache uses accepted replacement bytes at identical path");
        var directResidency = ui.Probe(12, 0);
        Check(directResidency.Number == 1 && directResidency.RowId == 4096 && ui.Probe(13, 0).Number == 1,
            "direct native same-path replacement retires the predecessor's cache and source ownership");

        ui.Dispose();
        Check(Stages() == 0 && engine.TextureCount == 0, "owner close removes SVG source snapshot without world cache residue");
        fixture.Write();
        using var late = new BoundUiSession<string>(engine, Bindings());
        late.LoadAsset(fixture.Assets, Fixture.LogicalPath); Render(); late.Apply("Late owner");
        Check(late.Probe(12, 0).Number == residency.Number, "new owner reloads complete SVG cache after prior owner disposal");
        late.LoadAsset(fixture.Assets, Fixture.LogicalPath);
        Check(Stages() == 2, "engine-first cleanup starts with both live and pending SVG snapshots");
        engine.Dispose(); late.Dispose();
        Check(late.IsDisposed && Stages() == 0, "engine-first disposal releases live and pending SVG source trees");
        Console.WriteLine($"UI SVG GRAPHICS PASS assertions={count}; alpha/tint, gradients/clip/mask/use/currentColor, viewBox, reload/cache and software density/SDL resize pixels checked; physical high-DPI hardware unverified; captures={captures}");
        return count;
    }

    private static UiBindings<string> Bindings() => new UiBindings<string>().Text("heading", value => value).Action("go", 7);
    private static HashSet<string> StageDirectories() => Directory.EnumerateDirectories(Path.GetTempPath(), "gal-bound-ui-*").ToHashSet(StringComparer.Ordinal);
    private static string Svg(string content, int width = 32, int height = 32, string? viewBox = null, string extra = "") =>
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"" + width + "\" height=\"" + height + "\" viewBox=\"" +
        (viewBox ?? "0 0 " + width + " " + height) + "\"" + extra + ">" + content + "</svg>";
    private static string Solid(string color, int width = 32, int height = 32) =>
        Svg("<rect width=\"" + width + "\" height=\"" + height + "\" fill=\"" + color + "\"/>", width, height);
    private static byte[] SolidBmp(Rgb color)
    {
        const int width = 8, height = 8, stride = 24;
        byte[] bytes = new byte[54 + stride * height]; bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), height);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 1); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), stride * height);
        for (int i = 54; i < bytes.Length; i += 3) { bytes[i] = color.B; bytes[i + 1] = color.G; bytes[i + 2] = color.R; }
        return bytes;
    }

    private sealed class Fixture : IDisposable
    {
        internal const string LogicalPath = "ui/screens/svg.rml";
        internal const string Content =
            "<svg id=\"natural\" src=\"images/alpha.svg\"/>" +
            "<svg id=\"explicit\" src=\"images/alpha.svg\" width=\"32\" height=\"32\"/>" +
            "<div id=\"decorated\"></div><svg id=\"css\" src=\"images/alpha.svg\" width=\"32\" height=\"32\"/>" +
            "<svg id=\"tint\" src=\"images/white.svg\" width=\"32\" height=\"32\"/><div id=\"tint-decorator\"></div>" +
            "<svg id=\"gradient\" src=\"images/gradient.svg\"/><svg id=\"clip\" src=\"images/clip.svg\"/>" +
            "<svg id=\"mask\" src=\"images/mask.svg\"/><svg id=\"use\" src=\"images/use.svg\"/>" +
            "<svg id=\"current\" src=\"images/current.svg\"/><svg id=\"wide\" src=\"images/wide.svg\"/>" +
            "<svg id=\"stretch\" src=\"images/stretch.svg\"/><svg id=\"group\" src=\"images/group.svg\"/>" +
            "<svg id=\"hidden\" src=\"images/hidden.svg\"/><svg id=\"zero\" src=\"images/white.svg\"/>" +
            "<svg id=\"tint-alt\" src=\"images/white.svg\" width=\"32\" height=\"32\"/>";
        internal const string BaseStyle =
            "body { margin: 0px; width: 100%; height: 100%; font-family: \"Noto Sans CJK SC\"; font-size: 16px; background-color: #102030; color: #ff00ff; }" +
            " #heading { display: none; } #go { display: none; }";
        internal const string Style = BaseStyle +
            " #hidden { display: none; } #natural { position: absolute; left: 16px; top: 16px; }" +
            " #explicit { position: absolute; left: 48px; top: 16px; }" +
            " #decorated { position: absolute; left: 96px; top: 16px; width: 32px; height: 32px; decorator: svg(images/alpha.svg); }" +
            " #css { position: absolute; left: 144px; top: 16px; width: 48px; height: 24px; }" +
            " #tint { position: absolute; left: 208px; top: 16px; image-color: #8040c0; opacity: 0.5; }" +
            " #tint-decorator { position: absolute; left: 256px; top: 16px; width: 32px; height: 32px; image-color: #8040c0; opacity: 0.5; decorator: svg(images/white.svg); }" +
            " #gradient { position: absolute; left: 304px; top: 16px; } #clip { position: absolute; left: 352px; top: 16px; }" +
            " #mask { position: absolute; left: 400px; top: 16px; } #use { position: absolute; left: 448px; top: 16px; }" +
            " #current { position: absolute; left: 496px; top: 16px; } #wide { position: absolute; left: 544px; top: 16px; }" +
            " #stretch { position: absolute; left: 592px; top: 16px; } #group { position: absolute; left: 640px; top: 16px; }" +
            " #zero { position: absolute; left: 688px; top: 16px; width: 0px; height: 0px; }" +
            " #tint-alt { position: absolute; left: 736px; top: 16px; image-color: #40c080; opacity: 0.5; }";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "gal-svg-test-" + Guid.NewGuid().ToString("N"));
        internal AssetRoot Assets { get; }
        internal Fixture() { Assets = new(_root); Directory.CreateDirectory(Path.Combine(_root, "ui/screens/images")); }
        internal string PathFor(string relative) => Path.Combine(_root, "ui/screens", relative.Replace('/', Path.DirectorySeparatorChar));
        internal void Put(string relative, string data)
        {
            string path = PathFor(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, data, new UTF8Encoding(false));
        }
        internal void WriteSources(string content, string style)
        {
            Put("svg.rml", "<rml><head><title>SVG fixture</title><link type=\"text/rcss\" href=\"svg.rcss\"/></head><body>" +
                "<p id=\"heading\">Initial</p><button id=\"go\">Go</button>" + content + "</body></rml>");
            Put("svg.rcss", style);
        }
        internal void Write()
        {
            WriteSources(Content, Style);
            Put("images/alpha.svg", Svg("<rect width=\"4\" height=\"4\" fill=\"#ff0000\"/>" +
                "<rect x=\"4\" width=\"4\" height=\"4\" fill=\"#00ff00\" fill-opacity=\"0.5\"/>" +
                "<rect y=\"4\" width=\"4\" height=\"4\" fill=\"#0000ff\" fill-opacity=\"0\"/>" +
                "<rect x=\"4\" y=\"4\" width=\"4\" height=\"4\" fill=\"#0000ff\"/>", 8, 8));
            Put("images/white.svg", Svg("<rect width=\"32\" height=\"32\" fill=\"#ffffff\" fill-opacity=\"0.5\"/>"));
            Put("images/gradient.svg", Svg("<defs><linearGradient id=\"ramp\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"0\" x2=\"32\" y2=\"0\">" +
                "<stop offset=\"0\" stop-color=\"#ff0000\"/><stop offset=\"1\" stop-color=\"#0000ff\"/></linearGradient></defs>" +
                "<rect width=\"32\" height=\"32\" fill=\"url(#ramp)\"/>"));
            Put("images/clip.svg", Svg("<defs><clipPath id=\"half\"><rect width=\"16\" height=\"32\"/></clipPath></defs>" +
                "<rect width=\"32\" height=\"32\" fill=\"#ffff00\" clip-path=\"url(#half)\"/>"));
            Put("images/mask.svg", Svg("<defs><mask id=\"half\" maskUnits=\"userSpaceOnUse\" x=\"0\" y=\"0\" width=\"32\" height=\"32\">" +
                "<rect width=\"16\" height=\"32\" fill=\"#ffffff\"/><rect x=\"16\" width=\"16\" height=\"32\" fill=\"#000000\"/></mask></defs>" +
                "<rect width=\"32\" height=\"32\" fill=\"#00ff00\" mask=\"url(#half)\"/>"));
            Put("images/use.svg", Svg("<defs><rect id=\"tile\" width=\"16\" height=\"16\" fill=\"#e08020\"/></defs><use href=\"#tile\" x=\"8\" y=\"8\"/>"));
            Put("images/current.svg", Svg("<g color=\"#40a0e0\"><rect width=\"32\" height=\"32\" fill=\"currentColor\"/></g>"));
            const string wide = "<rect x=\"10\" y=\"20\" width=\"16\" height=\"8\" fill=\"#00ffff\"/>";
            Put("images/wide.svg", Svg(wide, viewBox: "10 20 16 8"));
            Put("images/stretch.svg", Svg(wide, viewBox: "10 20 16 8", extra: " preserveAspectRatio=\"none\""));
            Put("images/group.svg", Svg("<g opacity=\"0.5\"><rect width=\"32\" height=\"32\" fill=\"#ff0000\"/>" +
                "<rect x=\"16\" width=\"16\" height=\"32\" fill=\"#ff0000\"/></g>"));
            Put("images/hidden.svg", Solid("#00ffff", 8, 8));
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }

    private readonly record struct Rgb(byte R, byte G, byte B);
    private sealed record BitmapPixels(int Width, int Height, byte[] Rgb)
    {
        internal Rgb At(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) throw new InvalidOperationException($"SVG sample ({x},{y}) outside {Width}x{Height} capture");
            int offset = (y * Width + x) * 3; return new(Rgb[offset], Rgb[offset + 1], Rgb[offset + 2]);
        }
        internal bool Near(int x, int y, Rgb expected, int tolerance)
        {
            var actual = At(x, y);
            return Math.Abs(actual.R - expected.R) <= tolerance && Math.Abs(actual.G - expected.G) <= tolerance && Math.Abs(actual.B - expected.B) <= tolerance;
        }
        internal static BitmapPixels Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 54 || bytes[0] != 'B' || bytes[1] != 'M') throw new InvalidOperationException("Expected complete BMP capture: " + path);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10));
            int width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18));
            int signedHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22));
            int height = Math.Abs(signedHeight), bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28));
            uint compression = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(30));
            if (width <= 0 || height <= 0 || bits is not (24 or 32) || compression is not (0 or 3)) throw new InvalidOperationException("Unsupported capture BMP format: " + path);
            int stride = checked(((width * bits + 31) / 32) * 4), step = bits / 8;
            if (offset < 54 || (long)offset + (long)stride * height > bytes.Length) throw new InvalidOperationException("Truncated capture BMP: " + path);
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
