using System.Text.Json.Nodes;
using CardRules;
using CardUiOurs;

internal static class DiscardChecks
{
    public static void Run(CardUi ui, string catalog, string savePath, string outputDirectory)
    {
        int checks = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("DISCARD CHECK: " + name);
            checks++;
        }
        const ulong potion = 9007199254740993, snack = 9007199254740994,
            coin = 9007199254740995, empty = 9007199254740996;
        var game = new CardGame(catalog);
        ItemView Item(CardGame g, ulong id) => g.View(false, "").Sections.SelectMany(s => s.Items).Single(i => i.Id == id);
        void Rejected(CardGame g, ulong id, string label)
        {
            string before = g.Save();
            Check(!g.Discard(id), label + " returns false");
            Check(g.Save() == before, label + " leaves full persisted state unchanged");
        }
        void DiscardOnlyStock(CardGame g, ulong id, string label)
        {
            var before = JsonNode.Parse(g.Save())!;
            int count = g.Count(id);
            Check(g.Discard(id), label + " succeeds");
            Check(g.Count(id) == count - 1, label + " removes exactly one");
            var after = JsonNode.Parse(g.Save())!;
            before["Counts"]![id.ToString()] = count - 1;
            Check(JsonNode.DeepEquals(before, after), label + " changes only target stock");
        }
        Check(Item(game, potion).CanDiscard && !Item(game, empty).CanDiscard, "stock availability");
        Rejected(game, 0, "zero identity");
        Rejected(game, ulong.MaxValue, "unknown identity");
        Rejected(game, empty, "empty stock");
        game.Inspect(potion);
        DiscardOnlyStock(game, potion, "selected healing card");
        Check(game.SelectedId == potion && Item(game, potion).Selected && Item(game, potion).CanDiscard,
            "selection and availability retained");
        DiscardOnlyStock(game, potion, "last stock");
        Check(game.IsVisible(potion) && !Item(game, potion).CanDiscard && Item(game, potion).CanInspect,
            "depletion keeps card visible and inspectable");
        Rejected(game, potion, "depleted stock");
        DiscardOnlyStock(game, coin, "gold card");
        game.TogglePause();
        Check(game.View(false, "").Sections.SelectMany(s => s.Items).All(i => !i.CanDiscard), "all paused availability");
        Rejected(game, snack, "paused positive stock");
        string saved = game.Save();
        game.Restart();
        Check(game.Count(potion) == 2 && game.Count(snack) == 1 && game.Count(coin) == 1 && game.Count(empty) == 0,
            "restart initial counts");
        game.Load(saved);
        Check(game.Save() == saved && !Item(game, snack).CanDiscard, "save/load preserves discarded counts and paused availability");
        game.TogglePause();
        game.RemoveEmpty();
        Rejected(game, potion, "removed card");
        var hiddenPositive = JsonNode.Parse(game.Save())!;
        hiddenPositive["Counts"]![potion.ToString()] = 2;
        game.Load(hiddenPositive.ToJsonString());
        Rejected(game, potion, "removed card with positive stock");
        game.Restart();
        game.Reverse();
        DiscardOnlyStock(game, snack, "reordered exact identity");
        game.RemoveEmpty();
        Check(game.View(false, "").Sections[0].Items[0].Id == potion, "shrink retains exact remaining identity");
        DiscardOnlyStock(game, potion, "post-shrink identity");
        var maxGame = new CardGame(catalog.Replace(potion.ToString(), ulong.MaxValue.ToString()));
        DiscardOnlyStock(maxGame, ulong.MaxValue, "full ulong identity");
        Console.WriteLine($"DISCARD RULES PASS assertions={checks}");

