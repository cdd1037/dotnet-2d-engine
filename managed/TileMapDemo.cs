using System.Diagnostics;
namespace GameAuthoringLab;
internal static class TileMapDemo
{
    public static int Run(bool headless,int frames,bool scenario,bool withPhysics,bool clipped=false)
    {
        if(scenario && frames==0) frames=withPhysics?180:5;
        var source=TileMapAsset.LoadAsset(new AssetRoot(),"basics.tilemap.json");
        using var engine=new EngineHost(headless,4096,legacyTone:false);
        using var physics=withPhysics?engine.OpenPhysics():null;
        using var map=new TileMapInstance(engine,source,new(64,64)); var scale=new PhysicsScale(32);
        TileMapCollision? collision=physics is null?null:map.AttachCollision(physics,scale);
        using var ball=physics?.CreateBody(new(PhysicsBodyType.Dynamic,24.5f,4)); ball?.AddShape(new(PhysicsShapeType.Circle,.4f));
        var world=new World(); var actor=world.Create("Optional dynamic ball"); if(ball is not null)actor.Sprite=new(25.6f,25.6f,R:.3f,G:1,B:.5f,Layer:5);
        var batch=new SpriteBatch(256); var camera=new Camera{Zoom=1};
        var actions=new InputActionMap(InputBinding.Key(1,PhysicalKey.Left),InputBinding.Key(2,PhysicalKey.Right),InputBinding.Key(4,PhysicalKey.Up),InputBinding.Key(8,PhysicalKey.Down),InputBinding.Key(16,PhysicalKey.T),InputBinding.Key(32,PhysicalKey.E),InputBinding.Key(64,PhysicalKey.Escape));
        string? captures=Environment.GetEnvironmentVariable("GAL_TILEMAP_CAPTURE_DIR"); if(!headless && captures is not null)Directory.CreateDirectory(captures);
        var clock=Stopwatch.StartNew(); double previous=clock.Elapsed.TotalSeconds,accumulator=0; int rendered=0,steps=0;
        Console.WriteLine($"TILEMAP | {source.Map.Width}x{source.Map.Height} | 16x16 culling chunks | physics={withPhysics} clipped={clipped} rectangles={(collision is null?0:collision.Rectangles.Length)}");
        Console.WriteLine("Arrows: camera | wheel: zoom | T: reset camera | E: reset optional ball | Escape: exit");
        while(frames==0||rendered<frames)
        {
            var input=engine.PollInput();var action=actions.Update(input);if(input.Quit!=0||(action.Pressed&64)!=0)break;
            if(scenario&&!headless&&!input.Drawable){Thread.Sleep(1);continue;}
            double now=clock.Elapsed.TotalSeconds,dt=scenario||headless?1d/60:Math.Clamp(now-previous,0,.1);previous=now;
            bool active=headless||scenario||(input.Focused&&input.Drawable);
            if(!active){dt=0;accumulator=0;}
            if(scenario&&!withPhysics)
                camera=(rendered%5) switch {1=>new(){X=224,Zoom=1},2=>new(){X=32,Y=96,Zoom=1.5f},3=>new(){X=-64,Y=-32,Zoom=.75f},_=>new(){Zoom=1}};
            else if(active)
            {
                float amount=(float)(240*dt/camera.Zoom);camera.X+=((action.Down&2)!=0?amount:0)-((action.Down&1)!=0?amount:0);camera.Y+=((action.Down&8)!=0?amount:0)-((action.Down&4)!=0?amount:0);
                if((action.Pressed&16)!=0)camera=new(){Zoom=1};
                if(input.GameWheelY!=0)Program.MoveCamera(ref camera,new Input{Wheel=input.GameWheelY,Width=input.PixelWidth,Height=input.PixelHeight},0);
                if(ball is not null&&(action.Pressed&32)!=0){ball.Teleport(24.5f,4);ball.SetVelocity(0,0);}
            }
            if(physics is not null)
            {
                accumulator+=scenario?physics.Settings.StepSeconds:dt;
                int ticks=0;while(accumulator>=physics.Settings.StepSeconds&&ticks++<8){if(physics.Step().Dropped!=0)throw new InvalidOperationException("Tile demo physics event overflow.");accumulator-=physics.Settings.StepSeconds;steps++;}
                if(ticks>8)accumulator=0; // Sample catch-up policy; the map does not advance physics implicitly.
                var pose=ball!.State;actor.LocalTransform=new(scale.ToPixels(pose.X)-12.8f,scale.ToPixels(pose.Y)-12.8f);
            }
            world.ExtractSprites(batch); map.AppendSprites(batch,TileView.FromCamera(camera,input.Viewport));
            if(input.Drawable||headless)
            {
                if(!headless&&captures is not null&&(withPhysics?rendered is 0 or 179:rendered<5))
                    Native.Check(UiNative.Capture(engine.NativeContext,Path.GetFullPath(Path.Combine(captures,$"frame-{rendered:D3}.bmp"))),"tilemap capture");
                if(clipped)engine.Draw(camera,batch.RegionDraws,new FramebufferClip(64,64,Math.Max(0,input.PixelWidth-128),Math.Max(0,input.PixelHeight-128)));
                else engine.Draw(camera,batch.RegionDraws);rendered++;
            }
            if(!scenario&&!headless)Thread.Sleep(1);
        }
        if(physics is not null)
        {
            var hit=physics.RayCast(24.5f,0,0,25);
            if(hit.Shape==0)throw new InvalidOperationException("Expected a floor/ball ray hit in the tile demo.");
            Console.WriteLine($"TILEMAP PHYSICS steps={steps} ball_y={ball!.State.Y:R} ray_y={hit.Y:R}");
        }
        int cacheLoads=engine.Textures.Loads;ball?.Dispose();map.Dispose();physics?.Step();
        if(engine.Textures.Count!=0||engine.TextureCount!=0||(physics is not null&&(physics.State.Bodies!=0||physics.State.Shapes!=0||physics.State.RetiredShapes!=0)))throw new InvalidOperationException("Tile map demo cleanup failed.");
        Console.WriteLine($"TILEMAP DONE frames={rendered} visible={batch.Count} cache_loads={cacheLoads} gpu_uploads={(headless?0:cacheLoads)} textures=0 bodies=0");return 0;
    }
}
