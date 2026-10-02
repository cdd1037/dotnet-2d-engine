using System.Numerics;
using GameAuthoringLab;
using CharacterMotionRecipe;

try
{
    if (args.Length > 1 || (args.Length == 1 && args[0] != "--check"))
        throw new ArgumentException("Usage: Sample.character-motion [--check] (headless native physics)");
    using var engine = EngineHost.Create(headless: true);
    using (var game = new CharacterMotion(engine))
    {
        if (args.Contains("--check")) MotionChecks.Run(game);
        else
        {
            void Tick(int count, float axis = 0) { for (int i = 0; i < count; i++) game.Tick(axis); }
            game.Teleport(new(350, 170)); Tick(120); Tick(30, 1);
            Console.WriteLine($"Uphill: player={game.Position}, grounded={game.Grounded}");
            game.Teleport(new(950, 220)); Tick(120);
            game.SetPlatformVelocity(new(60, -30)); Tick(60);
            Console.WriteLine($"Rising ride: player={game.Position}, platform={game.PlatformPosition}");
            game.SetPlatformVelocity(new(-60, 30)); Tick(60);
            Console.WriteLine($"Reversed ride: player={game.Position}, platform={game.PlatformPosition}");
            Console.WriteLine($"Half-step presentation: player={game.PresentationPosition(.5)}, platform={game.PresentationPlatformPosition(.5)}");
            Console.WriteLine($"CHARACTER MOTION DEMO PASS steps={game.Steps}; headless, no rendered-frame claim");
        }
    }
    using var reopened = engine.OpenPhysics(PhysicsSettings.Default);
    if (reopened.State.Bodies != 0) throw new InvalidOperationException("Recipe leaked physics ownership.");
    Console.WriteLine("CHARACTER MOTION OWNERSHIP PASS reopened-empty-world=true");
    return 0;
}
catch (Exception error) when (error is ArgumentException or InvalidOperationException
    or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
{
    Console.Error.WriteLine($"CHARACTER MOTION ERROR: {error.Message}");
    Console.Error.WriteLine("Use matching Linux x64 preview packages with Box2D enabled; headless still needs the native runtime.");
    return 2;
}