        if (File.Exists(savePath)) File.Delete(savePath);
        ui.Game.Restart();
        ui.Refresh();
        ui.Draw();
        void Click(float x, float y) { SdlInput.Click(x, y); ui.Frame(); }
        void NativeDiscard(float x, float y, ulong id, string label)
        {
            int before = ui.Commands;
            int count = ui.Game.Count(id);
            int health = ui.Game.Health, gold = ui.Game.Gold;
            ulong selected = ui.Game.SelectedId;
            var visible = ui.Game.View(false, "").Sections.SelectMany(s => s.Items).Select(i => i.Id).ToArray();
            Click(x, y);
            Check(ui.Commands == before + 1 && ui.LastCommand.CommandId == 9 &&
                ui.LastCommand[0].Key == id, label + " dispatches exact typed key");
            Check(ui.Game.Count(id) == count - 1 && ui.Game.Health == health && ui.Game.Gold == gold &&
                ui.Game.SelectedId == selected && visible.SequenceEqual(ui.Game.View(false, "").Sections.SelectMany(s => s.Items).Select(i => i.Id)),
                label + " changes only stock");
            Check(ui.Status.Diagnostic.Length == 0 && ui.Status.Overflow == 0, label + " no diagnostics");
        }
        void Disabled(float x, float y, string label)
        {
            int before = ui.Commands;
            string state = ui.Game.Save();
            Click(x, y);
            Check(ui.Commands == before && ui.Game.Save() == state, label + " emits no native command or state change");
        }
        SdlInput.Focus(); ui.Frame();
        Check(ui.Status.Loaded && !ui.Status.Pending && ui.Status.Diagnostic.Length == 0, "native document loaded");
        ui.Capture(Path.Combine(outputDirectory, "discard-enabled.bmp"));
        Click(104, 228);
        Check(ui.LastCommand.CommandId == 1, "Inspect keeps wire ID 1");
        NativeDiscard(338, 228, potion, "first row Discard");
        NativeDiscard(338, 228, potion, "depleting selected card");
        Disabled(338, 228, "depleted Discard");
        Disabled(788, 378, "initially empty Discard");
        Click(550, 378);
        Check(ui.Game.SelectedId == empty, "empty Inspect unchanged");
        NativeDiscard(788, 228, coin, "other section Discard");
        Click(100, 460);
        Check(ui.LastCommand.CommandId == 3, "Pause keeps wire ID 3");
        Disabled(338, 378, "paused positive-stock Discard");
        Disabled(338, 228, "paused depleted Discard");
        ui.Capture(Path.Combine(outputDirectory, "discard-disabled.bmp"));
        Click(350, 460);
        Check(ui.LastCommand.CommandId == 5, "Save keeps wire ID 5");
        string nativeSave = File.ReadAllText(savePath);
        Click(230, 460);
        Check(ui.LastCommand.CommandId == 4, "Restart keeps wire ID 4");
        Check(ui.Game.Count(potion) == 2 && ui.Game.Count(coin) == 1 && !ui.Game.Paused, "native Restart initial counts");
        Click(480, 460);
        Check(ui.LastCommand.CommandId == 6, "Load keeps wire ID 6");
        Check(ui.Game.Save() == nativeSave, "native Save/Load retains discarded stock, selection and pause");
        Click(100, 460);
        NativeDiscard(338, 378, snack, "resumed Discard");
        Click(230, 460);
        Click(600, 460);
        Check(ui.LastCommand.CommandId == 7, "Reverse keeps wire ID 7");
        NativeDiscard(338, 228, snack, "reordered first row key");
        Disabled(788, 228, "reordered empty first row");
        NativeDiscard(788, 378, coin, "reordered second section key");
        Click(750, 460);
        Check(ui.LastCommand.CommandId == 8, "Remove empty keeps wire ID 8");
        Check(!ui.Game.IsVisible(snack) && !ui.Game.IsVisible(coin) && !ui.Game.IsVisible(empty), "native Remove empty shrinks arrays");
        NativeDiscard(338, 228, potion, "remaining post-shrink row key");
        Click(220, 228);
        Check(ui.LastCommand.CommandId == 2, "Use keeps wire ID 2");
        Check(ui.Game.Health == 75 && ui.Game.Count(potion) == 0, "existing Use after Discard and shrink");
        Disabled(338, 228, "Use-depleted Discard");
        Click(750, 460);
        Check(ui.Game.View(false, "").Sections.All(s => s.Items.Length == 0), "fully empty sections render");
        ui.Reload();
        Check(ui.Status.Diagnostic.Length == 0 && ui.Status.Overflow == 0, "reload and all structural mutations have no diagnostics");
        Click(230, 460);
        Check(ui.Game.View(false, "").Sections.SelectMany(s => s.Items).Count() == 4, "toolbar remains usable after empty lists");

        int queuedCommands = ui.Commands;
        ulong applies = ui.SnapshotApplies;
        SdlInput.Click(338, 228);
        SdlInput.Click(338, 228);
        SdlInput.Click(338, 228);
        ui.Frame();
        Check(ui.Commands == queuedCommands + 3 && ui.Game.Count(potion) == 0 && ui.Game.Health == 50 && ui.Game.Gold == 0,
            "same-revision Discard queue drains fully while game guard prevents negative stock");
        Check(ui.SnapshotApplies == applies + 1 && !ui.IsCurrent(ui.LastCommand),
            "one changed projection follows the full drain and retires those packets");
        queuedCommands = ui.Commands;
        applies = ui.SnapshotApplies;
        SdlInput.Click(100, 460);
        SdlInput.Click(100, 460);
        ui.Frame();
        Check(ui.Commands == queuedCommands + 2 && !ui.Game.Paused && ui.SnapshotApplies == applies,
            "two queued toggles execute before the one unchanged projection");
        Check(ui.Status.Overflow == 0 && ui.Status.Diagnostic.Length == 0, "batched dispatch has no diagnostics");

        // The captures exercise the authored disabled class in native rendering, not only model booleans.
        (int R, int G, int B) Pixel(string path, int x, int y)
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            Check(reader.ReadUInt16() == 0x4d42, "capture is BMP");
            reader.BaseStream.Position = 10;
            int offset = reader.ReadInt32();
            reader.BaseStream.Position = 18;
            int width = reader.ReadInt32(), height = reader.ReadInt32();
            reader.ReadUInt16();
            int bpp = reader.ReadUInt16();
            Check(bpp is 24 or 32, "capture pixel format");
            int stride = ((width * bpp + 31) / 32) * 4;
            int row = height > 0 ? height - 1 - y : y;
            reader.BaseStream.Position = offset + row * stride + x * bpp / 8;
            int b = reader.ReadByte(), g = reader.ReadByte(), r = reader.ReadByte();
            return (r, g, b);
        }
        var enabled = Pixel(Path.Combine(outputDirectory, "discard-enabled.bmp"), 303, 220);
        var disabled = Pixel(Path.Combine(outputDirectory, "discard-disabled.bmp"), 303, 220);
        Check(enabled == (122, 211, 181), $"enabled Discard fill: {enabled}");
        Check(disabled == (41, 62, 77), $"disabled Discard fill: {disabled}");
        Console.WriteLine($"DISCARD NATIVE UI PASS total_assertions={checks}; SDL pointer routing, disabled command suppression, rendered appearance, save/load/restart, reorder/shrink");
    }
}
