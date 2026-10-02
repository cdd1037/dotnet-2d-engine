using GameAuthoringLab;
namespace Relay;

// Scripted acceptance through the same app host as live play. SDL is used only to
// queue public input events, never to obtain an engine context or call private probes.
internal static class ScenarioChecks
{
    public static void Run(EngineHost engine, RelayHost host, MissionGame game, AssetCatalog catalog, string savePath)
    {
        int checks = 0;
        void Check(bool condition, string label)
        { if (!condition) throw new InvalidOperationException("RELAY SCENARIO: " + label); checks++; }
        foreach (string library in new[] { "libgal.so", "libSDL3.so.0" })
        {
            string[] loaded = File.ReadLines("/proc/self/maps")
                .Where(line => line.Contains("/" + library, StringComparison.Ordinal))
                .Select(line => line[line.IndexOf('/')..].Replace("\\040", " ", StringComparison.Ordinal))
                .Distinct().ToArray();
            Check(loaded.Length == 1 && loaded[0].StartsWith(AppContext.BaseDirectory, StringComparison.Ordinal), "loaded " + library + " comes from this app output");
            Console.WriteLine("RELAY NATIVE_LOADED " + loaded[0]);
        }
        string captures = Path.GetFullPath(Environment.GetEnvironmentVariable("RELAY_CAPTURE_DIR") ?? "relay-captures");
        Directory.CreateDirectory(captures);
        // Poll/update one zero-time outer frame before readback, just as live play
        // does after a model change; nested data-if layout settles there.
        void Capture(string name)
        { host.Frame(0); host.Capture(Path.Combine(captures, name + ".bmp")); Check(File.Exists(Path.Combine(captures, name + ".bmp")), name + " captured"); }
        void Click(int slot, RelayCommand command)
        {
            int prior = host.Commands;
            SdlInput.Click(321 + (slot % 3) * 150, 388 + (slot / 3) * 49);
            host.Frame(0);
            Check(host.Commands == prior + 1 && host.LastCommand.CommandId == (uint)command, "real " + command + " button");
        }
        void Tap(PhysicalKey key, float elapsed = 0) { SdlInput.Tap(key); host.Frame(elapsed); }
        void Draw() { host.Refresh(); host.Render(); }
        Check(!File.Exists(savePath), "isolated scenario checkpoint starts absent");
        Check(host.UiStatus.Loaded && !host.UiStatus.Pending && game.Screen == MissionScreen.Title, "generic title committed");
        SdlInput.Focus(); host.Frame(0); Capture("title");
        SdlInput.Key(PhysicalKey.D, true);
        Click(0, RelayCommand.Start); var titlePacket = host.LastCommand;
        Check(game.Screen == MissionScreen.Playing && !host.IsCurrent(titlePacket), "Start changes model and retires old packet");
        host.Dispatch(RelayCommand.Menu);
        Check(game.Screen == MissionScreen.Playing, "inapplicable Menu command cannot skip play");
        for (int i = 0; i < 10; i++) host.Frame(RoomGame.FixedDelta);
        Check(game.Room.Player.LocalTransform.X == 160 && game.RemainingTicks == 5400, "held title keys cannot enter gameplay");
        SdlInput.Key(PhysicalKey.D, false); host.Frame(0);
        SdlInput.Key(PhysicalKey.D, true); host.Frame(RoomGame.FixedDelta);
        SdlInput.Key(PhysicalKey.D, false); host.Frame(0);
        Check(game.Room.Player.LocalTransform.X == 163, "fresh SDL input moves one unchanged 3-pixel tick");
        int commands = host.Commands;
        SdlInput.Click(780, 82); SdlInput.Click(780, 82); host.Frame(0);
        Check(game.Screen == MissionScreen.Paused && host.Commands == commands + 1, "real Pause plus duplicate gesture is bounded to one command");
        Check(!host.DispatchPacket(titlePacket), "old title packet rejected by typed dispatch"); Capture("paused");
        int ticks = game.RemainingTicks; var position = game.Room.Player.LocalTransform;
        SdlInput.Key(PhysicalKey.D, true); Tap(PhysicalKey.E);
        for (int i = 0; i < 120; i++) host.Frame(RoomGame.FixedDelta);
        Check(game.RemainingTicks == ticks && game.Room.Player.LocalTransform == position, "pause freezes timer/movement and drops gameplay edges");
        SdlInput.Key(PhysicalKey.D, false); host.Frame(0);
        Click(1, RelayCommand.Save); Check(File.Exists(savePath), "Save writes checkpoint");
        string goodSave = File.ReadAllText(savePath);
        var retired = game.Room.World;
        Click(2, RelayCommand.Load);
        Check(retired.EntityCount == 0 && game.Screen == MissionScreen.Paused && game.RemainingTicks == ticks, "Load retires old world and stays paused with exact ticks");
        var live = game.Room.World; int failures = host.Failures;
        File.WriteAllText(savePath, "{ broken"); Click(2, RelayCommand.Load);
        Check(host.Failures == failures + 1 && ReferenceEquals(live, game.Room.World) && game.RemainingTicks == ticks,
            "bad save reports error and retains usable live run");
        File.WriteAllText(savePath, goodSave);
        string missionPath = catalog.Assets.Resolve("relay.mission.json"), mission = File.ReadAllText(missionPath);
        try
        {
            File.WriteAllText(missionPath, "{}"); failures = host.Failures; Click(3, RelayCommand.Restart);
            Check(host.Failures == failures + 1 && ReferenceEquals(live, game.Room.World), "bad authored mission retains live run");
        }
        finally { File.WriteAllText(missionPath, mission); }
        string imagePath = catalog.PathFor("room-b"); byte[] image = File.ReadAllBytes(imagePath);
        try
        {
            File.WriteAllBytes(imagePath, [0]); failures = host.Failures; Click(3, RelayCommand.Restart);
            Check(host.Failures == failures + 1 && ReferenceEquals(live, game.Room.World) && engine.TextureCount == host.LoadedTextures,
                "bad future-room asset retains world and bounded texture ownership");
        }
        finally { File.WriteAllBytes(imagePath, image); }
        Click(0, RelayCommand.Resume); host.Frame(0);
        Check(game.Screen == MissionScreen.Playing, "Resume returns to play");
        SdlInput.Key(PhysicalKey.D, true); host.Frame(RoomGame.FixedDelta);
        SdlInput.Focus(false); host.Frame(.25f); ticks = game.RemainingTicks; position = game.Room.Player.LocalTransform;
        Check(game.Screen == MissionScreen.Paused, "focus loss pauses");
        SdlInput.Focus(); host.Frame(.25f);
        Check(game.Screen == MissionScreen.Paused && game.RemainingTicks == ticks, "focus gain never auto resumes or catches up");
        SdlInput.Key(PhysicalKey.D, false); host.Frame(0); Tap(PhysicalKey.Space); host.Frame(0);
        Check(game.Room.Player.LocalTransform == position, "focus recovery releases held gameplay input");
        game.Room.Player.LocalTransform = new Transform2D(230, 280); Tap(PhysicalKey.E, RoomGame.FixedDelta);
        Check(game.Room.Held is not null, "SDL E picks up amber cell");
        Tap(PhysicalKey.F, RoomGame.FixedDelta); Check(game.Room.Held is null, "SDL F drops cell");
        Tap(PhysicalKey.E, RoomGame.FixedDelta); Check(game.Room.Held is not null, "cell can be picked up again");
        game.Room.Player.LocalTransform = new Transform2D(830, 280); Tap(PhysicalKey.T, RoomGame.FixedDelta);
        Check(game.Room.RoomIndex == 1 && game.Room.Held is not null, "door transition carries cell"); Capture("archive");
        game.Room.Player.LocalTransform = new Transform2D(game.Definition.DeliveryX, game.Definition.DeliveryY);
        Tap(PhysicalKey.E, RoomGame.FixedDelta); Check(game.Screen == MissionScreen.Won, "explicit relay delivery wins"); Capture("won");
        Click(1, RelayCommand.Restart); host.Frame(0);
        for (int i = 0; i < game.Definition.Seconds * 60; i++) game.Advance(0, RoomGame.FixedDelta);
        Draw(); Check(game.Screen == MissionScreen.Lost && game.RemainingTicks == 0, "unchanged 90-second simulation deadline loses"); Capture("lost");
        Click(2, RelayCommand.Menu); Check(game.Screen == MissionScreen.Title, "first loss Menu works");
        Click(0, RelayCommand.Start); host.Frame(0);
        for (int i = 0; i < game.Definition.Seconds * 60; i++) game.Advance(0, RoomGame.FixedDelta);
        Draw();
        for (int cycle = 0; cycle < 12; cycle++)
        {
            retired = game.Room.World;
            Tap(PhysicalKey.Space); host.Frame(0);
            Check(retired.EntityCount == 0 && game.Room.World.EntityCount == 6, "restart retires prior world");
            Tap(PhysicalKey.Escape); Check(game.Screen == MissionScreen.Paused, "keyboard pause");
            Click(4, RelayCommand.Menu); Check(game.Screen == MissionScreen.Title, "Menu returns to title");
            Click(0, RelayCommand.Start); host.Frame(0); Tap(PhysicalKey.Escape);
            Check(engine.TextureCount == host.LoadedTextures && host.LoadedTextures <= 4 && host.UiStatus.Overflow == 0, "restart ownership and UI queue remain bounded");
            // Next cycle starts from a result, as with the original scenario's restart command.
            host.Dispatch(RelayCommand.Restart); host.Refresh(); host.Render(); host.Frame(0);
            for (int i = 0; i < game.Definition.Seconds * 60; i++) game.Advance(0, RoomGame.FixedDelta);
            Draw();
        }
        Click(2, RelayCommand.Menu); Capture("final-title");
        host.Dispose(); game.Dispose();
        Check(engine.TextureCount == 0 && game.Room.World.EntityCount == 0, "final world and textures disposed");
        var commandsAfterClose = new UiCommands();
        foreach (var command in Enum.GetValues<RelayCommand>())
            commandsAfterClose.Add(command.ToString().ToLowerInvariant(), (uint)command);
        using (var reopened = new UiModelSession<RelayView>(engine, RelayView.Schema(), commandsAfterClose))
        {
            reopened.LoadAsset(catalog.Assets, "ui/game.rml");
            engine.Draw(new Camera { Zoom = 1 }, ReadOnlySpan<SpriteDraw>.Empty);
            Check(reopened.Status.Loaded, "native UI document opens under a fresh owner after disposal");
        }
        Console.WriteLine($"RELAY SCENARIO PASS assertions={checks}; full loop + real SDL/Rml commands + software readback; physical GPU/input/audio/IME not covered");
    }
}
