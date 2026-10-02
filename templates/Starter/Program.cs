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
    using var game = new StarterGame(engine, options.Physics);
    var actions = new InputActionMap(
        InputBinding.Key(StarterGame.Left, PhysicalKey.A), InputBinding.Key(StarterGame.Left, PhysicalKey.Left),
        InputBinding.Key(StarterGame.Right, PhysicalKey.D), InputBinding.Key(StarterGame.Right, PhysicalKey.Right),
        InputBinding.Key(StarterGame.Up, PhysicalKey.W), InputBinding.Key(StarterGame.Up, PhysicalKey.Up),
        InputBinding.Key(StarterGame.Down, PhysicalKey.S), InputBinding.Key(StarterGame.Down, PhysicalKey.Down),
        InputBinding.Key(StarterGame.Pulse, PhysicalKey.Space),
        InputBinding.Key(StarterGame.Pause, PhysicalKey.Escape, allowUiConsumed: true),
        InputBinding.Key(StarterGame.RestartAction, PhysicalKey.T));
    var fixedInput = new FixedStepInput(StarterGame.StepSeconds);
    var camera = new Camera { Zoom = 1 };
    var draws = new SpriteDraw[1];
    var watch = Stopwatch.StartNew();
    double previous = watch.Elapsed.TotalSeconds;
    int frames = 0;
    Console.WriteLine($"Starter: {engine.Backend}; assets={assets.DirectoryPath}; physics={options.Physics}; no UI/font required.");
    Console.WriteLine("Arrows/WASD move; Space changes color; Escape pauses; T restarts; close window exits.");
    while (options.Frames == 0 || frames < options.Frames)
    {
        var input = engine.PollInput(); // Poll once per outer frame, before reading UI/actions.
        if (input.Quit != 0) break;
        var action = actions.Update(input);
        double now = watch.Elapsed.TotalSeconds;
        double elapsed = now - previous;
        previous = now; // Also update while paused; no resume-time catch-up.
        // Drain/check optional UI commands here, before deciding pause/restart.
        // Keep UI polling/model updates/drawing outside the fixed-step loop.
        if ((action.Pressed & StarterGame.Pause) != 0) game.Paused = !game.Paused;
        bool restart = (action.Pressed & StarterGame.RestartAction) != 0;
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
            draws[0] = new SpriteDraw {
                M11 = 1, M22 = 1, X = position.X - 16, Y = position.Y - 16,
                Width = 32, Height = 32, Texture = texture.Handle,
                R = game.Paused ? .4f : 1, G = game.Pulses % 2 == 0 ? .8f : .2f, B = .3f, A = 1
            };
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
