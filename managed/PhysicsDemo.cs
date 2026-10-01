using System.Diagnostics;
using System.Globalization;
namespace GameAuthoringLab;
internal static class PhysicsDemo
{
    public static int Run(int frames,bool scenario)
    {
        var assets=new AssetRoot();var fixture=PhysicsFixture.LoadAsset(assets,"basics.physics.json");var scale=new PhysicsScale(fixture.PixelsPerMeter);
        using var engine=new EngineHost(false,128,legacyTone:false);using var physics=engine.OpenPhysics(fixture.Settings);using var scope=new PhysicsScope(physics);
        using var white=engine.Textures.Acquire(assets,"regions.bmp");
        var bodies=new PhysicsBody[fixture.Bodies.Count];for(int i=0;i<bodies.Length;i++){bodies[i]=scope.CreateBody(fixture.Bodies[i].BodyDefinition);bodies[i].AddShape(fixture.Bodies[i].ShapeDefinition);}
        int selected=fixture.Bodies.FindIndex(b=>b.Type=="dynamic");
        var actions=new InputActionMap(InputBinding.Key(1,PhysicalKey.E),InputBinding.Key(2,PhysicalKey.T),InputBinding.Key(4,PhysicalKey.Space),InputBinding.Key(8,PhysicalKey.Escape));
        var draw=new SpriteDrawV2[bodies.Length+1];var camera=new Camera{Zoom=1};var clock=Stopwatch.StartNew();double previous=clock.Elapsed.TotalSeconds,accumulator=0;
        bool paused=false;int rendered=0;uint contacts=0,sensors=0;
        if(scenario&&frames==0)frames=180;
        Console.WriteLine("PHYSICS DEMO | meters -> pixels explicitly | E impulse first dynamic body | T teleport/reset velocity | Space pause | Escape exit");
        while(frames==0||rendered<frames)
        {
            var input=engine.PollInput();var action=actions.Update(input);if(input.Quit!=0||(action.Pressed&8)!=0)break;
            double now=clock.Elapsed.TotalSeconds,dt=Math.Clamp(now-previous,0,.1);previous=now;
            bool active=input.Focused&&input.Drawable;
            if(active)
            {
                if((action.Pressed&4)!=0)paused=!paused;
                if(selected>=0&&(action.Pressed&1)!=0)bodies[selected].ApplyImpulse(0,-3);
                if(selected>=0&&(action.Pressed&2)!=0){var source=fixture.Bodies[selected];bodies[selected].Teleport(source.X,source.Y,source.Angle);bodies[selected].SetVelocity(0,0);}
            }
            if(scenario)accumulator=fixture.StepSeconds;
            else if(active&&!paused)accumulator+=dt;else accumulator=0;
            int steps=0;
            while(accumulator>=fixture.StepSeconds&&steps++<8)
            {
                var result=physics.Step();if(result.Dropped!=0)throw new InvalidOperationException($"Physics step {result.Index} completed with {result.Dropped} dropped events.");
                foreach(var e in physics.Events){if(e.Type==PhysicsEventType.ContactBegin)contacts++;if(e.Type==PhysicsEventType.SensorBegin)sensors++;}
                accumulator-=fixture.StepSeconds;
            }
            if(steps>8)accumulator=0; // bounded demo catch-up policy, not a solver capability.
            if(input.Drawable)
            {
                for(int i=0;i<bodies.Length;i++)
                {
                    var state=bodies[i].State;var source=fixture.Bodies[i];float width=scale.ToPixels(source.A*2),height=scale.ToPixels((source.Shape=="circle"?source.A:source.B)*2);
                    float cosine=MathF.Cos(state.Angle),sine=MathF.Sin(state.Angle);float x=scale.ToPixels(state.X),y=scale.ToPixels(state.Y);
                    var d=new SpriteDraw{M11=cosine,M12=sine,M21=-sine,M22=cosine,X=x-cosine*width/2+sine*height/2,Y=y-sine*width/2-cosine*height/2,Width=width,Height=height,
                        R=source.Sensor?.3f:state.Type==PhysicsBodyType.Dynamic?1:.5f,G=source.Sensor?1:state.Type==PhysicsBodyType.Kinematic?.8f:.6f,B=source.Sensor?.5f:.8f,A=source.Sensor?.18f:1,Texture=source.Shape=="circle"?0:white.Handle};
                    draw[i]=SpriteDrawV2.Create(d,d.Texture==0?null:new TextureRegion(7,7,1,1));
                }
                draw[^1]=SpriteDrawV2.Create(new SpriteDraw{M11=1,M22=1,X=16,Y=16,Width=20,Height=20,R=paused?1:.2f,G=.8f,B=.2f,A=1});
                engine.Draw(camera,draw);rendered++;
            }
            if(!scenario)Thread.Sleep(1);
        }
        string positions=string.Join(";",bodies.Select(b=>{var s=b.State;return string.Create(CultureInfo.InvariantCulture,$"{s.X:R},{s.Y:R},{s.Angle:R}");}));
        scope.Dispose();physics.Step();var final=physics.State;if(final.Bodies!=0||final.Shapes!=0||final.RetiredShapes!=0)throw new InvalidOperationException("Physics demo resources were not released.");
        Console.WriteLine($"PHYSICS DEMO DONE frames={rendered} contact_begins={contacts} sensor_begins={sensors} bodies=0 shapes=0");
        if(scenario)Console.WriteLine("PHYSICS POSITIONS "+positions);return 0;
    }
}
