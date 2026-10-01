using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace GameAuthoringLab;

/// <summary>One explicit pass. Draw ranges partition the shared array, and the last pass targets the window (zero).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct RenderPass
{
    public uint Size,Version;
    public ulong Target;
    public Camera Camera;
    public Vector4 ClearColor;
    public uint FirstDraw,DrawCount,Reserved;
    public static unsafe RenderPass Create(ulong target,Camera camera,int firstDraw,int drawCount,Vector4 clearColor=default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstDraw);ArgumentOutOfRangeException.ThrowIfNegative(drawCount);
        return new(){Size=(uint)sizeof(RenderPass),Version=1,Target=target,Camera=camera,ClearColor=clearColor,FirstDraw=(uint)firstDraw,DrawCount=(uint)drawCount};
    }
}

/// <summary>Owns paired RGBA8 attachment/sample textures. Create a replacement before disposing the old target to resize safely.</summary>
public sealed unsafe class RenderTargetStore : IEngineOwned
{
    public const int MaximumTargets=8,MaximumDimension=4096;
    public const long MaximumBytes=64*1024*1024;
    private readonly EngineHost _engine;
    private readonly Dictionary<ulong,RenderTarget> _targets=new();
    private bool _destroyed;
    internal RenderTargetStore(EngineHost engine)=>_engine=engine;
    public int Count{get{CheckAccess();return _targets.Count;}}
    public long AllocatedBytes{get;private set;}
    internal void CheckAccess(){_engine.AssertAlive();ObjectDisposedException.ThrowIf(_destroyed,this);}
    public RenderTarget Create(int width,int height)
    {
        CheckAccess();
        if(width is <1 or >MaximumDimension)throw new ArgumentOutOfRangeException(nameof(width));
        if(height is <1 or >MaximumDimension)throw new ArgumentOutOfRangeException(nameof(height));
        long bytes=8L*width*height;
        if(_targets.Count==MaximumTargets||bytes>MaximumBytes-AllocatedBytes)throw new InvalidOperationException("Render target count or paired RGBA8 byte budget exceeded.");
        var descriptor=new TargetDescriptor{Size=(uint)sizeof(TargetDescriptor),Version=1,Width=width,Height=height};ulong handle=0;
        Native.Check(TargetNative.Create(_engine.NativeContext,&descriptor,&handle),"create render target");
        try{var target=new RenderTarget(this,handle,width,height);_targets.Add(handle,target);AllocatedBytes+=bytes;return target;}
        catch{TargetNative.Release(_engine.NativeContext,handle);throw;}
    }
    internal void Release(ulong handle)
    {
        _engine.AssertThread();if(_destroyed)return;CheckAccess();var target=_targets[handle];
        Native.Check(TargetNative.Release(_engine.NativeContext,handle),"release render target");_targets.Remove(handle);AllocatedBytes-=target.StorageBytes;
    }
    void IEngineOwned.EngineDestroyed(){_targets.Clear();AllocatedBytes=0;_destroyed=true;}
}

public sealed class RenderTarget : IDisposable
{
    private readonly RenderTargetStore _owner;
    private readonly ulong _handle;
    private readonly int _width,_height;
    private bool _disposed;
    internal RenderTarget(RenderTargetStore owner,ulong handle,int width,int height){_owner=owner;_handle=handle;_width=width;_height=height;}
    private void CheckAccess(){ObjectDisposedException.ThrowIf(_disposed,this);_owner.CheckAccess();}
    public ulong Handle{get{CheckAccess();return _handle;}}
    public int Width{get{CheckAccess();return _width;}}
    public int Height{get{CheckAccess();return _height;}}
    public TextureBinding Binding=>new(Handle);
    public long StorageBytes=>8L*_width*_height;
    public void Dispose(){if(_disposed)return;_owner.Release(_handle);_disposed=true;}
}

[StructLayout(LayoutKind.Sequential)]
internal struct TargetDescriptor{public uint Size,Version;public int Width,Height;}
internal static unsafe partial class TargetNative
{
    [LibraryImport("gal",EntryPoint="gal_target_create_v1")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    internal static partial int Create(nint context,TargetDescriptor* descriptor,ulong* target);
    [LibraryImport("gal",EntryPoint="gal_target_release")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    internal static partial int Release(nint context,ulong target);
    [LibraryImport("gal",EntryPoint="gal_render_frame_v1")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    internal static partial int Render(nint context,RenderPass* passes,uint passCount,MaterialDraw* draws,uint drawCount,FramebufferClip* clips,uint clipCount);
}
