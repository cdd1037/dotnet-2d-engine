using System.Numerics;
namespace GameAuthoringLab;
internal static class RenderTargetDemo
{
    internal static int Run(bool test,int frames)
    {
        using var engine=EngineHost.Create(false,64);var assets=new AssetRoot();
        using var white=engine.Textures.Acquire(assets,"regions.bmp");
        using var tint=engine.Materials.Acquire(assets,"materials/tint.material.json");using var gray=engine.Materials.Acquire(assets,"materials/desaturate.material.json");
        using var a=engine.RenderTargets.Create(96,96);using var b=engine.RenderTargets.Create(96,96);var camera=new Camera{Zoom=1};
        SpriteDrawV2 Solid(float x,float y,float width,float height,Vector4 color)=>SpriteDrawV2.Create(new(){M11=1,M22=1,X=x,Y=y,Width=width,Height=height,R=color.X,G=color.Y,B=color.Z,A=color.W,Texture=white.Handle},new(7,7,1,1));
        SpriteDrawV2 Sample(RenderTarget target,float x,float y)=>SpriteDrawV2.Create(new(){M11=1,M22=1,X=x,Y=y,Width=96,Height=96,R=1,G=1,B=1,A=1,Texture=target.Handle});
        MaterialDraw[] draws=[
            MaterialDraw.Create(Solid(0,0,64,64,new(1,0,0,.5f))),
            MaterialDraw.Create(Solid(32,0,64,64,new(0,0,1,.5f))),
            MaterialDraw.Create(Solid(0,64,96,16,new(1,1,1,1f/255))),
            MaterialDraw.Create(Solid(0,80,96,8,new(1,1,1,.000001f))),
            MaterialDraw.Create(Sample(a,0,0),tint.Handle,new(new(.5f,1,.25f,.5f))),
            MaterialDraw.Create(Sample(a,32,32)),
            MaterialDraw.Create(Sample(b,160,32)),
            MaterialDraw.Create(Sample(a,288,32),gray.Handle,new(new(1,1,0,0))),
            MaterialDraw.Create(Solid(32,160,64,64,new(1,0,0,.5f))),
            MaterialDraw.Create(Solid(64,160,64,64,new(0,0,1,.5f)))];
        RenderPass[] passes=[RenderPass.Create(a.Handle,camera,0,4),RenderPass.Create(b.Handle,camera,4,1),RenderPass.Create(0,camera,5,5,new(0,0,0,1))];
        RenderPass[] windowOnly=[RenderPass.Create(0,camera,0,5,new(0,0,0,1))];
        int n=0;void Check(bool ok,string label){if(!ok)throw new Exception("TARGET GRAPHICS: "+label);n++;}
        if(test)
        {
            string directory=Path.GetFullPath(Environment.GetEnvironmentVariable("GAL_TARGET_CAPTURE_DIR")??"evidence/targets/jit");Directory.CreateDirectory(directory);
            void Capture(string name)=>Native.Check(UiNative.Capture(engine.NativeContext,Path.Combine(directory,name+".bmp")),"target capture");
            engine.PollInput();Capture("composite");engine.RenderFrame(passes,draws);
            Check(engine.GetStats().Frames==1&&engine.GetStats().Sprites==10,"ordered three-pass frame counts user quads once");
            Check(engine.GetStats().DrawCalls==8,"six user runs plus two resolve passes");
            Capture("retained");engine.RenderFrame(windowOnly,draws.AsSpan(5));Check(engine.GetStats().DrawCalls==12,"retained target sampling adds no resolve pass");
            RenderPass[] bad=[RenderPass.Create(a.Handle,camera,0,0,new(0,1,0,1)),RenderPass.Create(b.Handle,camera,0,1),RenderPass.Create(0,camera,1,0,new(0,0,0,1))];
            bool rejected=false;try{engine.RenderFrame(bad,new[]{MaterialDraw.Create(Sample(b,0,0))});}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&engine.GetStats().Frames==2,"late feedback rejection executes no earlier pass");
            Capture("failed-retained");engine.RenderFrame(windowOnly,draws.AsSpan(5));
            // Camera and scissors refer to target pixels, not the window's larger dimensions.
            Capture("camera-clip");RenderPass[] projected=[RenderPass.Create(a.Handle,new(){X=10,Y=20,Zoom=2},0,1),RenderPass.Create(0,camera,1,1,new(0,0,0,1))];
            MaterialDraw[] projectedDraws=[MaterialDraw.Create(Solid(18,28,24,16,new(0,1,0,1))),MaterialDraw.Create(Sample(a,32,32))];
            engine.RenderFrame(projected,projectedDraws,new[]{new FramebufferClip(24,16,24,32),FramebufferClip.Disabled});
            Capture("clear");engine.RenderFrame(new[]{RenderPass.Create(a.Handle,camera,0,0,new(1,.5f,0,.25f)),RenderPass.Create(0,camera,0,1,new(0,0,0,1))},new[]{MaterialDraw.Create(Sample(a,32,32))});
            using(var blank=engine.RenderTargets.Create(96,96))
            {Capture("new-transparent");engine.RenderFrame(new[]{RenderPass.Create(0,camera,0,1,new(0,0,0,1))},new[]{MaterialDraw.Create(Sample(blank,32,32))});}
            using(var replacement=engine.RenderTargets.Create(48,32))
            {
                Capture("resized-target");var resized=SpriteDrawV2.Create(new(){M11=1,M22=1,X=32,Y=32,Width=48,Height=32,R=1,G=1,B=1,A=1,Texture=replacement.Handle});
                engine.RenderFrame(new[]{RenderPass.Create(replacement.Handle,camera,0,0,new(1,1,0,1)),RenderPass.Create(0,camera,0,1,new(0,0,0,1))},new[]{MaterialDraw.Create(resized)});
                Check(a.Width==96&&replacement.Width==48&&engine.RenderTargets.Count==3,"explicit replacement leaves previous target valid");
            }
            using(var ui=new BoundUiSession<string>(engine,new UiBindings<string>().Text("caption",s=>s).List("members",_=>Array.Empty<UiListRow>(),7).Action("add",8)))
            {
                ui.LoadAsset(assets,"ui/roster.rml");engine.Draw(camera,ReadOnlySpan<SpriteDrawV2>.Empty);ui.Apply("Final window UI / 最终窗口");
                Capture("ui-window");engine.RenderFrame(new[]{RenderPass.Create(0,camera,0,0,new(0,0,0,1))},[]);
                Capture("ui-target");engine.RenderFrame(new[]{RenderPass.Create(a.Handle,camera,0,0),RenderPass.Create(0,camera,0,1,new(0,0,0,1))},new[]{MaterialDraw.Create(Sample(a,700,350))});
                Check(ui.Status.Loaded,"UI stays on final window pass");
            }
            a.Dispose();b.Dispose();Check(engine.RenderTargets.Count==0&&engine.RenderTargets.AllocatedBytes==0&&engine.TextureCount==1,"target pairs and sample registrations released");
            Console.WriteLine($"TARGET GRAPHICS PASS assertions={n}; software Vulkan, alpha/pixel checks separate");return 0;
        }
        Console.WriteLine("TARGET DEMO | direct translucent overlap below; target, second-pass tint and desaturation above | Escape exits");
        int rendered=0;while(frames==0||rendered<frames){var input=engine.PollInput();if(input.Quit!=0||input.KeyPressed(PhysicalKey.Escape))break;engine.RenderFrame(passes,draws);rendered++;Thread.Sleep(1);}
        Console.WriteLine($"TARGET DEMO DONE frames={rendered} paired_bytes={engine.RenderTargets.AllocatedBytes}");return 0;
    }
}
