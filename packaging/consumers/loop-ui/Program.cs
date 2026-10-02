using System.Diagnostics;
using Dotnet2DStarter;
using GameAuthoringLab;

// The harness copies StarterGame.cs, StarterInput.cs, FixedStepInput.cs and white.png unchanged.
// This optional UI example still consumes only the two public preview packages.
if (args.Any(arg => arg != "--check"))
    throw new ArgumentException("Usage: Sample.loop-ui [--check]");
string? font = Environment.GetEnvironmentVariable("GAL_UI_FONT");
if (string.IsNullOrWhiteSpace(font) || !File.Exists(font))
    throw new InvalidOperationException("Set GAL_UI_FONT to a readable, licensed font matching pause.rcss's font-family.");
using (File.OpenRead(font)) { } // Check access before starting native graphics.
var assets = new AssetRoot(Path.Combine(AppContext.BaseDirectory, "assets"));
using var engine = EngineHost.Create(maxSprites: 16);
using var texture = engine.Textures.Acquire(assets, "white.png");
var controls = new StarterInput();
using (var game = new StarterGame(engine, usePhysics: true, controls))
using (var ui = new UiModelSession<StarterGame>(engine,
    new UiRecord<StarterGame>().Boolean("paused", static game => game.Paused),
    new UiCommands().Add("toggle", LoopUiHost.Toggle).Add("restart", LoopUiHost.Restart)))
{
    ui.LoadAsset(assets, "ui/pause.rml");
    engine.Draw(new Camera { Zoom = 1 }, ReadOnlySpan<SpriteCommand>.Empty);
    ui.Apply(game); // Publish the loaded document, then its initial model.
    var host = new LoopUiHost(engine, game, ui, texture.Texture, controls);
    host.Frame(0);
    if (args.Contains("--check")) ScriptedChecks.Run(engine, host, game, ui);
    else
    {
        Console.WriteLine("WASD/arrows move; Space changes color; Escape or Pause toggles pause; T or Restart resets. Close the window to exit.");
        var watch = Stopwatch.StartNew();
        double previous = watch.Elapsed.TotalSeconds;
        while (true)
        {
            double now = watch.Elapsed.TotalSeconds;
            double elapsed = now - previous;
            previous = now; // Keep sampling through pause, focus loss and minimization.
            if (!host.Frame(elapsed)) break;
            Thread.Sleep(1);
        }
    }
}
if (args.Contains("--check"))
{
    // All restarts reused one game-owned world; final disposal allows a fresh one.
    using var reopened = engine.OpenPhysics();
    if (reopened.State.Bodies != 0) throw new InvalidOperationException("Game owner leaked physics bodies");
    Console.WriteLine("LOOP UI OWNERSHIP PASS reopened-empty-world=true");
}
