using System.Diagnostics;
using GameAuthoringLab;
using Relay;

try
{
    bool check = false, rules = false;
    int frames = 0;
    string save = Path.GetFullPath("relay-save.json");
    for (int i = 0; i < args.Length; i++)
        switch (args[i])
        {
            case "--check": check = true; break;
            case "--rules": rules = true; break;
            case "--save-file" when i + 1 < args.Length: save = Path.GetFullPath(args[++i]); break;
            case "--frames" when i + 1 < args.Length && int.TryParse(args[++i], out frames) && frames >= 0: break;
            default: throw new ArgumentException("Usage: Relay [--check | --rules] [--frames N] [--save-file PATH]");
        }
    string assetRoot = Path.Combine(AppContext.BaseDirectory, "assets");
    if (rules) { RuleChecks.Run(assetRoot); return 0; }
    if (check)
    {
        RuleChecks.Run(assetRoot);
        save = Path.Combine(Path.GetTempPath(), "relay-scenario-" + Guid.NewGuid().ToString("N") + ".json");
    }
    string? font = Environment.GetEnvironmentVariable("GAL_UI_FONT");
    if (string.IsNullOrWhiteSpace(font) || !File.Exists(font))
        throw new InvalidOperationException("RELAY font: set GAL_UI_FONT to a readable licensed Noto Sans CJK SC font matching assets/ui/game.rcss; fonts are not bundled.");
    using (File.OpenRead(font)) { }
    var catalog = RelayAssets.Catalog(assetRoot);
    // Validate mission/art before opening native graphics so missing input names are actionable.
    string missionPath = catalog.Assets.Resolve("relay.mission.json");
    _ = MissionDefinition.Load(missionPath);
    foreach (string key in catalog.Paths.Keys) _ = catalog.PathFor(key);
    using var engine = EngineHost.Create(maxSprites: 128);
    using var game = new MissionGame(missionPath, catalog);
    using var host = new RelayHost(engine, game, catalog, save);
    if (check)
    {
        try { ScenarioChecks.Run(engine, host, game, catalog, save); }
        finally { if (File.Exists(save)) File.Delete(save); }
    }
    else
    {
        Console.WriteLine("RELAY | WASD/arrows move; E pickup/deliver; F drop; T door | Escape pause/resume | Space start/resume/restart | F5 save; F9 load | paused T title | close to quit");
        Console.WriteLine("Save file: " + save);
        var clock = Stopwatch.StartNew(); double previous = clock.Elapsed.TotalSeconds;
        for (int frame = 0; frames == 0 || frame < frames; frame++)
        {
            double now = clock.Elapsed.TotalSeconds;
            float elapsed = (float)Math.Clamp(now - previous, 0, .25);
            previous = now; // Sample during pause, focus loss and minimization, too.
            if (!host.Frame(elapsed)) break;
            Thread.Sleep(1);
        }
    }
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("RELAY: " + error.Message);
    if (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException ||
        error.Message.Contains("generic UI", StringComparison.OrdinalIgnoreCase))
        Console.Error.WriteLine("Check that the local Engine/native packages include the generic UI bridge and that the Linux x64 graphics prerequisites are available. See README.md; no runtime fallback/download is attempted.");
    return 1;
}
