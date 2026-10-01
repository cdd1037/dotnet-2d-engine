using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace GameAuthoringLab;

/// <summary>Half-open axis-aligned framebuffer-pixel scissor. Use Disabled explicitly; default has no ABI header.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct FramebufferClip : IEquatable<FramebufferClip>
{
    private readonly uint _size,_version,_flags,_reserved;
    public readonly int X,Y,Width,Height;
    public bool Enabled=>(_flags&1)!=0;
    public static FramebufferClip Disabled=>new(false,0,0,0,0);
    public FramebufferClip(int x,int y,int width,int height):this(true,x,y,width,height){}
    private FramebufferClip(bool enabled,int x,int y,int width,int height)
    {
        if(width<0)throw new ArgumentOutOfRangeException(nameof(width),"Clip extents must be nonnegative.");
        if(height<0)throw new ArgumentOutOfRangeException(nameof(height),"Clip extents must be nonnegative.");
        _size=32;_version=1;_flags=enabled?1u:0;_reserved=0;X=x;Y=y;Width=width;Height=height;
    }
    internal void Validate()
    {
        if(_size!=32||_version!=1||_reserved!=0||_flags>1||Width<0||Height<0||(!Enabled&&(X!=0||Y!=0||Width!=0||Height!=0)))
            throw new ArgumentException("Invalid clip record; construct a rectangle or use FramebufferClip.Disabled.");
    }
    public bool Equals(FramebufferClip other)=>_size==other._size&&_version==other._version&&_flags==other._flags&&_reserved==other._reserved&&X==other.X&&Y==other.Y&&Width==other.Width&&Height==other.Height;
    public override bool Equals(object? obj)=>obj is FramebufferClip other&&Equals(other);
    public override int GetHashCode()=>HashCode.Combine(_size,_version,_flags,_reserved,X,Y,Width,Height);
    public static bool operator==(FramebufferClip left,FramebufferClip right)=>left.Equals(right);
    public static bool operator!=(FramebufferClip left,FramebufferClip right)=>!left.Equals(right);

    /// <summary>Conservative outward rounding, clamped to the supplied drawable framebuffer. False for invalid sizes/numbers.</summary>
    public static bool TryFromWindow(Viewport viewport,double x,double y,double width,double height,out FramebufferClip clip)
    {
        clip=Disabled;if(!viewport.IsValid||!Bounds(x,y,width,height))return false;
        double sx=(double)viewport.PixelWidth/viewport.WindowWidth,sy=(double)viewport.PixelHeight/viewport.WindowHeight;
        return Pixels(viewport,x*sx,y*sy,(x+width)*sx,(y+height)*sy,width==0||height==0,out clip);
    }
    /// <summary>Axis-aligned world bounds projected by the current camera, then outward-rounded to framebuffer pixels.</summary>
    public static bool TryFromWorld(Viewport viewport,Camera camera,double x,double y,double width,double height,out FramebufferClip clip)
    {
        clip=Disabled;if(!viewport.IsValid||!Bounds(x,y,width,height)||!float.IsFinite(camera.X)||!float.IsFinite(camera.Y)||!float.IsFinite(camera.Zoom)||camera.Zoom is <.01f or >100)return false;
        return Pixels(viewport,(x-camera.X)*camera.Zoom,(y-camera.Y)*camera.Zoom,(x+width-camera.X)*camera.Zoom,(y+height-camera.Y)*camera.Zoom,width==0||height==0,out clip);
    }
    private static bool Bounds(double x,double y,double width,double height)=>double.IsFinite(x)&&double.IsFinite(y)&&double.IsFinite(width)&&double.IsFinite(height)&&width>=0&&height>=0&&double.IsFinite(x+width)&&double.IsFinite(y+height);
    private static bool Pixels(Viewport viewport,double left,double top,double right,double bottom,bool empty,out FramebufferClip clip)
    {
        clip=Disabled;if(!double.IsFinite(left)||!double.IsFinite(top)||!double.IsFinite(right)||!double.IsFinite(bottom))return false;
        int x=(int)Math.Clamp(Math.Floor(left),0,viewport.PixelWidth),y=(int)Math.Clamp(Math.Floor(top),0,viewport.PixelHeight);
        int endX=(int)Math.Clamp(Math.Ceiling(right),0,viewport.PixelWidth),endY=(int)Math.Clamp(Math.Ceiling(bottom),0,viewport.PixelHeight);
        clip=new(x,y,empty?0:Math.Max(0,endX-x),empty?0:Math.Max(0,endY-y));return true;
    }
}
internal static unsafe partial class ClippingNative
{
    [LibraryImport("gal",EntryPoint="gal_submit_draws_clipped_v1")]
    [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    internal static partial int Submit(nint context,SpriteDrawV2* draws,uint count,FramebufferClip* clips,uint clipCount);
}
