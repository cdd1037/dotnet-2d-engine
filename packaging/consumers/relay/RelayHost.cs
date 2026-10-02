using GameAuthoringLab;
namespace Relay;

internal enum RelayCommand : uint { Start = 1, Pause, Resume, Save, Load, Restart, Menu }

// Explicit app-owned loop. Input, commands, simulation, projection and rendering remain visible.
internal sealed class RelayHost : IDisposable
{
    private readonly EngineHost engine;
    private readonly MissionGame game;
    private readonly string savePath;
    private readonly TextureBank bank;
    private readonly UiModelSession<RelayView> ui;
    private readonly SpriteBatch batch = new(64);
    private readonly SpriteDrawV2[] draws = new SpriteDrawV2[128];
    private readonly InputActionMap actions = RelayInput.CreateActions();
    private readonly DiagnosticLog diagnostics = new(16) { MinimumLevel = DiagnosticLevel.Info };
    private bool canLoad;
    public int Commands { get; private set; }
    public int Failures { get; private set; }
    public UiCommandEvent LastCommand { get; private set; }
    public UiBindingStatus UiStatus => ui.Status;
    public int LoadedTextures => bank.LoadedCount;
    public bool IsCurrent(UiCommandEvent packet) => ui.IsCurrent(packet);
    public bool DispatchPacket(UiCommandEvent packet) => ui.Dispatch(packet);
    public RelayHost(EngineHost engine, MissionGame game, AssetCatalog catalog, string savePath)
    {
        this.engine = engine; this.game = game; this.savePath = savePath;
        canLoad = File.Exists(savePath);
        bank = new TextureBank(engine, catalog); batch.RegionResolver = bank.ResolveRegion;
        var commands = new UiCommands()
            .On("start", (uint)RelayCommand.Start, () => Dispatch(RelayCommand.Start))
            .On("pause", (uint)RelayCommand.Pause, () => Dispatch(RelayCommand.Pause))
            .On("resume", (uint)RelayCommand.Resume, () => Dispatch(RelayCommand.Resume))
            .On("save", (uint)RelayCommand.Save, () => Dispatch(RelayCommand.Save))
            .On("load", (uint)RelayCommand.Load, () => Dispatch(RelayCommand.Load))
            .On("restart", (uint)RelayCommand.Restart, () => Dispatch(RelayCommand.Restart))
            .On("menu", (uint)RelayCommand.Menu, () => Dispatch(RelayCommand.Menu));
        try { ui = new UiModelSession<RelayView>(engine, RelayView.Schema(), commands); }
        catch { bank.Dispose(); throw; }
        try { ui.LoadAsset(catalog.Assets, "ui/game.rml"); Render(); Refresh(); Render(); }
        catch { try { ui.Dispose(); } finally { bank.Dispose(); } throw; }
    }
    public void Dispatch(RelayCommand command)
    {
        var prior = game.Room;
        try
        {
            // A current packet is not game-rule authority. Hidden/inapplicable commands are harmless.
            switch (command)
            {
                case RelayCommand.Start when game.Screen == MissionScreen.Title:
                case RelayCommand.Restart when game.Screen is MissionScreen.Paused or MissionScreen.Won or MissionScreen.Lost:
                    game.Start(candidate => bank.Sync(candidate.World)); break;
                case RelayCommand.Pause: game.Pause(); break;
                case RelayCommand.Resume: game.Resume(); break;
                case RelayCommand.Menu when game.Screen is MissionScreen.Paused or MissionScreen.Won or MissionScreen.Lost:
                    game.Title(); break;
                case RelayCommand.Save when game.Screen is MissionScreen.Playing or MissionScreen.Paused:
                    game.SaveFile(savePath); canLoad = true; break;
                case RelayCommand.Load: game.LoadFile(savePath, candidate => bank.Sync(candidate.World)); break;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SceneFormatException or UiAuthoringException or InvalidDataException or InvalidOperationException or AggregateException)
        {
            bool retained = ReferenceEquals(game.Room, prior);
            string outcome = retained ? "Previous run kept" : "New run committed; old-run cleanup reported errors";
            diagnostics.TryWrite(DiagnosticLevel.Error, 1, "relay.command", $"{command}: {outcome}");
            // Full source path/cause stays in stderr; bounded log records outcome without truncating diagnostics.
            Console.Error.WriteLine($"{command} failed; {outcome}: {e.Message}");
            game.SetNotice($"Could not {command.ToString().ToLowerInvariant()}. {outcome}. See terminal for file/field details.");
            Failures++;
        }
        while (diagnostics.TryRead(out _)) { } // Explicit bounded drain; stderr is the app's durable sink.
    }
    private bool PumpActions()
    {
        for (var packet = ui.Poll(); !packet.IsEmpty; packet = ui.Poll())
        {
            if (!ui.Dispatch(packet)) continue;
            LastCommand = packet; Commands++;
            // RELAY policy: one command per frame, unlike a generic batch-editing UI.
            // Discard queued double clicks so a new screen cannot consume the old gesture.
            while (!ui.Poll().IsEmpty) { }
            return true;
        }
        return false;
    }
    public bool Frame(float elapsed)
    {
        var input = engine.PollInput(); if (input.Quit != 0) return false;
        var mapped = actions.Update(input);
        int revision = game.Revision;
        if (!input.Focused || !input.Drawable) game.Pause();
        bool fromUi = false;
        if (input.Focused && input.Drawable) fromUi = PumpActions();
        else while (!ui.Poll().IsEmpty) { }
        if (!fromUi && game.Revision == revision && input.Focused && input.Drawable)
        {
            uint pressed = mapped.Pressed;
            if ((pressed & RelayInput.Escape) != 0)
            { if (game.Screen == MissionScreen.Playing) Dispatch(RelayCommand.Pause); else if (game.Screen == MissionScreen.Paused) Dispatch(RelayCommand.Resume); }
            else if ((pressed & RelayInput.Space) != 0)
            {
                if (game.Screen == MissionScreen.Title) Dispatch(RelayCommand.Start);
                else if (game.Screen == MissionScreen.Paused) Dispatch(RelayCommand.Resume);
                else if (game.Screen is MissionScreen.Won or MissionScreen.Lost) Dispatch(RelayCommand.Restart);
            }
            else if ((pressed & RelayInput.Save) != 0) Dispatch(RelayCommand.Save);
            else if ((pressed & RelayInput.Load) != 0) Dispatch(RelayCommand.Load);
            else if ((pressed & RelayInput.Transition) != 0 && game.Screen is MissionScreen.Paused or MissionScreen.Won or MissionScreen.Lost) Dispatch(RelayCommand.Menu);
        }
        game.Advance(mapped.Down, mapped.Pressed, elapsed);
        Refresh(); if (input.Drawable) Render();
        return true;
    }
    public void Refresh() => ui.Apply(RelayView.From(game, canLoad));
    public void Render()
    {
        bank.Sync(game.Room.World); game.Room.World.ExtractSprites(batch);
        batch.RegionDraws.CopyTo(draws); int count = batch.Count;
        if (game.Room.RoomIndex == game.Definition.DeliveryRoom)
        {
            float x = game.Definition.DeliveryX, y = game.Definition.DeliveryY;
            draws[count++] = Disc(x - 20, y - 20, 80, .3f, 1, .75f, .35f);
            draws[count++] = Disc(x - 8, y - 8, 56, .12f, .35f, .3f, .9f);
            draws[count++] = Disc(x + 11, y + 11, 18, .65f, 1, .8f, 1);
        }
        engine.Draw(new Camera { Zoom = 1 }, draws.AsSpan(0, count));
    }
    public void Capture(string path) { ui.Capture(path); Render(); }
    public void Dispose() { try { ui.Dispose(); } finally { bank.Dispose(); } }
    private static SpriteDrawV2 Disc(float x, float y, float size, float r, float g, float b, float a) => SpriteDrawV2.Create(new SpriteDraw()
    { M11 = 1, M22 = 1, X = x, Y = y, Width = size, Height = size, R = r, G = g, B = b, A = a });
}
