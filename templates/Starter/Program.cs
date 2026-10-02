using System.Diagnostics;
using Dotnet2DStarter;
using GameAuthoringLab;

try
{
    var options = StarterOptions.Parse(args);
    if (options.Help) { Console.WriteLine(StarterOptions.Usage); return 0; }
    if (options.SelfTest) { StarterChecks.Run(); return 0; }
    var assets = new AssetRoot(options.Assets ?? Path.Combine(AppContext.BaseDirectory, "assets"));
    assets.Resolve("white.png"); // A missing copied file fails before native startup.
    using var engine = EngineHost.Create(headless: options.Headless, maxSprites: 16);
    assets.ReadImageInfo("white.png");
    using var texture = engine.Textures.Acquire(assets, "white.png");
    var controls = new StarterInput();
    using var game = new StarterGame(engine, options.Physics, controls);
    var actions = controls.Map;
    var fixedInput = new FixedStepInput(StarterGame.StepSeconds);
    var camera = new Camera { Zoom = 1 };
    var draws = new SpriteCommand[1];
    var watch = Stopwatch.StartNew();
    double previous = watch.Elapsed.TotalSeconds;
    int frames = 0;
    Console.WriteLine($"Starter: {engine.Backend}; assets={assets.DirectoryPath}; physics={options.Physics}; no UI/font required.");
    Console.WriteLine("Arrows/WASD move; Space changes color; Escape pauses; T restarts; close window exits.");
    while (options.Frames == 0 || frames < options.Frames)
    {
        var input = engine.PollInputFrame(); // Poll once per outer frame, before reading UI/actions.
        if (input.Quit) break;
        var action = actions.Update(input);
        double now = watch.Elapsed.TotalSeconds;
        double elapsed = now - previous;
        previous = now; // Also update while paused; no resume-time catch-up.
        // Drain/check optional UI commands here, before deciding pause/restart.
        // Keep UI polling/model updates/drawing outside the fixed-step loop.
        if (action.IsPressed(controls.Pause)) game.Paused = !game.Paused;
        bool restart = action.IsPressed(controls.Restart);
        if (restart) { game.Restart(); fixedInput.Reset(); }
        bool suspended = game.Paused || restart || !input.Focused || !input.Drawable;
        // Headless is an explicit deterministic CLI test clock, never a live input path.
        if (options.Headless) { elapsed = StarterGame.StepSeconds; suspended = game.Paused || restart; }
        TimingStep frame = fixedInput.BeginFrame(elapsed, action, suspended);
        if (suspended) game.SnapPresentation();
        while (fixedInput.TryTakeStep(out var stepInput)) game.Tick(stepInput);
        // Optional UI/real-time timers advance with frame.RealSeconds even when paused.
        // Gameplay animations use a fixed TimingStep inside Tick, not both clocks.
        _ = frame;
        if (input.Drawable || options.Headless)
        {
            var position = game.PresentationPosition(fixedInput.InterpolationAlpha);
            draws[0] = new SpriteCommand(new(position.X - 16, position.Y - 16), new(32, 32),
                new(game.Paused ? .4f : 1, game.Pulses % 2 == 0 ? .8f : .2f, .3f, 1), texture.Texture);
            engine.Draw(camera, draws);
        }
        frames++;
        if (!options.Headless) Thread.Sleep(1); // Yield only, not a frame-rate guarantee.
    }
    var stats = engine.GetStats();
    Console.WriteLine($"STARTER PASS frames={frames} steps={game.Steps} pulses={game.Pulses} dropped-seconds={fixedInput.DroppedSeconds:F6} native-frames={stats.Frames} draw-calls={stats.DrawCalls}");
    return 0; // using declarations release game, texture, then engine on this thread.
}
catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException
    or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
{
    Console.Error.WriteLine($"STARTER ERROR: {error.Message}");
    if (error is AssetException)
        Console.Error.WriteLine("Assets: keep assets beside the built app, or pass --assets PATH (independent of working directory).");
    else if (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        Console.Error.WriteLine("Native: use matching local Linux x64 preview packages; keep native libraries together and verify glibc/C++ prerequisites in README.md. --headless still needs the native runtime.");
    else if (error is ArgumentException)
        Console.Error.WriteLine(StarterOptions.Usage);
    else
        Console.Error.WriteLine("See README.md troubleshooting. For Vulkan/display errors, --headless separates logic checks from graphics setup.");
    return 2;
}
