namespace GameAuthoringLab;
internal readonly record struct PhysicsSettings(float GravityX=0,float GravityY=9.8f,float StepSeconds=1f/60,uint Substeps=4)
{
    public static PhysicsSettings Default=>new(0,9.8f,1f/60,4);
    internal unsafe PhysicsConfig NativeValue(){Validate();return new(){Size=(uint)sizeof(PhysicsConfig),Version=1,GravityX=GravityX,GravityY=GravityY,StepSeconds=StepSeconds,Substeps=Substeps};}
    public void Validate(){PhysicsLimits.Bounded(GravityX,1000);PhysicsLimits.Bounded(GravityY,1000);PhysicsLimits.Range(StepSeconds,1f/240,1f/15);if(Substeps is <1 or >8)throw new ArgumentOutOfRangeException(nameof(Substeps));}
}
internal readonly record struct PhysicsBodyDefinition(PhysicsBodyType Type,float X=0,float Y=0,float Angle=0,float Vx=0,float Vy=0,float AngularVelocity=0,float GravityScale=1,float LinearDamping=0,float AngularDamping=0,bool FixedRotation=false,bool Bullet=false)
{
    internal unsafe PhysicsBodyDef NativeValue(){Validate();return new(){Size=(uint)sizeof(PhysicsBodyDef),Version=1,Type=Type,Flags=(FixedRotation?1u:0)|(Bullet?2u:0),X=X,Y=Y,Angle=Angle,Vx=Vx,Vy=Vy,AngularVelocity=AngularVelocity,GravityScale=GravityScale,LinearDamping=LinearDamping,AngularDamping=AngularDamping};}
    public void Validate()
    {
        if(Type>PhysicsBodyType.Dynamic||(Type!=PhysicsBodyType.Dynamic&&Bullet)||(Type==PhysicsBodyType.Static&&(Vx!=0||Vy!=0||AngularVelocity!=0)))throw new ArgumentOutOfRangeException(nameof(Type));
        PhysicsLimits.Bounded(X,10000);PhysicsLimits.Bounded(Y,10000);PhysicsLimits.Bounded(Angle,10000);PhysicsLimits.Bounded(Vx,1000);PhysicsLimits.Bounded(Vy,1000);PhysicsLimits.Bounded(AngularVelocity,100);PhysicsLimits.Bounded(GravityScale,100);PhysicsLimits.Range(LinearDamping,0,100);PhysicsLimits.Range(AngularDamping,0,100);
    }
}
internal readonly record struct PhysicsShapeDefinition(PhysicsShapeType Type,float A,float B=0,float OffsetX=0,float OffsetY=0,float Angle=0,float Density=1,float Friction=.6f,float Restitution=0,ulong Category=1,ulong Mask=ulong.MaxValue,int Group=0,bool Sensor=false)
{
    internal unsafe PhysicsShapeDef NativeValue(){Validate();return new(){Size=(uint)sizeof(PhysicsShapeDef),Version=1,Type=Type,Flags=Sensor?1u:0,OffsetX=OffsetX,OffsetY=OffsetY,Angle=Angle,A=A,B=B,Density=Density,Friction=Friction,Restitution=Restitution,Category=Category,Mask=Mask,Group=Group};}
    public void Validate()
    {
        if(Type>PhysicsShapeType.Box||(Type==PhysicsShapeType.Circle&&(B!=0||Angle!=0)))throw new ArgumentOutOfRangeException(nameof(Type));
        PhysicsLimits.Range(A,.001f,100);if(Type==PhysicsShapeType.Box)PhysicsLimits.Range(B,.001f,100);
        PhysicsLimits.Bounded(OffsetX,100);PhysicsLimits.Bounded(OffsetY,100);PhysicsLimits.Bounded(Angle,10000);PhysicsLimits.Range(Density,0,10000);PhysicsLimits.Range(Friction,0,10);PhysicsLimits.Range(Restitution,0,1);
    }
}
internal static class PhysicsLimits
{
    internal static void Bounded(float value,float limit){if(!float.IsFinite(value)||Math.Abs(value)>limit)throw new ArgumentOutOfRangeException(nameof(value),$"Expected finite value within +/-{limit}.");}
    internal static void Range(float value,float lower,float upper){if(!float.IsFinite(value)||value<lower||value>upper)throw new ArgumentOutOfRangeException(nameof(value),$"Expected finite value in [{lower},{upper}].");}
}
/// <summary>Explicit checked boundary conversion. No automatic entity transform writes or axis inversion.</summary>
internal readonly record struct PhysicsScale
{
    public float PixelsPerMeter{get;}
    public PhysicsScale(float pixelsPerMeter){PhysicsLimits.Range(pixelsPerMeter,.01f,10000);PixelsPerMeter=pixelsPerMeter;}
    public float ToMeters(float pixels)=>Convert(pixels,1d/PixelsPerMeter);
    public float ToPixels(float meters)=>Convert(meters,PixelsPerMeter);
    private float Convert(float value,double multiplier)
    {if(PixelsPerMeter<=0||!float.IsFinite(value)||!float.IsFinite((float)(value*multiplier)))throw new ArgumentOutOfRangeException(nameof(value));return (float)(value*multiplier);}
}
internal sealed unsafe class PhysicsWorld : IDisposable , IEngineOwned
{
    private readonly EngineHost _engine;
    private readonly PhysicsEvent[] _events=new PhysicsEvent[1024];
    private int _eventCount;
    private bool _closed;
    public PhysicsSettings Settings{get;}
    public PhysicsStep LastStep{get;private set;}
    internal nint Context{get{_engine.AssertAlive();ObjectDisposedException.ThrowIf(_closed,this);return _engine.NativeContext;}}
    internal PhysicsWorld(EngineHost engine,PhysicsSettings settings){_engine=engine;Settings=settings;var config=settings.NativeValue();Native.Check(PhysicsNative.Open(engine.NativeContext,&config),"open physics world");}
    public PhysicsBody CreateBody(PhysicsBodyDefinition definition){var def=definition.NativeValue();ulong id=0;Native.Check(PhysicsNative.CreateBody(Context,&def,&id),"create physics body");return new(this,id);}
    public PhysicsState State{get{var state=new PhysicsState{Size=(uint)sizeof(PhysicsState)};Native.Check(PhysicsNative.State(Context,&state),"physics state");return state;}}
    public ReadOnlySpan<PhysicsEvent> Events{get{_ =Context;return _events.AsSpan(0,_eventCount);}}
    public PhysicsStep Step()
    {
        var result=new PhysicsStep{Size=(uint)sizeof(PhysicsStep)};Native.Check(PhysicsNative.Step(Context,&result),"fixed physics step");
        uint count=0;fixed(PhysicsEvent* events=_events)Native.Check(PhysicsNative.Events(Context,events,(uint)_events.Length,&count),"copy physics events");
        _eventCount=(int)count;LastStep=result;return result;
    }
    public PhysicsRayHit RayCast(float x,float y,float dx,float dy,ulong category=ulong.MaxValue,ulong mask=ulong.MaxValue)
    {
        var query=new PhysicsRay{Size=(uint)sizeof(PhysicsRay),Version=1,X=x,Y=y,Dx=dx,Dy=dy,Category=category,Mask=mask};var result=new PhysicsRayHit{Size=(uint)sizeof(PhysicsRayHit)};
        Native.Check(PhysicsNative.Ray(Context,&query,&result),"physics closest ray");return result;
    }
    public int QueryAabb(float lowerX,float lowerY,float upperX,float upperY,Span<ulong> shapes,ulong category=ulong.MaxValue,ulong mask=ulong.MaxValue)
    {
        var query=new PhysicsAabb{Size=(uint)sizeof(PhysicsAabb),Version=1,LowerX=lowerX,LowerY=lowerY,UpperX=upperX,UpperY=upperY,Category=category,Mask=mask};uint count=0;
        fixed(ulong* output=shapes)Native.Check(PhysicsNative.Aabb(Context,&query,output,(uint)shapes.Length,&count),"physics broad-phase AABB");return (int)count;
    }
    internal void ReleaseBody(ulong id){_engine.AssertThread();if(!_closed)Native.Check(PhysicsNative.ReleaseBody(Context,id),"release physics body");}
    internal void ReleaseShape(ulong id){_engine.AssertThread();if(!_closed)Native.Check(PhysicsNative.ReleaseShape(Context,id),"release physics shape");}
    void IEngineOwned.EngineDestroyed()=>EngineDestroyed();
    internal void EngineDestroyed()=>_closed=true;
    public void Dispose(){_engine.AssertThread();if(_closed)return;Native.Check(PhysicsNative.Close(Context),"close physics world");_closed=true;_eventCount=0;_engine.PhysicsClosed(this);}
}
internal sealed unsafe class PhysicsBody(PhysicsWorld world,ulong id) : IDisposable
{
    private bool _disposed;
    internal bool Disposed=>_disposed;
    internal PhysicsWorld World=>world;
    internal nint Context{get{ObjectDisposedException.ThrowIf(_disposed,this);return world.Context;}}
    public ulong Id{get{_ =Context;return id;}}
    public PhysicsBodyState State{get{var state=new PhysicsBodyState{Size=(uint)sizeof(PhysicsBodyState)};Native.Check(PhysicsNative.GetBody(Context,id,&state),"physics body state");return state;}}
    public PhysicsShape AddShape(PhysicsShapeDefinition definition){var def=definition.NativeValue();ulong shape=0;Native.Check(PhysicsNative.CreateShape(Context,id,&def,&shape),"create physics shape");return new(this,shape);}
    public void Teleport(float x,float y,float angle=0)=>Command(1,x,y,angle);
    public void SetVelocity(float x,float y,float angular=0)=>Command(2,x,y,angular);
    public void ApplyImpulse(float x,float y)=>Command(3,x,y,0);
    public void ApplyForce(float x,float y)=>Command(4,x,y,0);
    private void Command(uint command,float x,float y,float z)=>Native.Check(PhysicsNative.Command(Context,id,command,x,y,z),"physics body command");
    public void Dispose(){if(_disposed)return;world.ReleaseBody(id);_disposed=true;}
}
internal sealed class PhysicsShape(PhysicsBody body,ulong id) : IDisposable
{
    private bool _disposed;
    public ulong Id{get{_ =body.Context;ObjectDisposedException.ThrowIf(_disposed,this);return id;}}
    public void Dispose(){if(_disposed)return;if(!body.Disposed)body.World.ReleaseShape(id);_disposed=true;}
}
internal sealed class PhysicsScope(PhysicsWorld world) : IDisposable
{
    private readonly List<PhysicsBody> _bodies=[];private bool _disposed;
    public PhysicsBody CreateBody(PhysicsBodyDefinition definition){ObjectDisposedException.ThrowIf(_disposed,this);var body=world.CreateBody(definition);_bodies.Add(body);return body;}
    public void Dispose(){if(_disposed)return;for(int i=_bodies.Count-1;i>=0;i--)_bodies[i].Dispose();_bodies.Clear();_disposed=true;}
}
