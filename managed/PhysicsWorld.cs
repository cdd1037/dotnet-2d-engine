namespace GameAuthoringLab;
/// <summary>Fixed-step settings: gravity in meters/second squared, step duration in seconds and 1..8 solver substeps.</summary>
public readonly record struct PhysicsSettings(float GravityX=0,float GravityY=9.8f,float StepSeconds=1f/60,uint Substeps=4)
{
    public static PhysicsSettings Default=>new(0,9.8f,1f/60,4);
    internal unsafe PhysicsConfig NativeValue(){Validate();return new(){Size=(uint)sizeof(PhysicsConfig),Version=1,GravityX=GravityX,GravityY=GravityY,StepSeconds=StepSeconds,Substeps=Substeps};}
    public void Validate(){PhysicsLimits.Bounded(GravityX,1000);PhysicsLimits.Bounded(GravityY,1000);PhysicsLimits.Range(StepSeconds,1f/240,1f/15);if(Substeps is <1 or >8)throw new ArgumentOutOfRangeException(nameof(Substeps));}
}
/// <summary>Initial body pose and velocity in meters, seconds and radians. Bullet mode is valid only for dynamic bodies.</summary>
public readonly record struct PhysicsBodyDefinition(PhysicsBodyType Type,float X=0,float Y=0,float Angle=0,float Vx=0,float Vy=0,float AngularVelocity=0,float GravityScale=1,float LinearDamping=0,float AngularDamping=0,bool FixedRotation=false,bool Bullet=false)
{
    internal unsafe PhysicsBodyDef NativeValue(){Validate();return new(){Size=(uint)sizeof(PhysicsBodyDef),Version=1,Type=Type,Flags=(FixedRotation?1u:0)|(Bullet?2u:0),X=X,Y=Y,Angle=Angle,Vx=Vx,Vy=Vy,AngularVelocity=AngularVelocity,GravityScale=GravityScale,LinearDamping=LinearDamping,AngularDamping=AngularDamping};}
    public void Validate()
    {
        if(Type>PhysicsBodyType.Dynamic||(Type!=PhysicsBodyType.Dynamic&&Bullet)||(Type==PhysicsBodyType.Static&&(Vx!=0||Vy!=0||AngularVelocity!=0)))throw new ArgumentOutOfRangeException(nameof(Type));
        PhysicsLimits.Bounded(X,10000);PhysicsLimits.Bounded(Y,10000);PhysicsLimits.Bounded(Angle,10000);PhysicsLimits.Bounded(Vx,1000);PhysicsLimits.Bounded(Vy,1000);PhysicsLimits.Bounded(AngularVelocity,100);PhysicsLimits.Bounded(GravityScale,100);PhysicsLimits.Range(LinearDamping,0,100);PhysicsLimits.Range(AngularDamping,0,100);
    }
}
/// <summary>Circle A is its radius, with B and Angle zero; box A/B are half extents. Lengths are meters, local angles radians and density kg/m².</summary>
public readonly record struct PhysicsShapeDefinition(PhysicsShapeType Type,float A,float B=0,float OffsetX=0,float OffsetY=0,float Angle=0,float Density=1,float Friction=.6f,float Restitution=0,ulong Category=1,ulong Mask=ulong.MaxValue,int Group=0,bool Sensor=false)
{
    internal unsafe PhysicsShapeDef NativeValue(){Validate();return new(){Size=(uint)sizeof(PhysicsShapeDef),Version=1,Type=Type,Flags=Sensor?1u:0,OffsetX=OffsetX,OffsetY=OffsetY,Angle=Angle,A=A,B=B,Density=Density,Friction=Friction,Restitution=Restitution,Category=Category,Mask=Mask,Group=Group};}
    public void Validate()
    {
        if(Type>PhysicsShapeType.Box||(Type==PhysicsShapeType.Circle&&(B!=0||Angle!=0)))throw new ArgumentOutOfRangeException(nameof(Type));
        PhysicsLimits.Range(A,.001f,100);if(Type==PhysicsShapeType.Box)PhysicsLimits.Range(B,.001f,100);
        PhysicsLimits.Bounded(OffsetX,100);PhysicsLimits.Bounded(OffsetY,100);PhysicsLimits.Bounded(Angle,10000);PhysicsLimits.Range(Density,0,10000);PhysicsLimits.Range(Friction,0,10);PhysicsLimits.Range(Restitution,0,1);
    }
}
/// <summary>A capsule defined by two body-local segment endpoints and a radius, all in meters. Endpoints must be at least .01 meters apart. Density is kg/m²; sensors contribute mass when density is nonzero.</summary>
public readonly record struct PhysicsCapsuleDefinition(float X1,float Y1,float X2,float Y2,float Radius,float Density=1,float Friction=.6f,float Restitution=0,ulong Category=1,ulong Mask=ulong.MaxValue,int Group=0,bool Sensor=false)
{
    internal unsafe PhysicsCapsuleDef NativeValue()
    {
        Validate();
        return new(){Size=(uint)sizeof(PhysicsCapsuleDef),Version=1,Flags=Sensor?1u:0,X1=X1,Y1=Y1,X2=X2,Y2=Y2,Radius=Radius,Density=Density,Friction=Friction,Restitution=Restitution,Category=Category,Mask=Mask,Group=Group};
    }
    public void Validate()
    {
        PhysicsLimits.Bounded(X1,100);PhysicsLimits.Bounded(Y1,100);PhysicsLimits.Bounded(X2,100);PhysicsLimits.Bounded(Y2,100);
        double dx=(double)X2-X1,dy=(double)Y2-Y1,minimum=.01f;
        if(dx*dx+dy*dy<minimum*minimum)throw new ArgumentOutOfRangeException(nameof(X2),"Capsule endpoints must be at least .01 meters apart.");
        PhysicsLimits.Range(Radius,.001f,100);PhysicsLimits.Range(Density,0,10000);PhysicsLimits.Range(Friction,0,10);PhysicsLimits.Range(Restitution,0,1);
    }
}
internal static class PhysicsLimits
{
    internal static void Bounded(float value,float limit){if(!float.IsFinite(value)||Math.Abs(value)>limit)throw new ArgumentOutOfRangeException(nameof(value),$"Expected finite value within +/-{limit}.");}
    internal static void Range(float value,float lower,float upper){if(!float.IsFinite(value)||value<lower||value>upper)throw new ArgumentOutOfRangeException(nameof(value),$"Expected finite value in [{lower},{upper}].");}
}
/// <summary>Explicit checked boundary conversion. No automatic entity transform writes or axis inversion.</summary>
public readonly record struct PhysicsScale
{
    public float PixelsPerMeter{get;}
    public PhysicsScale(float pixelsPerMeter){PhysicsLimits.Range(pixelsPerMeter,.01f,10000);PixelsPerMeter=pixelsPerMeter;}
    public float ToMeters(float pixels)=>Convert(pixels,1d/PixelsPerMeter);
    public float ToPixels(float meters)=>Convert(meters,PixelsPerMeter);
    private float Convert(float value,double multiplier)
    {if(PixelsPerMeter<=0||!float.IsFinite(value)||!float.IsFinite((float)(value*multiplier)))throw new ArgumentOutOfRangeException(nameof(value));return (float)(value*multiplier);}
}
/// <summary>One explicit fixed-step world per engine. Use and dispose it on the engine's creating thread, outside draw frames.</summary>
public sealed unsafe class PhysicsWorld : IDisposable, IEngineOwned
{
    private readonly EngineHost _engine;
    private readonly PhysicsSettings _settings;
    private readonly PhysicsEvent[] _events = new PhysicsEvent[1024];
    private readonly PhysicsEventView[] _eventViews = new PhysicsEventView[1024];
    private PhysicsStepResult _lastStep;
    private int _eventCount;
    private bool _closed;

