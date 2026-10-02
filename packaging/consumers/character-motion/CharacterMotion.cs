using System.Numerics;
using GameAuthoringLab;

namespace CharacterMotionRecipe;

// Copyable application policy for one capsule, one convex slope and one translating platform.
// This is a dynamic Box2D body; the ray query is not a sweep/slide collision controller.
internal sealed class CharacterMotion : IDisposable
{
    public const float StepSeconds = 1f / 60, Gravity = 900, Speed = 220, JumpHeight = 128;
    public const float Radius = 12, Height = 40, SlopeAngle = -MathF.PI / 9;
    private readonly PhysicsScale scale = new(32);
    private readonly PhysicsWorld physics;
    private readonly PhysicsScope bodies;
    private readonly PhysicsBody player, platform;
    private readonly JumpGrace jumpGrace = new(.1f);
    private ulong retainedSupport;
    private Vector2 previousPosition, previousPlatformPosition;
    public Vector2 Position { get; private set; } = new(160, 320);
    public Vector2 PlatformPosition { get; private set; } = new(950, 300);
    public uint Steps { get; private set; }
    public bool Paused { get; private set; }
    public bool Grounded => ReadSupport(player.State).Supported;
    internal GroundSupport Support => ReadSupport(player.State);
    internal ulong RetainedSupportBody => retainedSupport;
    internal ulong PlatformBodyId => platform.Id;
    public Vector2 Velocity { get { var s = player.State; return new(scale.ToPixels(s.Vx), scale.ToPixels(s.Vy)); } }

    public CharacterMotion(EngineHost engine)
    {
        physics = engine.OpenPhysics(new(0, scale.ToMeters(Gravity), StepSeconds, 4));
        bodies = new(physics);
        try
        {
            AddBox(new(600, 500), new(1200, 40));
            AddBox(new(350, 240), new(400, 20), angle: SlopeAngle);
            platform = AddBox(PlatformPosition, new(160, 20), PhysicsBodyType.Kinematic);
            // Deliberate choice: caller owns airborne gravity and supported idle/tangent motion.
            player = bodies.CreateBody(new(PhysicsBodyType.Dynamic,
                scale.ToMeters(Position.X), scale.ToMeters(Position.Y), GravityScale: 0, FixedRotation: true));
            float segment = scale.ToMeters(Height / 2 - Radius);
            player.AddCapsule(new(0, -segment, 0, segment, scale.ToMeters(Radius),
                Friction: 0, Category: 2, Mask: 1));
        }
        catch { bodies.Dispose(); physics.Dispose(); throw; }
        SnapPresentation();
    }

    private PhysicsBody AddBox(Vector2 center, Vector2 size,
        PhysicsBodyType type = PhysicsBodyType.Static, float angle = 0)
    {
        var body = bodies.CreateBody(new(type, scale.ToMeters(center.X), scale.ToMeters(center.Y), Angle: angle));
        body.AddShape(PhysicsShapeDefinition.Box( scale.ToMeters(size.X / 2), scale.ToMeters(size.Y / 2),
            friction: 0, category: 1, mask: 2));
        return body;
    }

    private GroundSupport ReadSupport(PhysicsBodyStateView state) => GroundSupportProbe.Read(physics, state,
        scale.ToMeters(Radius), scale.ToMeters(Height / 2), scale.ToMeters(2), SupportVelocity, retainedSupport);
    private Vector2 SupportVelocity(ulong id)
    {
        if (id != platform.Id) return Vector2.Zero; // Every other support here is static.
        var state = platform.State;
        return new(state.Vx, state.Vy);
    }

    public void Tick(float axis = 0, bool jumpPressed = false)
    {
        if (Paused) return;
        var state = player.State;
        var support = ReadSupport(state);
        bool jumping = jumpGrace.Step(support.Supported, jumpPressed, StepSeconds);
        float speed = scale.ToMeters(Math.Clamp(axis, -1, 1) * Speed);
        Vector2 velocity = new(speed, state.Vy + scale.ToMeters(Gravity) * StepSeconds);
        if (support.Supported)
        {
            // Constant tangent speed + support translation. No automatic platform leave momentum.
            Vector2 tangent = new(-support.Normal.Y, support.Normal.X);
            velocity = tangent * speed + support.Velocity;
        }
        if (jumping)
            velocity.Y = scale.ToMeters(-MathF.Sqrt(2 * Gravity * JumpHeight)) + support.Velocity.Y;
        retainedSupport = !jumping && support.Supported ? support.Body : 0;
        player.SetVelocity(velocity.X, velocity.Y);
        if (physics.Step().Dropped != 0)
            throw new InvalidOperationException("Physics events dropped; the completed step must not be retried.");
        Steps++;
        // One authoritative post-step pose per moving body. Physics owns position; drawing never writes back.
        var pose = player.State;
        previousPosition = Position;
        Position = new(scale.ToPixels(pose.X), scale.ToPixels(pose.Y));
        var platformPose = platform.State;
        previousPlatformPosition = PlatformPosition;
        PlatformPosition = new(scale.ToPixels(platformPose.X), scale.ToPixels(platformPose.Y));
    }

    // Set before Tick, so the query sees this step's requested support velocity, including reversal.
    public void SetPlatformVelocity(Vector2 pixelsPerSecond) =>
        platform.SetVelocity(scale.ToMeters(pixelsPerSecond.X), scale.ToMeters(pixelsPerSecond.Y));

    public void Teleport(Vector2 position)
    {
        player.Teleport(scale.ToMeters(position.X), scale.ToMeters(position.Y));
        player.SetVelocity(0, 0);
        retainedSupport = 0;
        jumpGrace.Reset();
        Position = position;
        SnapPresentation();
    }

    public void SetPaused(bool paused) { Paused = paused; if (paused) Suspend(); }
    // The outer host also calls this on focus loss/no drawable area and resets its fixed-step input/debt.
    public void Suspend() { jumpGrace.Reset(); SnapPresentation(); }
    public void SnapPresentation() { previousPosition = Position; previousPlatformPosition = PlatformPosition; }
    public Vector2 PresentationPosition(double alpha) => Interpolate(previousPosition, Position, alpha);
    public Vector2 PresentationPlatformPosition(double alpha) => Interpolate(previousPlatformPosition, PlatformPosition, alpha);
    private static Vector2 Interpolate(Vector2 previous, Vector2 current, double alpha)
    {
        if (!double.IsFinite(alpha) || alpha < 0 || alpha > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
        return Vector2.Lerp(previous, current, (float)alpha);
    }

    public void Dispose() { bodies.Dispose(); physics.Dispose(); }
}
