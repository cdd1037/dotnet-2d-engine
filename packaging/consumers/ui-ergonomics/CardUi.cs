using CardRules;
using GameAuthoringLab;
namespace CardUiOurs;

public sealed class CardUi : IDisposable
{
    private readonly EngineHost _engine;
    private readonly UiModelSession<InventoryView> _ui;
    private readonly AssetRoot _assets;
    private readonly string _savePath;
    public CardGame Game { get; }
    public string Message { get; private set; } = "Inspect a card, or use it directly";
    public UiBindingStatus Status => _ui.Status;
    public UiCommandEvent LastCommand { get; private set; }
    public int Commands { get; private set; }
    public ulong SnapshotApplies => _ui.NativeApplyCalls;
    public bool IsCurrent(UiCommandEvent command) => _ui.IsCurrent(command);

    public CardUi(EngineHost engine, AssetRoot assets, string savePath)
    {
        _engine = engine;
        _assets = assets;
        _savePath = savePath;
        Game = new CardGame(File.ReadAllText(assets.Resolve("catalog.json")));
        // Stable wire IDs live beside the name, typed arguments and handler, once.
        var commands = new UiCommands()
            .On("inspect", 1, UiArgs.Key, (ulong id) => { Game.Inspect(id); })
            .On("use", 2, UiArgs.Key, (ulong id) => { Game.Use(id); })
            .On("pause", 3, Game.TogglePause)
            .On("restart", 4, () => { Game.Restart(); Message = "Restarted"; })
            .On("save", 5, () => { File.WriteAllText(_savePath, Game.Save()); Message = "Saved"; })
            .On("load", 6, () => { Game.Load(File.ReadAllText(_savePath)); Message = "Loaded"; })
            .On("reverse", 7, () => { Game.Reverse(); Message = "Order reversed"; })
            .On("remove_empty", 8, () => { Game.RemoveEmpty(); Message = "Empty cards removed"; })
            .On("discard", 9, UiArgs.Key, (ulong id) => { Game.Discard(id); });
        _ui = new UiModelSession<InventoryView>(engine, InventorySchema.Create(), commands);
        try { Reload(); }
        catch { _ui.Dispose(); throw; }
    }

    public void Reload()
    {
        _ui.LoadAsset(_assets, "ui/cards.rml");
        Draw(); // Publish staged source, then project and synchronize values.
        Refresh();
        Draw();
    }

    public void Refresh() => _ui.Apply(Game.View(File.Exists(_savePath), Message));

    public bool Frame()
    {
        if (_engine.PollInput().Quit != 0) return false;
        for (var command = _ui.Poll(); !command.IsEmpty; command = _ui.Poll())
        {
            try
            {
                // Dispatch checks current document/revision/kinds/keys immediately before calling.
                if (!_ui.Dispatch(command)) continue;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                Message = "Save/load failed; previous state kept";
            }
            LastCommand = command;
            Commands++;
        }
        Refresh(); // Drain this revision before applying once; handlers still enforce game rules.
        Draw();
        return true;
    }

    public void Draw() => _engine.Draw(new Camera { Zoom = 1 }, ReadOnlySpan<SpriteDraw>.Empty);
    public void Capture(string path)
    {
        _ui.Capture(path);
        Draw();
    }
    public void Dispose() => _ui.Dispose();
}