    public PhysicsSettings Settings { get { _ = Context; return _settings; } }
    public PhysicsStepResult LastStep { get { _ = Context; return _lastStep; } }
    internal nint Context { get { _engine.AssertAlive(); ObjectDisposedException.ThrowIf(_closed, this); return _engine.NativeContext; } }
    internal void AssertThread() => _engine.AssertThread();

    internal PhysicsWorld(EngineHost engine, PhysicsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(engine);
        engine.AssertAlive();
        _engine = engine;
        _settings = settings;
        var config = settings.NativeValue();
        Native.Check(PhysicsNative.Open(engine.NativeContext, &config), "open physics world");
    }

    public PhysicsBody CreateBody(PhysicsBodyDefinition definition)
    {
        nint context = Context;
        var def = definition.NativeValue();
        ulong id = 0;
        Native.Check(PhysicsNative.CreateBody(context, &def, &id), "create physics body");
        return new(this, id);
    }

    public PhysicsStateView State
    {
        get
        {
            var state = new PhysicsState { Size = (uint)sizeof(PhysicsState) };
            Native.Check(PhysicsNative.State(Context, &state), "physics state");
            return PhysicsStateView.FromNative(state);
        }
    }

    /// <summary>Reusable copied event storage, valid until the next Step or world/engine close. Copy records to retain them longer.</summary>
    public ReadOnlySpan<PhysicsEventView> Events { get { _ = Context; return _eventViews.AsSpan(0, _eventCount); } }

