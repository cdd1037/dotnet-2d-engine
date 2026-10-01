using System.Diagnostics;

namespace GameAuthoringLab;

internal static unsafe class MissionHost
{
    public static int Run(bool scenario, int frames, string savePath)
    {
        using var engine = new EngineHost(false, 4096);
        var catalog = new AssetCatalog();
        using var bank = new TextureBank(engine, catalog);
        using var game = new MissionGame(Path.Combine(catalog.Root, "relay.mission.json"), catalog);
        using var ui = new GameUiSession(engine);
        var batch = new SpriteBatch(64) { TextureResolver = bank.Resolve };
        var camera = new Camera { Zoom = 1 };
        var draws = new SpriteDraw[128];
        int checks = 0;
        uint generation = 0;
        bool canLoad = File.Exists(savePath);
        (MissionScreen Screen, string Title, string Objective, string Status, int Seconds, bool CanSave, bool CanLoad)? lastModel = null;
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("GAME UI: " + label); checks++; }
        void Render()
        {
            bank.Sync(game.Room.World); game.Room.World.ExtractSprites(batch);
            batch.Draws.CopyTo(draws); int count = batch.Count;
            if (game.Room.RoomIndex == game.Definition.DeliveryRoom)
            {
                float x = game.Definition.DeliveryX, y = game.Definition.DeliveryY;
                draws[count++] = Disc(x - 20, y - 20, 80, .3f, 1, .75f, .35f);
                draws[count++] = Disc(x - 8, y - 8, 56, .12f, .35f, .3f, .9f);
                draws[count++] = Disc(x + 11, y + 11, 18, .65f, 1, .8f, 1);
            }
            engine.Draw(camera, draws.AsSpan(0, count));
        }
        void Model()
        {
            string status = game.Notice;
            if (game.Screen == MissionScreen.Playing)
                status = game.Room.Held is not null
                    ? game.Room.RoomIndex == 0 ? "CELL LINKED - east door: T" : "CELL LINKED - upper-right glowing relay: E to deliver"
                    : game.Room.ItemRoomIndex == game.Room.RoomIndex ? "Find the amber power cell. E picks up; F drops." : "The cell is in the other room. Use T at the door.";
            if (game.Screen == MissionScreen.Playing && (game.Notice.StartsWith("Could not", StringComparison.Ordinal) || game.Notice.StartsWith("Saved", StringComparison.Ordinal))) status += " | " + game.Notice;
            string title = game.Screen switch { MissionScreen.Paused => "PAUSED / 暂停", MissionScreen.Won => "RELAY RESTORED / 任务完成", MissionScreen.Lost => "TIME IS UP / 时间耗尽", _ => game.Definition.Title };
            var model = (game.Screen, title, game.Definition.Objective, status, game.SecondsLeft, game.Screen is MissionScreen.Playing or MissionScreen.Paused, canLoad);
            if (lastModel == model) return;
            generation = ui.Set(generation, game.Screen, title, game.Definition.Objective,
                status, game.SecondsLeft, game.Screen is MissionScreen.Playing or MissionScreen.Paused, canLoad);
            lastModel = model;
        }
        void Dispatch(GameUiCommand command)
        {
            try
            {
                switch (command)
                {
                    case GameUiCommand.Start:
                    case GameUiCommand.Restart: game.Start(candidate => bank.Sync(candidate.World)); break;
                    case GameUiCommand.Pause: game.Pause(); break;
                    case GameUiCommand.Resume: game.Resume(); break;
                    case GameUiCommand.Menu: game.Title(); break;
                    case GameUiCommand.Save: game.SaveFile(savePath); canLoad = true; break;
                    case GameUiCommand.Load: game.LoadFile(savePath, candidate => bank.Sync(candidate.World)); break;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SceneFormatException or UiAuthoringException or InvalidDataException or InvalidOperationException)
            {
                Console.Error.WriteLine($"{command} failed; previous usable run retained: {e.Message}");
                game.SetNotice($"Could not {command.ToString().ToLowerInvariant()}. Previous run kept. See terminal for file/field details.");
            }
            Model();
        }
        bool PumpActions()
        {
            GameUiAction action;
            while ((action = ui.Poll()).Action != 0)
            {
                if (action.Generation != generation) continue;
                Dispatch((GameUiCommand)action.Action);
                // Exactly one command per input/render boundary. Prevent double-click
                // batches from consuming the next screen or loading twice.
                while (ui.Poll().Action != 0) { }
                return true;
            }
            return false;
        }
        void Click(GameUiCommand action) { ui.Command(generation, action); PumpActions(); Render(); }
        ui.Load(Path.Combine(catalog.Root, "ui", "game.rml"));
        Render(); generation = ui.State.Generation; Model(); Render();
        if (scenario)
        {
            string? captureRoot = Environment.GetEnvironmentVariable("GAL_GAME_CAPTURE");
            void Capture(string name)
            {
                if (captureRoot is null) return;
                Directory.CreateDirectory(captureRoot);
                ui.Capture(Path.Combine(captureRoot, name + ".bmp")); Render();
            }
            Capture("title");
            Check(ui.State.Loaded == 1 && game.Screen == MissionScreen.Title, "title UI committed");
            uint titleGeneration = generation;
            Click(GameUiCommand.Start);
            Check(game.Screen == MissionScreen.Playing && generation != titleGeneration, "real Start listener advances screen generation");
            game.Advance(Native.Right | Native.Interact, 10);
            Check(game.Room.Player.LocalTransform.X == 160, "held title keys cannot enter gameplay");
            game.Advance(0, 0); game.Advance(Native.Right, RoomGame.FixedDelta);
            Check(game.Room.Player.LocalTransform.X == 163, "fresh input moves");
            Click(GameUiCommand.Pause); Capture("paused");
            int timer = game.RemainingTicks; var paused = game.Room.Player.LocalTransform;
            for (int i = 0; i < 120; i++) game.Advance(Native.Right | Native.Interact | Native.Transition, RoomGame.FixedDelta);
            Check(game.Room.Player.LocalTransform == paused && game.RemainingTicks == timer, "pause freezes gameplay and timer");
            Check(ui.State.KeyboardFocus == 0, "game UI has no hidden text input focus");
            Click(GameUiCommand.Save); Check(File.Exists(savePath), "Save button writes checkpoint");
            var priorWorld = game.Room.World;
            Click(GameUiCommand.Load); Check(priorWorld.EntityCount == 0 && game.Screen == MissionScreen.Paused, "Load retires previous run and remains paused");
            Click(GameUiCommand.Resume); game.Advance(0, 0);
            game.Room.Player.LocalTransform = new Transform2D(230, 280); game.Advance(Native.Interact, RoomGame.FixedDelta);
            game.Room.Player.LocalTransform = new Transform2D(830, 280); game.Advance(Native.Transition, RoomGame.FixedDelta);
            Check(game.Room.RoomIndex == 1 && game.Room.Held is not null, "pickup and room transition");
            Model(); Render(); Capture("archive");
            game.Room.Player.LocalTransform = new Transform2D(game.Definition.DeliveryX, game.Definition.DeliveryY);
            game.Advance(Native.Interact, RoomGame.FixedDelta); Model(); Render(); Capture("won");
            Check(game.Screen == MissionScreen.Won, "delivery reaches won screen");
            Click(GameUiCommand.Restart); game.Advance(0, 0);
            for (int i = 0; i < game.Definition.Seconds * 60; i++) game.Advance(0, RoomGame.FixedDelta);
            Model(); Render(); Capture("lost"); Check(game.Screen == MissionScreen.Lost, "timeout reaches lost screen");
            for (int cycle = 0; cycle < 12; cycle++)
            {
                var retired = game.Room.World;
                Click(GameUiCommand.Restart); Check(retired.EntityCount == 0 && game.Room.World.EntityCount == 6, "repeated restart retires world");
                game.Advance(0, 0); Click(GameUiCommand.Pause);
                uint stale = generation;
                Click(GameUiCommand.Menu);
                bool rejected = false;
                try { ui.Command(stale, GameUiCommand.Resume); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected && ui.State.Queued == 0, "old menu commands cannot enter new screen");
                Click(GameUiCommand.Start); Click(GameUiCommand.Pause);
                Check(engine.TextureCount == bank.LoadedCount && bank.LoadedCount <= 4, "native texture ownership remains bounded");
            }
            Click(GameUiCommand.Menu); Capture("final-title");
            ui.Dispose(); UiState closed = new() { Size = (uint)sizeof(UiState) };
            Check(UiNative.State(engine.NativeContext, &closed) != 0, "final native UI context disposed");
            game.Dispose(); bank.Dispose(); Check(engine.TextureCount == 0 && game.Room.World.EntityCount == 0, "final world and textures disposed");
            Console.WriteLine($"GAME SCENARIO PASS assertions={checks}; actual Rml listeners + synthetic host input + software renderer; physical GPU/audio/IME not covered");
            return 0;
        }
        Console.WriteLine("RELAY | WASD/arrows move; E pickup/deliver; F drop; T nearby door | Escape pause/resume | Space start/resume/restart | F5 save; F9 load | paused T title | close window quits");
        Console.WriteLine("Save file: " + Path.GetFullPath(savePath));
        var clock = Stopwatch.StartNew(); double previousTime = clock.Elapsed.TotalSeconds; uint previousKeys = 0;
        for (int frame = 0; frames == 0 || frame < frames; frame++)
        {
            double now = clock.Elapsed.TotalSeconds; float dt = (float)Math.Clamp(now - previousTime, 0, .25); previousTime = now;
            var input = engine.Poll(); if (input.Quit != 0) break;
            int revision = game.Revision;
            if ((input.Keys & Native.FocusLost) != 0) game.Pause();
            uint keys = input.Keys & ~Native.FocusLost, pressed = keys & ~previousKeys; previousKeys = keys;
            bool fromUi = false;
            if ((input.Keys & Native.FocusLost) == 0) fromUi = PumpActions();
            else while (ui.Poll().Action != 0) { }
            if (!fromUi && game.Revision == revision && (input.Keys & Native.FocusLost) == 0)
            {
                if ((pressed & Native.Escape) != 0)
                { if (game.Screen == MissionScreen.Playing) Dispatch(GameUiCommand.Pause); else if (game.Screen == MissionScreen.Paused) Dispatch(GameUiCommand.Resume); }
                else if ((pressed & Native.Space) != 0)
                {
                    if (game.Screen == MissionScreen.Title) Dispatch(GameUiCommand.Start);
                    else if (game.Screen == MissionScreen.Paused) Dispatch(GameUiCommand.Resume);
                    else if (game.Screen is MissionScreen.Won or MissionScreen.Lost) Dispatch(GameUiCommand.Restart);
                }
                else if ((pressed & Native.Save) != 0 && game.Screen is MissionScreen.Playing or MissionScreen.Paused) Dispatch(GameUiCommand.Save);
                else if ((pressed & Native.Load) != 0) Dispatch(GameUiCommand.Load);
                else if ((pressed & Native.Transition) != 0 && game.Screen is MissionScreen.Paused or MissionScreen.Won or MissionScreen.Lost) Dispatch(GameUiCommand.Menu);
            }
            game.Advance(keys, dt); Model(); Render(); Thread.Sleep(1);
        }
        return 0;
    }
    private static SpriteDraw Disc(float x, float y, float size, float r, float g, float b, float a) => new()
    { M11 = 1, M22 = 1, X = x, Y = y, Width = size, Height = size, R = r, G = g, B = b, A = a };
}
