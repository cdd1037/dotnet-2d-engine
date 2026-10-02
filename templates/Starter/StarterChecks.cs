using GameAuthoringLab;

namespace Dotnet2DStarter;

// Small dependency-free checks. Keep or replace with your normal .NET test suite.
internal static class StarterChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException($"Check failed: {name}");
            checks++;
        }
        void Reject(Action action, string name)
        {
            try { action(); }
            catch (ArgumentException) { checks++; return; }
            throw new InvalidOperationException($"Expected rejection: {name}");
        }
        var loop = new FixedStepInput(.02, .1);
        loop.BeginFrame(.005, new(1, 1, 0), false);
        Check(!loop.TryTakeStep(out _), "zero-step frame retains edge");
        loop.BeginFrame(.015, new(0, 0, 1), false);
        Check(loop.TryTakeStep(out var tap) && tap == new ActionState(0, 1, 1), "press/release survives to one step");
        Check(!loop.TryTakeStep(out _), "one step only");
        loop.BeginFrame(.06, new(2, 2, 0), false);
        Check(loop.TryTakeStep(out var first) && first == new ActionState(2, 2, 0), "first catch-up step gets edge");
        Check(loop.TryTakeStep(out var second) && second == new ActionState(2, 0, 0), "next step keeps held only");
        while (loop.TryTakeStep(out _)) { }
        loop.Reset();
        loop.BeginFrame(.01, new(1, 1, 0), false);
        TimingStep pause = loop.BeginFrame(3, new(1, 1, 0), true);
        Check(pause.GameSeconds == 0 && pause.RealSeconds == .1, "pause uses zero game/capped real delta");
        Check(!loop.TryTakeStep(out _) && loop.DroppedSeconds == 2.9, "pause discards debt");
        loop.BeginFrame(.02, default, false);
        Check(loop.TryTakeStep(out var resumed) && resumed == default, "resume cannot fire stale edge");
        loop.BeginFrame(.01, new(1, 1, 0), false);
        loop.Reset();
        loop.BeginFrame(.02, default, false);
        Check(loop.TryTakeStep(out var restarted) && restarted == default, "restart discards pending edge");
        loop.Reset();
        loop.BeginFrame(10, new(2, 2, 0), false);
        int steps = 0, presses = 0;
        while (loop.TryTakeStep(out var step)) { steps++; if (step.Pressed != 0) presses++; }
        Check(steps == 5 && presses == 1, "long hitch bounded; edge once");
        Reject(() => loop.BeginFrame(double.NaN, default, false), "nonfinite elapsed");
        Reject(() => loop.BeginFrame(-1, default, false), "negative elapsed");
        Reject(() => new FixedStepInput(0), "zero fixed step");
        Reject(() => new FixedStepInput(.1, .01), "cap smaller than step");
        Reject(() => StarterOptions.Parse(["--frames", "0"]), "invalid frame count");
        Reject(() => StarterOptions.Parse(["--assets"]), "missing resource directory");
        Reject(() => StarterOptions.Parse(["--unknown"]), "unknown option");
        Check(StarterOptions.Parse(["--headless"]).Frames == 120, "bounded headless default");
        using var engine = EngineHost.Create(headless: true, maxSprites: 16);
        foreach (bool physics in new[] { false, true })
        {
            using (var game = new StarterGame(engine, physics))
            {
                for (int i = 0; i < 60; i++) game.Tick(new(StarterGame.Right, i == 0 ? StarterGame.Pulse : 0, 0));
                Check(Math.Abs(game.Position.X - 320) < .01 && game.Pulses == 1 && game.Steps == 60,
                    $"movement/one-shot {(physics ? "native physics" : "plain rules")}");
                game.Restart();
                Check(game.Position.X == 160 && game.Position.Y == 120 && game.Steps == 0 && game.Pulses == 0,
                    "restart state/pose/velocity");
            }
            // A new world can open after game disposal; old owner wasn't leaked.
            using var reopened = engine.OpenPhysics();
            Check(reopened.State.Bodies == 0, "game disposal releases physics world and bodies");
        }
        var assets = new AssetRoot(Path.Combine(AppContext.BaseDirectory, "assets"));
        Check(assets.ReadImageInfo("white.png").Width == 1, "copied PNG public preflight");
        using (var lease = engine.Textures.Acquire(assets, "white.png"))
            Check(engine.Textures.Count == 1, "headless managed texture lease acquired");
        Check(engine.Textures.Count == 0, "headless managed texture lease disposed before engine");
        Console.WriteLine($"STARTER CHECKS PASS assertions={checks}; public package; native physics + ownership; no graphical/keyboard acceptance");
    }
}