    /// <summary>Advances exactly one configured fixed step and copies at most 1,024 events without allocating managed storage.</summary>
    public PhysicsStepResult Step()
    {
        nint context = Context;
        var result = new PhysicsStep { Size = (uint)sizeof(PhysicsStep) };
        Native.Check(PhysicsNative.Step(context, &result), "fixed physics step");
        _lastStep = PhysicsStepResult.FromNative(result);
        _eventCount = 0;
        uint count = 0;
        fixed (PhysicsEvent* events = _events)
            Native.Check(PhysicsNative.Events(context, events, (uint)_events.Length, &count), "copy physics events");
        for (int i = 0; i < count; i++)
            _eventViews[i] = PhysicsEventView.FromNative(_events[i]);
        _eventCount = (int)count;
        return _lastStep;
    }

    /// <summary>Casts a nonzero displacement in meters and returns the closest filtered hit, ignoring initial overlap.</summary>
    public PhysicsRayHitView RayCast(float x, float y, float dx, float dy, ulong category = ulong.MaxValue, ulong mask = ulong.MaxValue)
    {
        nint context = Context;
        PhysicsLimits.Bounded(x, 10000);
        PhysicsLimits.Bounded(y, 10000);
        PhysicsLimits.Bounded(dx, 20000);
        PhysicsLimits.Bounded(dy, 20000);
        if (dx == 0 && dy == 0) throw new ArgumentException("Ray displacement must be nonzero.", nameof(dx));
        var query = new PhysicsRay { Size = (uint)sizeof(PhysicsRay), Version = 1, X = x, Y = y, Dx = dx, Dy = dy, Category = category, Mask = mask };
        var result = new PhysicsRayHit { Size = (uint)sizeof(PhysicsRayHit) };
        Native.Check(PhysicsNative.Ray(context, &query, &result), "physics closest ray");
        return PhysicsRayHitView.FromNative(result);
    }

    /// <summary>Writes sorted shape IDs for a broad-phase AABB query in meters. Capacity is at most 512; insufficient capacity leaves the output unchanged.</summary>
    public int QueryAabb(float lowerX, float lowerY, float upperX, float upperY, Span<ulong> shapes, ulong category = ulong.MaxValue, ulong mask = ulong.MaxValue)
    {
        nint context = Context;
        PhysicsLimits.Bounded(lowerX, 10000);
        PhysicsLimits.Bounded(lowerY, 10000);
        PhysicsLimits.Bounded(upperX, 10000);
        PhysicsLimits.Bounded(upperY, 10000);
        if (lowerX > upperX) throw new ArgumentException("Upper X must be at least lower X.", nameof(upperX));
        if (lowerY > upperY) throw new ArgumentException("Upper Y must be at least lower Y.", nameof(upperY));
        if (shapes.Length > 512) throw new ArgumentOutOfRangeException(nameof(shapes), "Query capacity must not exceed 512 shape IDs.");
        var query = new PhysicsAabb { Size = (uint)sizeof(PhysicsAabb), Version = 1, LowerX = lowerX, LowerY = lowerY, UpperX = upperX, UpperY = upperY, Category = category, Mask = mask };
        uint count = 0;
        fixed (ulong* output = shapes)
            Native.Check(PhysicsNative.Aabb(context, &query, output, (uint)shapes.Length, &count), "physics broad-phase AABB");
        return (int)count;
    }

