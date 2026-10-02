using CardRules;

internal static class RulesChecks
{
    public static void Run(string catalogJson)
    {
        int checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
        const ulong potion = 9007199254740993, snack = 9007199254740994, coin = 9007199254740995, empty = 9007199254740996;
        var game = new CardGame(catalogJson);
        Check(game.Health == 50 && game.Gold == 0, "initial state");
        Check(game.View(false, "").Sections[0].Items[0].Card.Stats.Length == 2, "nested stats");
        Check(game.Inspect(empty) && game.SelectedId == empty && !game.Use(empty), "empty inspect and disabled use");
        Check(game.Use(potion) && game.Health == 75 && game.Count(potion) == 1, "direct card use");
        Check(game.Use(potion) && game.Health == 100 && !game.Use(potion), "depletion and clamp");
        Check(game.Use(coin) && game.Gold == 7, "gold");
        game.TogglePause();
        Check(!game.Use(snack) && !game.Inspect(snack), "pause domain guard");
        string saved = game.Save(); game.Restart(); game.Load(saved);
        Check(game.Paused && game.Health == 100 && game.Gold == 7 && game.SelectedId == empty, "save roundtrip");
        try { game.Load("{\"Version\":99}"); throw new Exception("bad save accepted"); } catch (InvalidDataException) { checks++; }
        Check(game.Save() == saved, "bad save atomic state retention");
        game.Restart(); game.Reverse();
        Check(game.View(false, "").Sections[0].Items[0].Id == snack, "reverse exact IDs");
        game.Inspect(empty); game.RemoveEmpty();
        Check(!game.IsVisible(empty) && game.SelectedId == 0, "remove selected empty");
        Check(!game.Inspect(empty) && !game.Use(empty), "removed identity rejected");
        saved = game.Save(); game.Restart(); game.Load(saved);
        Check(!game.IsVisible(empty) && game.View(false, "").Sections[0].Items[0].Id == snack, "persist structural mutations");
        Console.WriteLine($"SHARED RULES PASS assertions={checks}");
    }
}
