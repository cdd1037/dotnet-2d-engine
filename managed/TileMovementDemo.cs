using System.Diagnostics;
using System.Globalization;
namespace GameAuthoringLab;

internal static class TileMovementDemo
{
    public static int Run(int frames,bool scenario)
    {
        if(scenario&&frames==0)frames=600;
        using var engine=new EngineHost(false,256,legacyTone:false);
        using var physics=engine.OpenPhysics(new(0,18,1f/60,4));
        var root=new AssetRoot();var source=TileMapAsset.LoadAsset(root,"movement.tilemap.json");
        TileMovementLevel level=new(engine,physics,source);
        using var white=engine.Textures.Acquire(root,"regions.bmp");
        var world=new World();var player=world.Create("Player");var goal=world.Create("Trigger");
        player.Sprite=new(22.4f,28.8f,R:.95f,G:.9f,B:.4f,Layer:2);goal.Sprite=new(51.2f,35.2f,R:.3f,G:1,B:.6f,A:.35f,Layer:1);
        var batch=new SpriteBatch(256){RegionResolver=_=>new(white.Handle,new(7,7,1,1))};
        player.Sprite=player.Sprite.Value with {AssetKey="white"};goal.Sprite=goal.Sprite.Value with {AssetKey="white"};
        goal.LocalTransform=new(TileMovementLevel.GoalX*32-25.6f,TileMovementLevel.GoalY*32-17.6f);
        var clock=new TileMovementClock();var camera=new Camera{Zoom=1};
        using var ui=new BoundUiSession<string>(engine,new UiBindings<string>().Text("status",static text=>text));
        ui.LoadAsset(root,"ui/movement.rml");engine.Draw(camera,ReadOnlySpan<SpriteDraw>.Empty);
        var watch=Stopwatch.StartNew();double previous=watch.Elapsed.TotalSeconds;int rendered=0,restarts=0;string oldStatus="";
        string? captures=Environment.GetEnvironmentVariable("GAL_MOVEMENT_CAPTURE_DIR");if(captures is not null)Directory.CreateDirectory(captures);
        Console.WriteLine("TILE MOVEMENT | A/D or arrows: move | Space: jump | E: pause | T: restart | Escape: quit");
        try
        {
            while(frames==0||rendered<frames)
            {
                var input=engine.PollInput();if(input.Quit!=0)break;
                if(scenario&&!input.Drawable){Thread.Sleep(1);continue;}
                double now=watch.Elapsed.TotalSeconds,delta=scenario?TileMovementClock.StepSeconds:Math.Max(0,now-previous);previous=now;
                var routed=scenario?TileMovementTests.ScenarioInput(rendered):input;
                int steps=clock.Advance(routed,delta);if(clock.Quit)break;
                if(clock.RestartRequested)
                {
                    level.Dispose();physics.Step(); // Explicitly reclaim old identities before the replacement scene.
                    level=new(engine,physics,source);restarts++;Console.WriteLine($"TILE MOVEMENT restart={restarts} bodies={physics.State.Bodies}");
                }
                for(int i=0;i<steps;i++)level.Step(clock.Direction,clock.ConsumeJump());
                if(scenario)TileMovementTests.VerifyScenario(rendered,level,clock,restarts);
                var state=level.State;player.LocalTransform=new(state.X*32-11.2f,state.Y*32-14.4f);
                player.Sprite=player.Sprite.Value with {R=level.InGoal?.3f:.95f,G=.9f,B=level.InGoal?1:.4f};
                string status=string.Create(CultureInfo.InvariantCulture,$"{(clock.Suspended?"FOCUS PAUSED":clock.Paused?"PAUSED":clock.WaitingForNeutral?"RELEASE KEYS":"RUNNING")} | x {state.X:F2} y {state.Y:F2} | {(level.Grounded?"grounded":"airborne")} | goal {level.GoalEntries}/{level.GoalExits} | restart {restarts}");
                if(status!=oldStatus){ui.Apply(status);oldStatus=status;}
                if(input.Drawable)
                {
                    world.ExtractSprites(batch);level.Map.AppendSprites(batch,TileView.FromCamera(camera,input.Viewport));
                    if(captures is not null&&scenario&&rendered is 45 or 90 or 225 or 400 or 599)ui.Capture(Path.Combine(captures,$"frame-{rendered:D3}.bmp"));
                    engine.Draw(camera,batch.RegionDraws);rendered++;
                }
                if(!scenario)Thread.Sleep(1);
            }
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,$"TILE MOVEMENT DONE frames={rendered} x={level.State.X:F3} y={level.State.Y:F3} entries={level.GoalEntries} exits={level.GoalExits} restarts={restarts}"));
        }
        finally{level.Dispose();}
        white.Dispose();physics.Step();
        if(physics.State.Bodies!=0||physics.State.Shapes!=0||physics.State.RetiredShapes!=0||engine.Textures.Count!=0)throw new InvalidOperationException("Tile movement resource cleanup failed.");
        Console.WriteLine("TILE MOVEMENT CLEAN bodies=0 shapes=0 retired=0 textures=0");return 0;
    }
}
