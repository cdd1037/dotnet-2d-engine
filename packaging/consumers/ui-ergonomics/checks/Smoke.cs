using CardRules;
using CardUiOurs;
using GameAuthoringLab;
internal static class Smoke
{
    public static void Run(EngineHost engine, CardUi ui, string savePath, string outputDirectory)
    {
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("UI CHECK: " + label);
            checks++;
        }
        void Click(float x, float y) { SdlInput.Click(x, y); ui.Frame(); }
        const ulong potion = 9007199254740993, snack = 9007199254740994, coin = 9007199254740995, empty = 9007199254740996;
        SdlInput.Focus(); ui.Frame();
        ui.Capture(Path.Combine(outputDirectory, "ours-initial.bmp"));
        Check(ui.Status.Loaded && !ui.Status.Pending && ui.Status.Overflow == 0, "generic document published");
        Check(ui.Game.View(false, "").Sections.Sum(s => s.Items.Length) == 4, "four nested cards");
        Click(104, 228);
        Check(ui.Game.SelectedId == potion && ui.LastCommand[0].Key == potion, "Inspect uses exact key above 2^53");
        Check(!ui.IsCurrent(ui.LastCommand), "updated selection retires command");
        Click(220, 228);
        Check(ui.Game.Health == 75 && ui.Game.Count(potion) == 1, "first card use updates nested stock and HUD");
        Click(220, 228);
        Check(ui.Game.Health == 100 && ui.Game.Count(potion) == 0, "deplete clamps health");
        int before = ui.Commands; Click(220, 228);
        Check(ui.Commands == before, "disabled depleted Use blocked by native UI");
        Click(550, 378);
        Check(ui.Game.SelectedId == empty, "empty card still independently inspectable");
        before = ui.Commands; Click(670, 378);
        Check(ui.Commands == before, "empty Use disabled");
        Click(670, 228);
        Check(ui.Game.Gold == 7 && ui.Game.Count(coin) == 0, "other section use works");
        Click(220, 378);
        Check(ui.Game.Gold == 8 && ui.Game.Count(snack) == 0, "second row use works");
        Click(100, 460);
        Check(ui.Game.Paused, "pause button");
        before = ui.Commands; Click(104, 228);
        Check(ui.Commands == before, "pause disables card Inspect");
        Click(350, 460);
        Check(File.Exists(savePath), "public Save interaction persists state");
        string saved = File.ReadAllText(savePath);
        Click(230, 460);
        Check(!ui.Game.Paused && ui.Game.Health == 50, "Restart interaction");
        Click(480, 460);
        Check(ui.Game.Paused && ui.Game.Gold == 8, "Load restores state");
        File.WriteAllText(savePath, "{\"Version\":99}"); Click(480, 460);
        Check(ui.Game.Gold == 8 && ui.Message.Contains("failed"), "invalid save preserves state");
        File.WriteAllText(savePath, saved); Click(230, 460);
        Click(600, 460);
        Check(ui.Game.View(false, "").Sections[0].Items[0].Id == snack, "nested array reversed");
        Click(104, 228);
        Check(ui.Game.SelectedId == snack && ui.LastCommand[0].Key == snack, "reused first slot resolves new exact identity");
        Click(220, 228);
        Check(ui.Game.Health == 60 && ui.Game.Count(snack) == 0, "reordered Use affects new item");
        Click(750, 460);
        Check(!ui.Game.IsVisible(snack) && !ui.Game.IsVisible(empty) && ui.Game.SelectedId == 0, "remove empty shrinks both nested arrays and clears selection");
        Click(104, 228);
        Check(ui.Game.SelectedId == potion, "shrunk first row hit targets remaining potion");
        ui.Capture(Path.Combine(outputDirectory, "ours-updated.bmp"));
        Click(104, 228); // Selecting the already-selected item leaves the applied snapshot unchanged.
        var old = ui.LastCommand;
        Check(ui.IsCurrent(old), "unchanged snapshot command is current immediately before reload");
        ui.Reload();
        Check(!ui.IsCurrent(old) && ui.Status.Diagnostic.Length == 0, "reload retires previous generation without native diagnostics");
        for (int i = 0; i < 3; i++) { Click(600, 460); Click(230, 460); }
        Check(ui.Game.View(false, "").Sections.Sum(s => s.Items.Length) == 4 && ui.Game.Health == 50, "repeated reorder/restart restores four cards");
        Check(ui.Status.Overflow == 0 && ui.Status.Diagnostic.Length == 0, "no overflow or native warnings after mutations");
        File.WriteAllLines(Path.Combine(outputDirectory, "ours-loaded-library.txt"), File.ReadLines("/proc/self/maps").Where(line => line.Contains("/libgal.so")));
        Console.WriteLine($"RICH .NET2D UI PASS assertions={checks} commands={ui.Commands}; actual SDL pointer hit tests, generic native model and rendering");
    }
}
