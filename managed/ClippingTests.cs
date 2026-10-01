using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace GameAuthoringLab;
internal static unsafe class ClippingTests
{
    public static int Run()
    {
        int n=0;void Check(bool ok,string label){if(!ok)throw new Exception("CLIP: "+label);n++;}
        void Reject<T>(Action action,string label)where T:Exception{try{action();}catch(T){n++;return;}throw new Exception("CLIP accepted: "+label);}
        Check(sizeof(FramebufferClip)==32&&Marshal.OffsetOf<FramebufferClip>(nameof(FramebufferClip.X))==16,"native layout");
        Check(!FramebufferClip.Disabled.Enabled&&new FramebufferClip(0,0,0,0).Enabled,"disabled and enabled-empty are distinct");
        Reject<ArgumentOutOfRangeException>(()=>new FramebufferClip(0,0,-1,2),"negative width");Reject<ArgumentOutOfRangeException>(()=>new FramebufferClip(0,0,1,-1),"negative height");
        Reject<ArgumentException>(()=>default(FramebufferClip).Validate(),"default lacks version header");
        var one=new Viewport(100,80,100,80);var two=new Viewport(100,80,200,160);var mixed=new Viewport(100,80,200,240);
        Check(FramebufferClip.TryFromWindow(one,10,20,30,40,out var a)&&a==new FramebufferClip(10,20,30,40),"1x window conversion");
        Check(FramebufferClip.TryFromWindow(two,10,20,30,40,out a)&&a==new FramebufferClip(20,40,60,80),"2x window conversion");
        Check(FramebufferClip.TryFromWindow(mixed,10,20,30,40,out a)&&a==new FramebufferClip(20,60,60,120),"nonuniform density");
        Check(FramebufferClip.TryFromWindow(one,10.25,20.75,.5,.5,out a)&&a==new FramebufferClip(10,20,1,2),"conservative outward rounding");
        Check(FramebufferClip.TryFromWindow(one,-10,-20,30,40,out a)&&a==new FramebufferClip(0,0,20,20),"partly outside viewport");
        Check(FramebufferClip.TryFromWindow(one,110,90,3,4,out a)&&a==new FramebufferClip(100,80,0,0),"fully outside viewport");
        Check(FramebufferClip.TryFromWindow(one,1.5,2.5,0,3,out a)&&a.Width==0&&a.Height==0,"fractional empty stays empty");
        Check(FramebufferClip.TryFromWindow(one,-double.MaxValue,0,double.MaxValue,10,out a)&&a.Width==0,"large finite bounds clamp before int conversion");
        Check(FramebufferClip.TryFromWorld(two,new Camera{X=10,Y=20,Zoom=2},20,30,16,8,out a)&&a==new FramebufferClip(20,20,32,16),"camera world projection is already framebuffer pixels");
        foreach(var viewport in new[]{new Viewport(0,80,100,80),new Viewport(100,80,0,80),new Viewport(100,80,100,80,false),new Viewport(100,80,16385,80)})
            Check(!FramebufferClip.TryFromWindow(viewport,0,0,10,10,out a)&&a==FramebufferClip.Disabled,"invalid/minimized viewport");
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity}){
            Check(!FramebufferClip.TryFromWindow(one,bad,0,1,1,out a),"invalid origin");Check(!FramebufferClip.TryFromWorld(one,new(){Zoom=1},0,0,bad,1,out a),"invalid extent");
        }
        Check(!FramebufferClip.TryFromWindow(one,0,0,-1,1,out a),"negative conversion extent");
        Check(!FramebufferClip.TryFromWindow(one,double.MaxValue,0,double.MaxValue,1,out a),"endpoint overflow");
        Check(!FramebufferClip.TryFromWorld(one,new(){Zoom=100},double.MaxValue/2,0,1,1,out a),"projection overflow");
        Check(!FramebufferClip.TryFromWorld(one,new(){Zoom=0},0,0,1,1,out a),"invalid camera zoom");
        Check(!FramebufferClip.TryFromWorld(one,new(){X=float.NaN,Zoom=1},0,0,1,1,out a),"invalid camera origin");
        using var engine=new EngineHost(true,8);var camera=new Camera{Zoom=1};
        SpriteDrawV2[] draws=[SpriteDrawV2.Create(new(){M11=1,M22=1,Width=32,Height=32,R=1,G=1,B=1,A=1})];
        FramebufferClip[] clips=[new(5,5,10,10)];engine.Draw(camera,draws,clips);engine.Draw(camera,draws,clips[0]);engine.Draw(camera,draws,ReadOnlySpan<FramebufferClip>.Empty);
        Check(engine.GetStats().Frames==3&&engine.GetStats().Sprites==3&&engine.GetStats().DrawCalls==0,"headless contract does not claim scissor rendering");
        Reject<ArgumentException>(()=>engine.Draw(camera,draws,new FramebufferClip[]{clips[0],clips[0]}),"wrong clip count");
        Reject<ArgumentException>(()=>engine.Draw(camera,draws,default(FramebufferClip)),"invalid clip rejected before frame");
        var badDraw=draws[0];badDraw.SourceWidth=1;Reject<InvalidOperationException>(()=>engine.Draw(camera,new[]{badDraw},clips),"invalid region aborts clipped frame");
        engine.Draw(camera,draws,clips);Check(engine.GetStats().Frames==4,"valid frame after rejected submission");
        for(int i=0;i<128;i++)engine.Draw(camera,draws,clips);long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)engine.Draw(camera,draws,clips);
        Check(GC.GetAllocatedBytesForCurrentThread()==before,"warmed clipped submission allocates no managed bytes");
        Console.WriteLine($"CLIP CONTRACT PASS assertions={n}");return n;
    }
    internal static SpriteDrawV2 Region(ulong texture,float x,float y,float width,float height,int sx,int sy,int sw=1,int sh=1,float alpha=1)
        =>SpriteDrawV2.Create(new(){M11=1,M22=1,X=x,Y=y,Width=width,Height=height,R=1,G=1,B=1,A=alpha,Texture=texture},new(sx,sy,sw,sh));
    public static int RunGraphics()
    {
        int n=0;void Check(bool ok,string label){if(!ok)throw new Exception("CLIP GRAPHICS: "+label);n++;}
        using var engine=new EngineHost(false,4096,legacyTone:false);var assets=new AssetRoot();using var lease=engine.Textures.Acquire(assets,"regions.bmp");
        string output=Path.GetFullPath(Environment.GetEnvironmentVariable("GAL_CLIP_CAPTURE_DIR")??"evidence/clipping/jit");Directory.CreateDirectory(output);
        var camera=new Camera{Zoom=1};var input=engine.PollInput();
        SpriteDrawV2[] draws=[Region(lease.Handle,32,32,128,128,0,0),Region(lease.Handle,32,32,128,128,8,0,alpha:.5f),Region(lease.Handle,160,40,20,20,8,7),Region(lease.Handle,320,32,128,128,0,0,8,8)];
        draws[3].Draw.M11=0;draws[3].Draw.M12=1;draws[3].Draw.M21=-1;draws[3].Draw.M22=0;draws[3].Flags=1;
        FramebufferClip[] clips=[new(48,48,64,64),new(64,64,64,64),FramebufferClip.Disabled,new(224,64,64,64)];
        void Capture(string name,ReadOnlySpan<SpriteDrawV2> data,ReadOnlySpan<FramebufferClip> clipping){Native.Check(UiNative.Capture(engine.NativeContext,Path.Combine(output,name+".bmp")),"clip capture");engine.Draw(camera,data,clipping);}
        Capture("regions-full",draws,ReadOnlySpan<FramebufferClip>.Empty);uint previous=engine.GetStats().DrawCalls;
        Capture("regions-clipped",draws,clips);Check(engine.GetStats().DrawCalls-previous==4,"different adjacent clip keys preserve separate runs");
        Capture("same-clip",draws,new[]{new FramebufferClip(48,48,64,64)});Check(engine.GetStats().DrawCalls-previous==5,"broadcast same texture/clip combines one run");
        previous=engine.GetStats().DrawCalls;Capture("empty",draws,new[]{new FramebufferClip(0,0,0,30)});Check(engine.GetStats().DrawCalls==previous,"empty intersections submit no world runs");
        // Mixed legacy/new submits inside one native frame must explicitly restore full viewport.
        Native.Check(UiNative.Capture(engine.NativeContext,Path.Combine(output,"legacy-reset.bmp")),"legacy clip capture");var nativeCamera=camera;Native.Check(Native.Begin(engine.NativeContext,&nativeCamera),"begin mixed clips");
        fixed(SpriteDrawV2* data=draws)fixed(FramebufferClip* clip=clips){Native.Check(ClippingNative.Submit(engine.NativeContext,data,2,clip,2),"clipped prefix");Native.Check(Native.SubmitDrawsV2(engine.NativeContext,data+2,1),"legacy suffix");}Native.Check(Native.End(engine.NativeContext),"mixed clipped frame");
        var tile=TileMapAsset.LoadAsset(assets,"basics.tilemap.json");using(var map=new TileMapInstance(engine,tile,new(0,0))){
            var batch=new SpriteBatch(256);map.ExtractSprites(batch,TileView.FromCamera(camera,input.Viewport));Check(batch.Count>0,"second consumer extracts visible tile regions");
            Capture("tile-full",batch.RegionDraws,ReadOnlySpan<FramebufferClip>.Empty);Capture("tile-clipped",batch.RegionDraws,new[]{new FramebufferClip(77,101,319,247)});
        }
        using(var ui=new BoundUiSession<string>(engine,new UiBindings<string>().Text("caption",s=>s).List("members",_=>Array.Empty<UiListRow>(),7).Action("add",8))){
            ui.LoadAsset(assets,"ui/roster.rml");engine.Draw(camera,ReadOnlySpan<SpriteDrawV2>.Empty);ui.Apply("UI survives world clipping / 界面独立");
            Capture("ui-full",ReadOnlySpan<SpriteDrawV2>.Empty,ReadOnlySpan<FramebufferClip>.Empty);
            Capture("ui-empty-world",draws,new[]{new FramebufferClip(0,0,0,0)});Check(ui.Status.Loaded,"UI pass remains live after empty world clip");
        }
        int windows=0;nint* handles=ClippingSdl.Windows(&windows);Check(handles!=null&&windows==1,"one SDL graphics test window");nint window=handles[0];ClippingSdl.Free(handles);
        Check(ClippingSdl.Resize(window,376,244),"resize SDL window");input=engine.PollInput();Check(input.PixelWidth==376&&input.PixelHeight==244,"resized framebuffer observed");
        SpriteDrawV2[] full=[Region(lease.Handle,0,0,2000,2000,0,0)];Capture("resized",full,new[]{new FramebufferClip(-100,20,int.MaxValue,40)});
        Check(FramebufferClip.TryFromWorld(input.Viewport,new(){X=10,Y=20,Zoom=2},20,30,16,16,out var projected)&&projected==new FramebufferClip(20,20,32,32),"world conversion uses resized pixel viewport");
        Console.WriteLine($"CLIP GRAPHICS PASS assertions={n}; SDL software framebuffer, pixel validation separate");return n;
    }
}
internal static unsafe partial class ClippingSdl
{
    [LibraryImport("libSDL3.so.0",EntryPoint="SDL_GetWindows")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial nint* Windows(int* count);
    [LibraryImport("libSDL3.so.0",EntryPoint="SDL_SetWindowSize")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] [return:MarshalAs(UnmanagedType.I1)] internal static partial bool Resize(nint window,int width,int height);
    [LibraryImport("libSDL3.so.0",EntryPoint="SDL_free")] [UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])] internal static partial void Free(void* pointer);
}
