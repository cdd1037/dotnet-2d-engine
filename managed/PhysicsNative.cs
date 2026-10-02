using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace GameAuthoringLab;
[StructLayout(LayoutKind.Sequential)]
internal struct PhysicsConfig {public uint Size,Version;public float GravityX,GravityY,StepSeconds;public uint Substeps,Flags,Reserved;}
[StructLayout(LayoutKind.Sequential)]
internal struct PhysicsBodyDef {public uint Size,Version;public PhysicsBodyType Type;public uint Flags;public float X,Y,Angle,Vx,Vy,AngularVelocity,GravityScale,LinearDamping,AngularDamping;public uint Reserved;}
[StructLayout(LayoutKind.Sequential)]
internal struct PhysicsShapeDef {public uint Size,Version;public PhysicsShapeType Type;public uint Flags;public float OffsetX,OffsetY,Angle,A,B,Density,Friction,Restitution;public ulong Category,Mask;public int Group;public uint Reserved;}
[StructLayout(LayoutKind.Sequential)]
internal struct PhysicsCapsuleDef {public uint Size,Version,Flags,Reserved;public float X1,Y1,X2,Y2,Radius,Density,Friction,Restitution;public ulong Category,Mask;public int Group;public uint Reserved2;}
[StructLayout(LayoutKind.Sequential)]
internal struct PhysicsOverlapQuery {public uint Size,Version;public PhysicsShapeType Type;public uint Flags;public float X,Y,A,B,Angle;public uint Reserved;public ulong Category,Mask;}
[StructLayout(LayoutKind.Sequential)]
internal struct PhysicsBodyState {public uint Size,Flags;public float X,Y,Angle,Vx,Vy,AngularVelocity;public PhysicsBodyType Type;public uint Reserved;public readonly bool Awake=>(Flags&1)!=0;}
[StructLayout(LayoutKind.Sequential)]
internal struct PhysicsStep {public uint Size,Events,Dropped,Index;}
[StructLayout(LayoutKind.Sequential)]
internal struct PhysicsEvent {public uint Type,Flags;public ulong ShapeA,ShapeB,BodyA,BodyB;}
[StructLayout(LayoutKind.Sequential)]
internal struct PhysicsRay {public uint Size,Version;public float X,Y,Dx,Dy;public ulong Category,Mask;}
[StructLayout(LayoutKind.Sequential)]
internal struct PhysicsRayHit {public uint Size,Hit;public ulong Shape,Body;public float X,Y,NormalX,NormalY,Fraction;public uint Reserved;}
[StructLayout(LayoutKind.Sequential)]
internal struct PhysicsAabb {public uint Size,Version;public float LowerX,LowerY,UpperX,UpperY;public ulong Category,Mask;}
[StructLayout(LayoutKind.Sequential)]
internal struct PhysicsState {public uint Size,Bodies,Shapes,RetiredShapes,Steps,Events,Dropped,Reserved;}
internal static unsafe partial class PhysicsNative
{
    [LibraryImport("gal",EntryPoint="gal_physics_open")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int Open(nint c,PhysicsConfig* config);
    [LibraryImport("gal",EntryPoint="gal_physics_close")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int Close(nint c);
    [LibraryImport("gal",EntryPoint="gal_physics_create_body")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int CreateBody(nint c,PhysicsBodyDef* def,ulong* body);
    [LibraryImport("gal",EntryPoint="gal_physics_release_body")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int ReleaseBody(nint c,ulong body);
    [LibraryImport("gal",EntryPoint="gal_physics_create_shape")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int CreateShape(nint c,ulong body,PhysicsShapeDef* def,ulong* shape);
    [LibraryImport("gal",EntryPoint="gal_physics_create_capsule_v1")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int CreateCapsule(nint c,ulong body,PhysicsCapsuleDef* def,ulong* shape);
    [LibraryImport("gal",EntryPoint="gal_physics_release_shape")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int ReleaseShape(nint c,ulong shape);
    [LibraryImport("gal",EntryPoint="gal_physics_body_command")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int Command(nint c,ulong body,uint command,float x,float y,float z);
    [LibraryImport("gal",EntryPoint="gal_physics_get_body")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int GetBody(nint c,ulong body,PhysicsBodyState* state);
    [LibraryImport("gal",EntryPoint="gal_physics_step")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int Step(nint c,PhysicsStep* result);
    [LibraryImport("gal",EntryPoint="gal_physics_events")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int Events(nint c,PhysicsEvent* events,uint capacity,uint* count);
    [LibraryImport("gal",EntryPoint="gal_physics_ray_cast")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int Ray(nint c,PhysicsRay* query,PhysicsRayHit* hit);
    [LibraryImport("gal",EntryPoint="gal_physics_query_aabb")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int Aabb(nint c,PhysicsAabb* query,ulong* shapes,uint capacity,uint* count);
    [LibraryImport("gal",EntryPoint="gal_physics_query_overlap_v1")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int Overlap(nint c,PhysicsOverlapQuery* query,ulong* shapes,uint capacity,uint* count);
    [LibraryImport("gal",EntryPoint="gal_physics_get_state")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] public static partial int State(nint c,PhysicsState* state);
}
