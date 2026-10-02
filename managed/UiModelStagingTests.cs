using System.Buffers.Binary;
namespace GameAuthoringLab;

// These tests exercise the handwritten schema path; the package consumer checks generated contracts.
internal static unsafe class UiModelStagingTests
{
    private sealed class Model { public string Title = "Live"; public List<string> Rows = ["one"]; }
    private static UiRecord<Model> Schema() => new UiRecord<Model>().Text("title", m => m.Title).Array("rows", m => m.Rows, UiData.Text, 4);
    private const string Rml = "<rml><head><title>Staged model</title><link type='text/rcss' href='stage.rcss'/></head><body data-model='model'><p id='title'>{{state.title}}</p><div data-for='row : state.rows'>{{row}}</div><img src='pixel.bmp'/><button id='act' data-event-click='act'>Act</button></body></rml>";
    private const string Css = "body { font-family: Noto Sans CJK SC; font-size: 18px; color: white; background-color: #123456; } button { width: 100px; height: 40px; } img { width: 24px; height: 24px; }";
    internal static int RunNative(EngineHost engine)
    {
        int assertions = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("UI STAGING: " + label); assertions++; }
        void Reject<E>(Action action, string label) where E : Exception
        { try { action(); } catch (E) { assertions++; return; } throw new Exception("UI STAGING accepted: " + label); }
        var previous = Directory.EnumerateDirectories(Path.GetTempPath(), "gal-bound-ui-*").ToHashSet();
        int Stages() => Directory.EnumerateDirectories(Path.GetTempPath(), "gal-bound-ui-*").Count(p => !previous.Contains(p));
        string root = Path.Combine(Path.GetTempPath(), "gal-staging-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        void Source(string css = Css) { File.WriteAllText(Path.Combine(root, "stage.rml"), Rml); File.WriteAllText(Path.Combine(root, "stage.rcss"), css); File.WriteAllBytes(Path.Combine(root, "pixel.bmp"), Pixel()); }
        void Draw() => engine.Draw(new Camera { Zoom = 1 }, ReadOnlySpan<Sprite>.Empty);
        try
        {
            Source(); var assets = new AssetRoot(root); var model = new Model(); int commands = 0;
            double RendererProbe(UiModelSession<Model> ui, uint command)
            {
                var status = ui.Status;
                ModelEvent packet = new() { Size = (uint)sizeof(ModelEvent), Generation = status.Generation, Revision = status.Revision };
                Native.Check(UiModelNative.Test(engine.NativeContext, command, "title", 0, &packet), "staging renderer probe");
                return packet.Arguments[0].Number;
            }
            using (var ui = new UiModelSession<Model>(engine, Schema(), new UiCommands().On("act", () => commands++)))
            {
                model.Title = "bad\ud800";
                Reject<UiAuthoringException>(() => ui.StageAsset(assets, "stage.rml", model), "invalid first model before native ownership");
                Check(Stages() == 0, "failed first projection leaves no staging resources");
                model.Title = "Live";
                File.WriteAllBytes(Path.Combine(root, "pixel.bmp"), Pixel()[..54]);
                Reject<InvalidOperationException>(() => ui.StageAsset(assets, "stage.rml", model), "first native image failure");
                Check(Stages() == 0, "failed first native open releases source ownership");
                Source(); ui.StageAsset(assets, "stage.rml", model);
                RendererProbe(ui, 14); Draw();
                Check(ui.Status is { Loaded: false, Pending: false, Revision: 0 } && ui.Status.Diagnostic.Length != 0 && Stages() == 0,
                    "failed first candidate render leaves session retryable and releases resources");
                ui.StageAsset(assets, "stage.rml", model);
                Check(ui.Status is { Loaded: false, Pending: true, Revision: 0 } && Stages() == 1, "stage does not draw or publish");
                Reject<InvalidOperationException>(() => ui.Apply(model), "apply while first candidate pending");
                model.Title = "Mutated after staging"; model.Rows.Clear();
                Draw();
                Check(ui.Probe(5, "title") == "Live" && ui.Status is { Loaded: true, Pending: false, Revision: 1 }, "first frame uses copied initial model");
                model.Title = "Live"; model.Rows.Add("one");
                Check(!ui.Apply(model) && ui.NativeApplyCalls == 0, "published initial snapshot is the unchanged baseline");
                ui.Probe(1, "act"); var old = ui.Poll(); ui.Probe(1, "act");
                var live = ui.Status;
                byte[] Capture(string name) { string path = Path.Combine(root, name + ".bmp"); ui.Capture(path); Draw(); return File.ReadAllBytes(path); }
                byte[] original = Capture("live");
                Check(RendererProbe(ui, 13) == 1, "live renderer owns one image");
                void Retained(string reason)
                {
                    Check(ui.Status.Generation == live.Generation && ui.Revision == live.Revision && ui.Status.Loaded && !ui.Status.Pending, reason + " retains live identity");
                    Check(ui.IsCurrent(old) && ui.Dispatch(old), reason + " retains copied commands");
                    Check(Capture(reason).AsSpan().SequenceEqual(original), reason + " retains rendered pixels");
                    Check(Stages() == 1 && RendererProbe(ui, 13) == 1, reason + " releases candidate source and renderer resources");
                }
                model.Title = "bad\ud800";
                Reject<UiAuthoringException>(() => ui.StageAsset(assets, "stage.rml", model), "invalid replacement projection");
                Retained("projection"); Check(!ui.Poll().IsEmpty, "failed projection retains queued command");
                model.Title = "Replacement";
                File.WriteAllText(Path.Combine(root, "stage.rml"), "<rml>");
                Reject<UiAuthoringException>(() => ui.StageAsset(assets, "stage.rml", model), "invalid replacement source"); Retained("source");
                Source(); File.WriteAllBytes(Path.Combine(root, "pixel.bmp"), Pixel()[..54]);
                Reject<InvalidOperationException>(() => ui.StageAsset(assets, "stage.rml", model), "renderer rejects truncated image after metadata preflight"); Retained("resource");
                Source();
                ui.StageAsset(assets, "stage.rml", model);
                Check(ui.Status.Pending, "renderer-warning candidate remains isolated until draw");
                ui.Probe(1, "act"); RendererProbe(ui, 14);
                Draw();
                Check(ui.Status.Diagnostic.Length != 0, "deferred renderer diagnostic is observable");
                Retained("renderer"); Check(!ui.Poll().IsEmpty, "failed candidate render retains queued live command");
                Source(); ui.StageAsset(assets, "stage.rml", model);
                Check(ui.Status.Pending && ui.Status.Loaded && ui.IsCurrent(old) && Stages() == 2, "old model stays active while replacement pending");
                ui.Probe(1, "act"); var pendingCommand = ui.Poll();
                Check(ui.Dispatch(pendingCommand) && ui.Status.Pending, "pending candidate does not intercept live commands");
                Reject<InvalidOperationException>(() => ui.Apply(model), "apply rejects ambiguous pending generation");
                model.Title = "bad\ud800";
                Reject<UiAuthoringException>(() => ui.StageAsset(assets, "stage.rml", model), "bad later candidate preserves accepted pending candidate");
                Check(ui.Status.Pending && Stages() == 2, "managed failure preserves prior candidate");
                model.Title = "Replacement"; File.WriteAllBytes(Path.Combine(root, "pixel.bmp"), Pixel()[..54]);
                Reject<InvalidOperationException>(() => ui.StageAsset(assets, "stage.rml", model), "native failure drops prior pending candidate");
                Retained("pending-native"); Source();
                model.Title = "Superseded"; ui.StageAsset(assets, "stage.rml", model);
                model.Title = "Newest"; model.Rows.Clear(); ui.StageAsset(assets, "stage.rml", model);
                Check(Stages() == 2 && RendererProbe(ui, 13) == 2, "newest stage replaces earlier pending resources"); Draw();
                Check(ui.Probe(5, "title") == "Newest" && ui.Status.Generation != live.Generation && ui.Revision == 1 && Stages() == 1, "one draw publishes newest empty-array model");
                Check(!ui.Dispatch(old) && !ui.IsCurrent(old) && ui.Poll().IsEmpty, "successful replacement rejects old generation and queue");
                Check(!ui.Apply(model), "empty initial snapshot is unchanged baseline");
                for (int i = 0; i < 100; i++) ui.Apply(model);
                long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 1000; i++) ui.Apply(model);
                Check(GC.GetAllocatedBytesForCurrentThread() == before && ui.NativeApplyCalls == 0, "warm staged baseline avoids allocations and native applies");
                for (int i = 0; i < 8; i++)
                {
                    ui.StageAsset(assets, "stage.rml", model); Draw();
                    Check(ui.Revision == 1 && Stages() == 1 && RendererProbe(ui, 13) == 1, "repeated publication bounds renderer residency " + i);
                }
                ui.StageAsset(assets, "stage.rml", model); ui.Dispose();
                Check(Stages() == 0, "dispose clears both current and pending source ownership");
                Reject<ObjectDisposedException>(() => ui.StageAsset(assets, "stage.rml", model), "stage after disposal");
            }
            using (var unrendered = new UiModelSession<Model>(engine, Schema()))
            {
                File.WriteAllText(Path.Combine(root, "stage.rml"), Rml.Replace("data-event-click='act'", ""));
                unrendered.StageAsset(assets, "stage.rml", new Model()); unrendered.Dispose();
                Check(Stages() == 0, "dispose before first draw releases candidate");
            }
            UiModelSession<Model>? reentrant = null;
            using (reentrant = new UiModelSession<Model>(engine, new UiRecord<Model>().Text("title", m => { reentrant!.Dispose(); return m.Title; })))
            { Reject<InvalidOperationException>(() => reentrant.StageAsset(assets, "stage.rml", model), "staged projection cannot re-enter disposal"); Check(!reentrant.IsDisposed, "reentry leaves owner alive"); }
            Check(Stages() == 0, "all stage lifetimes balanced");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"UI STAGING NATIVE PASS assertions={assertions}"); return assertions;
    }
    private static byte[] Pixel()
    {
        var data = new byte[58]; data[0] = (byte)'B'; data[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), data.Length); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 40); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), 1); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(34), 4); BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(26), 1); BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28), 24); data[56] = 255;
        return data;
    }
}
