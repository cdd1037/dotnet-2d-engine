using System.Numerics;
using System.Runtime.InteropServices;
namespace GameAuthoringLab;
internal static unsafe class RenderTargetTests
{
    public static int Run()
    {
        int n=0;void Check(bool ok,string label){if(!ok)throw new Exception("TARGET: "+label);n++;}
        void Reject<T>(Action action,string label)where T:Exception{try{action();}catch(T){n++;return;}throw new Exception("TARGET accepted: "+label);}
        Check(sizeof(TargetDescriptor)==16&&sizeof(RenderPass)==56,"target/pass layouts");
        Check(Marshal.OffsetOf<RenderPass>(nameof(RenderPass.Target))==8&&Marshal.OffsetOf<RenderPass>(nameof(RenderPass.Camera))==16&&Marshal.OffsetOf<RenderPass>(nameof(RenderPass.ClearColor))==28&&Marshal.OffsetOf<RenderPass>(nameof(RenderPass.FirstDraw))==44,"pass field offsets");
        using var engine=EngineHost.Create(true,16);var targets=engine.RenderTargets;var camera=new Camera{Zoom=1};
        Reject<ArgumentOutOfRangeException>(()=>targets.Create(0,1),"zero width");Reject<ArgumentOutOfRangeException>(()=>targets.Create(1,4097),"oversized height");
        Reject<InvalidOperationException>(()=>targets.Create(4096,4096),"paired texture byte budget");
        using var first=targets.Create(64,32);using var second=targets.Create(32,16);ulong oldHandle=first.Handle;
        Check(targets.Count==2&&targets.AllocatedBytes==8L*(64*32+32*16)&&first.StorageBytes==8L*64*32,"paired allocation accounting");
        Check(first.Width==64&&first.Height==32&&first.Binding.Handle==first.Handle&&engine.GetTextureInfo(first.Handle).Width==64,"target dimensions and sample binding");
        Check(engine.TextureCount==2,"targets registered in texture namespace");
        Check(Task.Run(()=>{try{_ = first.Handle;return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult(),"target ownership thread");
        var solid=SpriteDrawV2.Create(new(){M11=1,M22=1,Width=16,Height=16,R=1,G=1,B=1,A=1});
        var sampled=solid;sampled.Draw.Texture=first.Handle;
        MaterialDraw[] draws=[MaterialDraw.Create(solid),MaterialDraw.Create(sampled)];
        RenderPass[] passes=[RenderPass.Create(first.Handle,camera,0,1),RenderPass.Create(0,camera,1,1,new(0,0,0,1))];
        engine.RenderFrame(passes,draws);Check(engine.GetStats().Frames==1&&engine.GetStats().Sprites==2&&engine.GetStats().DrawCalls==0,"headless multi-pass frame counts without rendering claim");
        Reject<InvalidOperationException>(()=>engine.RenderFrame([],draws),"zero passes");
        Reject<InvalidOperationException>(()=>engine.RenderFrame(new RenderPass[17],[]),"pass bound");
        Reject<InvalidOperationException>(()=>engine.RenderFrame(new[]{passes[0]},draws.AsSpan(0,1)),"missing final window pass");
        Reject<InvalidOperationException>(()=>engine.RenderFrame(new[]{passes[1],passes[1]},draws),"window before final pass");
        Reject<InvalidOperationException>(()=>engine.RenderFrame(new[]{RenderPass.Create(first.Handle,camera,0,1),RenderPass.Create(0,camera,0,1)},draws),"overlapping ranges");
        Reject<InvalidOperationException>(()=>engine.RenderFrame(new[]{RenderPass.Create(first.Handle,camera,0,0),RenderPass.Create(0,camera,1,1)},draws),"gap in ranges");
        Reject<InvalidOperationException>(()=>engine.RenderFrame(new[]{RenderPass.Create(0,camera,0,1)},draws),"unused trailing draw");
        var bad=passes[0];bad.Reserved=1;Reject<InvalidOperationException>(()=>engine.RenderFrame(new[]{bad,passes[1]},draws),"reserved pass field");
        bad=passes[0];bad.Camera.Zoom=0;Reject<InvalidOperationException>(()=>engine.RenderFrame(new[]{bad,passes[1]},draws),"invalid pass camera");
        bad=passes[0];bad.ClearColor=new(float.NaN,0,0,0);Reject<InvalidOperationException>(()=>engine.RenderFrame(new[]{bad,passes[1]},draws),"nonfinite clear");
        bad.ClearColor=new(0,0,0,1.1f);Reject<InvalidOperationException>(()=>engine.RenderFrame(new[]{bad,passes[1]},draws),"clear alpha outside range");
        var feedback=(MaterialDraw[])draws.Clone();feedback[0].Sprite.Draw.Texture=first.Handle;
        Reject<InvalidOperationException>(()=>engine.RenderFrame(passes,feedback),"read/write target feedback");
        Reject<InvalidOperationException>(()=>engine.ReleaseTexture(first.Handle),"target requires its owning release API");
        engine.RenderFrame(passes,draws,new[]{new FramebufferClip(0,0,16,16)});Check(engine.GetStats().Frames==2,"valid frame after all rejected plans");
        // Sampling another target is legal, including a newly created transparent target.
        feedback[0].Sprite.Draw.Texture=second.Handle;engine.RenderFrame(passes,feedback);Check(engine.GetStats().Frames==3,"different attachment/sample handles accepted");
        var legacyCamera=camera;Native.Check(Native.Begin(engine.NativeContext,&legacyCamera),"test legacy begin");
        Reject<InvalidOperationException>(()=>engine.RenderFrame(passes,draws),"render plan during legacy frame");
        Reject<InvalidOperationException>(()=>targets.Create(1,1),"create during legacy frame");
        Reject<InvalidOperationException>(()=>first.Dispose(),"release during legacy frame");Native.Check(Native.Abort(engine.NativeContext),"test legacy abort");
        Check(first.Handle==oldHandle&&targets.Count==2,"failed release/create retains ownership");
        Reject<InvalidOperationException>(()=>targets.Create(4096,2048),"failed resize replacement retains original");
        Check(first.Handle==oldHandle&&first.Width==64,"old target survives rejected replacement");
        using(var replacement=targets.Create(128,64)){Check(replacement.Handle!=oldHandle&&targets.Count==3,"explicit successful resize replacement");}
        for(int i=0;i<128;i++)engine.RenderFrame(passes,draws);long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)engine.RenderFrame(passes,draws);
        Check(GC.GetAllocatedBytesForCurrentThread()==before,"warmed render plans allocate no managed bytes");
        first.Dispose();first.Dispose();Reject<ObjectDisposedException>(()=>_ = first.Binding,"disposed sample binding");
        Reject<InvalidOperationException>(()=>engine.RenderFrame(passes,draws),"released target plan rejected");
        Check(targets.Count==1&&targets.AllocatedBytes==second.StorageBytes,"target release accounting");
        using(var third=targets.Create(1,1))Check(third.Handle!=oldHandle,"target identity never reused");
        var extras=new List<RenderTarget>();try{for(int i=0;i<7;i++)extras.Add(targets.Create(1,1));Reject<InvalidOperationException>(()=>targets.Create(1,1),"target count cap");}finally{foreach(var target in extras)target.Dispose();}
        second.Dispose();using(var full=targets.Create(4096,2048))Check(targets.AllocatedBytes==RenderTargetStore.MaximumBytes,"exact 64 MiB virtual budget");
        using var survivor=targets.Create(4,4);ulong foreign=survivor.Handle;engine.Dispose();survivor.Dispose();Check(targets.AllocatedBytes==0,"engine-first cleanup and later target disposal");
        using var next=EngineHost.Create(true,16);Reject<InvalidOperationException>(()=>next.RenderFrame(new[]{RenderPass.Create(foreign,camera,0,0),RenderPass.Create(0,camera,0,0)},[]),"old context target rejected");
        Console.WriteLine($"TARGET CONTRACT PASS assertions={n}");return n;
    }
}