    /// <summary>Writes sorted stable shape IDs overlapping a world-space circle in meters using Box2D narrow-phase geometry (including its .0005-meter tolerance). Includes sensors by default; category/mask filtering is reciprocal and ignores collision groups. Capacity is at most 512; insufficient capacity leaves the span unchanged.</summary>
    public int QueryCircle(float x, float y, float radius, Span<ulong> shapes, ulong category = ulong.MaxValue, ulong mask = ulong.MaxValue, PhysicsSensorQuery sensors = PhysicsSensorQuery.Include)
        => QueryOverlap(PhysicsShapeType.Circle, x, y, radius, 0, 0, shapes, category, mask, sensors);

    /// <summary>Writes sorted stable shape IDs overlapping a world-space rotated box using Box2D narrow-phase geometry (including its .0005-meter tolerance). Center and positive half extents are meters; angle is radians. Includes sensors by default; category/mask filtering is reciprocal and ignores collision groups. Capacity is at most 512; insufficient capacity leaves the span unchanged.</summary>
    public int QueryBox(float x, float y, float halfWidth, float halfHeight, float angle, Span<ulong> shapes, ulong category = ulong.MaxValue, ulong mask = ulong.MaxValue, PhysicsSensorQuery sensors = PhysicsSensorQuery.Include)
        => QueryOverlap(PhysicsShapeType.Box, x, y, halfWidth, halfHeight, angle, shapes, category, mask, sensors);

    private int QueryOverlap(PhysicsShapeType type, float x, float y, float a, float b, float angle, Span<ulong> shapes, ulong category, ulong mask, PhysicsSensorQuery sensors)
    {
        nint context = Context;
        PhysicsLimits.Bounded(x, 10000);
        PhysicsLimits.Bounded(y, 10000);
        PhysicsLimits.Range(a, .001f, 100);
        if (type == PhysicsShapeType.Box) PhysicsLimits.Range(b, .001f, 100);
        PhysicsLimits.Bounded(angle, 10000);
        if (sensors > PhysicsSensorQuery.Only) throw new ArgumentOutOfRangeException(nameof(sensors));
        if (shapes.Length > 512) throw new ArgumentOutOfRangeException(nameof(shapes), "Query capacity must not exceed 512 shape IDs.");
        var query = new PhysicsOverlapQuery { Size = (uint)sizeof(PhysicsOverlapQuery), Version = 1, Type = type, Flags = (uint)sensors, X = x, Y = y, A = a, B = b, Angle = angle, Category = category, Mask = mask };
        uint count = 0;
        fixed (ulong* output = shapes)
        {
            int status = PhysicsNative.Overlap(context, &query, output, (uint)shapes.Length, &count);
            // Native validation and collection precede the copy. Include the required
            // size on failure without allocating anything on the successful path.
            if (status != 0 && count > shapes.Length)
                throw new ArgumentException($"Query output requires {count} shape IDs; capacity is {shapes.Length}.", nameof(shapes));
            Native.Check(status, "physics exact overlap query");
        }
        return (int)count;
    }

    internal void ReleaseBody(ulong id) { AssertThread(); if (!_closed) Native.Check(PhysicsNative.ReleaseBody(Context, id), "release physics body"); }
    internal void ReleaseShape(ulong id) { AssertThread(); if (!_closed) Native.Check(PhysicsNative.ReleaseShape(Context, id), "release physics shape"); }
    void IEngineOwned.EngineDestroyed() => EngineDestroyed();
    internal void EngineDestroyed()
    {
        _closed = true;
        _eventCount = 0;
        Array.Clear(_eventViews);
    }

    public void Dispose()
    {
        AssertThread();
        if (_closed) return;
        Native.Check(PhysicsNative.Close(Context), "close physics world");
        EngineDestroyed();
        _engine.PhysicsClosed(this);
    }
}

/// <summary>A world-owned body with pose in meters/radians, velocity in meters/second and angular velocity in radians/second.</summary>
public sealed unsafe class PhysicsBody : IDisposable
{
    private readonly PhysicsWorld _world;
    private readonly ulong _id;
    private bool _disposed;
    internal bool Disposed => _disposed;
    internal PhysicsWorld World => _world;
    internal nint Context { get { _world.AssertThread(); ObjectDisposedException.ThrowIf(_disposed, this); return _world.Context; } }

