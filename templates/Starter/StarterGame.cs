using System.Numerics;
using GameAuthoringLab;

namespace Dotnet2DStarter;

// Game-owned state and optional physics. No base game, service lookup or scanning.
internal sealed class StarterGame : IDisposable
{
    private readonly StarterInput controls;
    public const float StepSeconds = 1f / 60;
    private static readonly Vector2 Spawn = new(160, 120);
    private Vector2 previousPosition = Spawn;
    private readonly PhysicsScale scale = new(64);
    private readonly PhysicsWorld? physics;
    private readonly PhysicsScope? bodies;
    private readonly PhysicsBody? body;
    public Vector2 Position { get; private set; } = Spawn;
    public int Pulses { get; private set; }
    public int Steps { get; private set; }
    public bool Paused { get; set; }

    public StarterGame(EngineHost engine, bool usePhysics, StarterInput controls)
    {
        this.controls = controls;
        if (!usePhysics) return;
        physics = engine.OpenPhysics(new(0, 0, StepSeconds, 4));
        bodies = new(physics);
        try
        {
            body = bodies.CreateBody(new(PhysicsBodyType.Dynamic,
                scale.ToMeters(Position.X), scale.ToMeters(Position.Y), FixedRotation: true));
            body.AddShape(PhysicsShapeDefinition.Box(halfWidth: .25f, halfHeight: .25f));
        }
        catch { bodies.Dispose(); physics.Dispose(); throw; }
    }

    public void Tick(ActionState input)
    {
        Vector2 direction = new(
            (input.IsDown(controls.Right) ? 1 : 0) - (input.IsDown(controls.Left) ? 1 : 0),
            (input.IsDown(controls.Down) ? 1 : 0) - (input.IsDown(controls.Up) ? 1 : 0));
        if (direction.LengthSquared() > 1) direction = Vector2.Normalize(direction);
        Vector2 velocity = direction * 160;
        Vector2 nextPosition;
        if (physics is not null && body is not null)
        {
            body.SetVelocity(scale.ToMeters(velocity.X), scale.ToMeters(velocity.Y));
            PhysicsStepResult step = physics.Step(); // Exactly one configured step.
            if (step.Dropped != 0) throw new InvalidOperationException("Physics events dropped; completed step must not be retried.");
            // Consume physics.Events here, before another Step/Dispose. Copy to retain.
            var pose = body.State; // Read one copied pose; meters -> pixels once.
            nextPosition = new(scale.ToPixels(pose.X), scale.ToPixels(pose.Y));
        }
        else nextPosition = Position + velocity * StepSeconds;
        previousPosition = Position;
        Position = nextPosition;
        if (input.IsPressed(controls.Pulse)) Pulses++;
        Steps++;
    }

    // Presentation only. Rules, queries and following fixed-step behaviors read Position.
    public Vector2 PresentationPosition(double alpha)
    {
        if (!double.IsFinite(alpha) || alpha < 0 || alpha > 1)
            throw new ArgumentOutOfRangeException(nameof(alpha));
        return Vector2.Lerp(previousPosition, Position, (float)alpha);
    }

    // Pause/focus loss/teleport/restart must not interpolate through stale history.
    public void SnapPresentation() => previousPosition = Position;

    public void Restart()
    {
        Position = Spawn;
        SnapPresentation();
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
