using System.Numerics;
using GameAuthoringLab;

namespace Dotnet2DStarter;

// Game-owned state and optional physics. No base game, service lookup or scanning.
internal sealed class StarterGame : IDisposable
{
    public const uint Left = 1, Right = 2, Up = 4, Down = 8, Pulse = 16, Pause = 32, RestartAction = 64;
    public const float StepSeconds = 1f / 60;
    private readonly PhysicsScale scale = new(64);
    private readonly PhysicsWorld? physics;
    private readonly PhysicsScope? bodies;
    private readonly PhysicsBody? body;
    public Vector2 Position { get; private set; } = new(160, 120);
    public int Pulses { get; private set; }
    public int Steps { get; private set; }
    public bool Paused { get; set; }

    public StarterGame(EngineHost engine, bool usePhysics)
    {
        if (!usePhysics) return;
        physics = engine.OpenPhysics(new(0, 0, StepSeconds, 4));
        bodies = new(physics);
        try
        {
            body = bodies.CreateBody(new(PhysicsBodyType.Dynamic,
                scale.ToMeters(Position.X), scale.ToMeters(Position.Y), FixedRotation: true));
            body.AddShape(new(PhysicsShapeType.Box, .25f, .25f));
        }
        catch { bodies.Dispose(); physics.Dispose(); throw; }
    }

    public void Tick(ActionState input)
    {
        Vector2 direction = new(
            ((input.Down & Right) != 0 ? 1 : 0) - ((input.Down & Left) != 0 ? 1 : 0),
            ((input.Down & Down) != 0 ? 1 : 0) - ((input.Down & Up) != 0 ? 1 : 0));
        if (direction.LengthSquared() > 1) direction = Vector2.Normalize(direction);
        Vector2 velocity = direction * 160;
        if (physics is not null && body is not null)
        {
            body.SetVelocity(scale.ToMeters(velocity.X), scale.ToMeters(velocity.Y));
            PhysicsStepResult step = physics.Step(); // Exactly one configured step.
            if (step.Dropped != 0) throw new InvalidOperationException("Physics events dropped; completed step must not be retried.");
            // Consume physics.Events here, before another Step/Dispose. Copy to retain.
            var pose = body.State; // Read one copied pose; meters -> pixels once.
            Position = new(scale.ToPixels(pose.X), scale.ToPixels(pose.Y));
        }
        else Position += velocity * StepSeconds;
        if ((input.Pressed & Pulse) != 0) Pulses++;
        Steps++;
    }

    public void Restart()
    {
        Position = new(160, 120);
        Pulses = Steps = 0;
        body?.Teleport(scale.ToMeters(Position.X), scale.ToMeters(Position.Y));
        body?.SetVelocity(0, 0);
        // Restart preserves the pause choice. There is no automatic save/load.
    }

    public void Dispose()
    {
        bodies?.Dispose(); // Bodies before their world; game before EngineHost.
        physics?.Dispose();
    }
}