    internal PhysicsBody(PhysicsWorld world, ulong id)
    {
        ArgumentNullException.ThrowIfNull(world);
        _ = world.Context;
        if (id == 0) throw new ArgumentOutOfRangeException(nameof(id));
        _world = world;
        _id = id;
    }

    public ulong Id { get { _ = Context; return _id; } }
    public PhysicsBodyStateView State
    {
        get
        {
            var state = new PhysicsBodyState { Size = (uint)sizeof(PhysicsBodyState) };
            Native.Check(PhysicsNative.GetBody(Context, _id, &state), "physics body state");
            return PhysicsBodyStateView.FromNative(state);
        }
    }

    public PhysicsShape AddShape(PhysicsShapeDefinition definition)
    {
        nint context = Context;
        var def = definition.NativeValue();
        ulong shape = 0;
        Native.Check(PhysicsNative.CreateShape(context, _id, &def, &shape), "create physics shape");
        return new(this, shape);
    }

    /// <summary>Adds a capsule from body-local endpoints and a radius in meters. The body's pose transforms the capsule; its wrapper follows ordinary shape ownership.</summary>
    public PhysicsShape AddCapsule(PhysicsCapsuleDefinition definition)
    {
        nint context = Context;
        var def = definition.NativeValue();
        ulong shape = 0;
        Native.Check(PhysicsNative.CreateCapsule(context, _id, &def, &shape), "create physics capsule");
        return new(this, shape);
    }

    public void Teleport(float x, float y, float angle = 0) => Command(1, x, y, angle);
    public void SetVelocity(float x, float y, float angular = 0) => Command(2, x, y, angular);
    /// <summary>Applies a center impulse in kg·m/s to a dynamic body, affecting velocity immediately.</summary>
    public void ApplyImpulse(float x, float y) => Command(3, x, y, 0);
    /// <summary>Applies a center force in newtons to a dynamic body for the next step.</summary>
    public void ApplyForce(float x, float y) => Command(4, x, y, 0);
    private void Command(uint command, float x, float y, float z)
    {
        nint context = Context;
        PhysicsLimits.Bounded(x, command == 1 ? 10000 : 1000);
        PhysicsLimits.Bounded(y, command == 1 ? 10000 : 1000);
        PhysicsLimits.Bounded(z, command == 1 ? 10000 : 100);
        Native.Check(PhysicsNative.Command(context, _id, command, x, y, z), "physics body command");
    }

    public void Dispose()
    {
        _world.AssertThread();
        if (_disposed) return;
        _world.ReleaseBody(_id);
        _disposed = true;
    }
}

/// <summary>A shape owned by its body. Disposing the body also invalidates this shape.</summary>
public sealed class PhysicsShape : IDisposable
{
    private readonly PhysicsBody _body;
    private readonly ulong _id;
    private bool _disposed;

    internal PhysicsShape(PhysicsBody body, ulong id)
    {
        ArgumentNullException.ThrowIfNull(body);
        _ = body.Context;
        if (id == 0) throw new ArgumentOutOfRangeException(nameof(id));
        _body = body;
        _id = id;
    }

    public ulong Id { get { _ = _body.Context; ObjectDisposedException.ThrowIf(_disposed, this); return _id; } }
    public void Dispose()
    {
        _body.World.AssertThread();
        if (_disposed) return;
        if (!_body.Disposed) _body.World.ReleaseShape(_id);
        _disposed = true;
    }
}

/// <summary>Explicit body ownership for scene cleanup. Dispose on the world's creating thread.</summary>
public sealed class PhysicsScope : IDisposable
{
    private readonly PhysicsWorld _world;
    private readonly List<PhysicsBody> _bodies = [];
    private bool _disposed;

    public PhysicsScope(PhysicsWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _ = world.Context;
        _world = world;
    }

    public PhysicsBody CreateBody(PhysicsBodyDefinition definition)
    {
        _world.AssertThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var body = _world.CreateBody(definition);
        _bodies.Add(body);
        return body;
    }

    public void Dispose()
    {
        _world.AssertThread();
        if (_disposed) return;
        for (int i = _bodies.Count - 1; i >= 0; i--) _bodies[i].Dispose();
        _bodies.Clear();
        _disposed = true;
    }
}
