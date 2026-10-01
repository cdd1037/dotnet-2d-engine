using System.Numerics;
namespace GameAuthoringLab;
internal static unsafe class MaterialDemo
{
    internal static int Run(bool test,int frames)
    {
        using var engine=EngineHost.Create(false,64);var assets=new AssetRoot();
        using var white=engine.Textures.Acquire(assets,"regions.bmp");
        using var tint=engine.Materials.Acquire(assets,"materials/tint.material.json");
        using var gray=engine.Materials.Acquire(assets,"materials/desaturate.material.json");
        var camera=new Camera{Zoom=1};var whiteRegion=new TextureRegion(7,7,1,1);
        SpriteDrawV2 Sprite(float x,float y,float r=1,float g=1,float b=1)=>SpriteDrawV2.Create(new(){M11=1,M22=1,X=x,Y=y,Width=96,Height=96,R=r,G=g,B=b,A=1,Texture=white.Handle},whiteRegion);
        MaterialDraw[] draws=[
            MaterialDraw.Create(Sprite(32,32,.25f,.35f,.7f)),
            MaterialDraw.Create(Sprite(144,32),tint.Handle,new(new(1,0,0,1))),
            MaterialDraw.Create(Sprite(256,32),tint.Handle,new(new(0,1,0,1))),
            MaterialDraw.Create(Sprite(368,32,1,0,0),gray.Handle,new(new(1,1,0,0))),
            MaterialDraw.Create(Sprite(480,32,0,.5f,1)),
            MaterialDraw.Create(Sprite(32,160),tint.Handle,new(new(1,0,0,.5f))),
            MaterialDraw.Create(Sprite(32,160),tint.Handle,new(new(0,0,1,.5f))),
            MaterialDraw.Create(Sprite(144,160),tint.Handle,new(new(1,1,0,1))),
            MaterialDraw.Create(Sprite(256,160),tint.Handle,new(new(1,1,0,1)))];
        int n=0;void Check(bool ok,string label){if(!ok)throw new Exception("MATERIAL GRAPHICS: "+label);n++;}
        if(test)
        {
            string directory=Path.GetFullPath(Environment.GetEnvironmentVariable("GAL_MATERIAL_CAPTURE_DIR")??"evidence/materials/jit");Directory.CreateDirectory(directory);
            void Capture(string name){Native.Check(UiNative.Capture(engine.NativeContext,Path.Combine(directory,name+".bmp")),"material capture");}
            engine.PollInput();Capture("materials");engine.Draw(camera,draws);
            Check(engine.GetStats().DrawCalls==8,"adjacent identical material parameters merge while ordering and parameter changes split");
            Capture("clipped");FramebufferClip[] clips=new FramebufferClip[draws.Length];Array.Fill(clips,FramebufferClip.Disabled);clips[1]=new(168,56,32,32);engine.Draw(camera,draws,clips);
            Check(engine.GetStats().DrawCalls==16,"material clips preserve run count");
            // Native submit copies all constants; changing the borrowed managed record afterward cannot affect it.
            Capture("copied");var copied=draws[1];Native.Check(Native.Begin(engine.NativeContext,&camera),"begin copied parameters");Native.Check(MaterialNative.Submit(engine.NativeContext,&copied,1,null,0),"submit copied parameters");copied.Parameters=new(new(0,1,0,1));Native.Check(Native.End(engine.NativeContext),"end copied parameters");
            Check(engine.GetStats().DrawCalls==17,"parameter snapshot run");
            Capture("legacy-reset");Native.Check(Native.Begin(engine.NativeContext,&camera),"begin legacy reset");fixed(MaterialDraw* data=draws)Native.Check(MaterialNative.Submit(engine.NativeContext,data+1,1,null,0),"custom material prefix");var legacy=Sprite(256,32,0,0,1);Native.Check(Native.SubmitDrawsV2(engine.NativeContext,&legacy,1),"legacy default suffix");Native.Check(Native.End(engine.NativeContext),"end legacy reset");
            Check(engine.GetStats().DrawCalls==19,"legacy submit restores default pipeline");
            using(var extra=engine.Materials.Acquire(assets,"materials/tint.material.json"))Check(extra.Handle==tint.Handle&&engine.Materials.Count==2,"graphics cache shares existing pipeline");
            ulong old=tint.Handle;tint.Dispose();bool stale=false;try{engine.Draw(camera,draws);}catch(InvalidOperationException){stale=true;}Check(stale,"released pipeline rejected before drawing");
            using var newer=engine.Materials.Acquire(assets,"materials/tint.material.json");Check(newer.Handle!=old,"reload uses new material identity");
            draws[1].Material=newer.Handle;Capture("reloaded");engine.Draw(camera,draws.AsSpan(1,1));
            newer.Dispose();gray.Dispose();Check(engine.Materials.Count==0,"all pipelines released");
            Console.WriteLine($"MATERIAL GRAPHICS PASS assertions={n}; software Vulkan readback, pixels checked separately");return 0;
        }
        Console.WriteLine("MATERIAL DEMO | default, tint, desaturation, parameter variants and alpha ordering | Escape exits");
        int rendered=0;while(frames==0||rendered<frames){var input=engine.PollInput();if(input.Quit!=0||input.KeyPressed(PhysicalKey.Escape))break;engine.Draw(camera,draws);rendered++;Thread.Sleep(1);}
        Console.WriteLine($"MATERIAL DEMO DONE frames={rendered} pipelines={engine.Materials.Count}");return 0;
    }
}
