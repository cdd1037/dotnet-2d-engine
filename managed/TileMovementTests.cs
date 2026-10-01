namespace GameAuthoringLab;

internal static unsafe class TileMovementTests
{
    internal static InputSnapshot Input(PhysicalKey? key=null,bool down=false,bool pressed=false,bool released=false,bool consumed=false)
    {
        var input=InputTests.Snapshot();
        if(key is {} value)
        {
            int i=(int)value;ulong bit=1ul<<(i%64);
            if(down)input.KeysDown[i/64]|=bit;if(pressed)input.KeysPressed[i/64]|=bit;if(released)input.KeysReleased[i/64]|=bit;
            if(!consumed){if(down)input.GameKeysDown[i/64]|=bit;if(pressed)input.GameKeysPressed[i/64]|=bit;if(released)input.GameKeysReleased[i/64]|=bit;}
        }
        return input;
    }
    internal static InputSnapshot ScenarioInput(int frame)=>frame switch
    {
        30=>Input(PhysicalKey.Space,false,true,true),
        >=90 and <400=>Input(PhysicalKey.D,true,frame==90),
        400 or 440=>Input(PhysicalKey.E,false,true,true),
        >=450 and <560=>Input(PhysicalKey.A,true,frame==450),
        560=>Input(PhysicalKey.T,false,true,true),
        _=>Input()
    };
    internal static void VerifyScenario(int frame,TileMovementLevel level,TileMovementClock clock,int restarts)
    {
        bool valid=frame switch
        {
            29=>level.Grounded,
            45=>!level.Grounded&&level.State.Y<10.6f,
            90=>level.Grounded,
            225=>level.GoalEntries==1&&level.GoalExits==1&&level.State.X>15,
            400 or 430=>clock.Paused&&level.State.X is >24.5f and <24.66f,
            560=>restarts==1&&level.State.X==TileMovementLevel.SpawnX&&level.State.Y==TileMovementLevel.SpawnY,
            599=>restarts==1&&level.Grounded&&level.State.X==TileMovementLevel.SpawnX&&level.GoalEntries==0,
            _=>true
        };
        if(!valid)throw new InvalidOperationException($"Movement scenario checkpoint failed at frame {frame}.");
    }
    public static int RunContracts()
    {
        int count=0;void Check(bool ok,string label){if(!ok)throw new InvalidOperationException("MOVEMENT CLOCK: "+label);count++;}
        var clock=new TileMovementClock();var right=Input(PhysicalKey.D,true,true);var tap=Input(PhysicalKey.Space,false,true,true);double tick=TileMovementClock.StepSeconds;
        Check(clock.Advance(right,tick)==0&&clock.WaitingForNeutral,"startup held movement waits for neutral");
        Check(clock.Advance(Input(),tick)==0&&!clock.WaitingForNeutral,"neutral clears startup gate without simulating old time");
        Check(clock.Advance(right,tick)==1&&clock.Direction==1,"fresh right drives fixed tick");
        Check(clock.Advance(Input(),tick)==1&&clock.Direction==0,"release stops horizontal input");
        Check(clock.Advance(tap,tick/2)==0,"quick jump waits for fixed tick");
        Check(clock.Advance(Input(),tick/2)==1&&clock.ConsumeJump()&&!clock.ConsumeJump(),"quick jump consumed once");
        Check(clock.Advance(tap,.1)==6&&clock.ConsumeJump()&&!clock.ConsumeJump(),"catch-up consumes one jump across six ticks");
        Check(clock.Advance(Input(),10)==6,"long stall caps catch-up to six ticks");
        clock.Advance(tap,0);clock.Advance(Input(PhysicalKey.E,false,true,true),tick);
        Check(clock.Paused&&!clock.ConsumeJump(),"pause clears pending quick tap");
        Check(clock.Advance(right,10)==0&&clock.Direction==0,"pause discards movement and wall time");
        clock.Advance(Input(PhysicalKey.E,false,true,true),tick);Check(!clock.Paused&&clock.WaitingForNeutral,"resume waits for neutral");
        Check(clock.Advance(right,tick)==0,"held movement does not leak through resume");
        clock.Advance(Input(),tick);Check(clock.Advance(right,tick)==1,"fresh movement after resume");
        clock.Advance(tap,tick/2);var lost=Input();lost.Flags&=~InputFlags.Focused;
        Check(clock.Advance(lost,2)==0&&clock.Suspended&&!clock.ConsumeJump(),"focus loss drops pending jump and partial tick");
        Check(clock.Advance(right,tick)==0,"focus regain held input suppressed");
        clock.Advance(Input(),0);Check(clock.Advance(Input(),tick/2)==0,"focus regain accumulated time cleared");
        var minimized=Input();minimized.Flags&=~InputFlags.Drawable;
        Check(clock.Advance(minimized,2)==0&&clock.Suspended,"minimize suspends");
        Check(clock.Advance(right,tick)==0,"restore held input suppressed");
        clock.Advance(Input(),0);clock.Advance(tap,0);
        Check(clock.Advance(Input(PhysicalKey.T,false,true,true),tick)==0&&clock.RestartRequested&&!clock.ConsumeJump(),"restart clears pending input and time");
        Check(clock.Advance(Input(PhysicalKey.D,true,true,consumed:true),tick)==0&&clock.WaitingForNeutral,"raw consumed key still delays neutral gate");
        clock.Advance(Input(),0);clock.Advance(Input(PhysicalKey.D,true,true,consumed:true),tick);
        Check(clock.Direction==0,"consumed movement is suppressed");
        var opposing=Input(PhysicalKey.A,true,true);int r=(int)PhysicalKey.D;opposing.KeysDown[r/64]|=1ul<<(r%64);opposing.GameKeysDown[r/64]|=1ul<<(r%64);
        clock.Advance(opposing,tick);Check(clock.Direction==0,"opposed directions cancel");
        foreach(double invalid in new[]{-1,double.NaN,double.PositiveInfinity})
        {try{clock.Advance(Input(),invalid);throw new InvalidOperationException("invalid delta accepted");}catch(ArgumentOutOfRangeException){count++;}}
        clock.Advance(Input(PhysicalKey.Escape,false,true,true),0);Check(clock.Quit,"quick escape exits");
        for(int i=0;i<128;i++)clock.Advance(Input(),tick);long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++){clock.Advance(right,tick);clock.ConsumeJump();}
        Check(GC.GetAllocatedBytesForCurrentThread()==before,"warmed fixture clock has no per-frame allocation");
        Console.WriteLine($"TILE MOVEMENT CLOCK PASS assertions={count}");return count;
    }
    public static int RunPhysics()
    {
        int count=0;void Check(bool ok,string label){if(!ok)throw new InvalidOperationException("MOVEMENT PHYSICS: "+label);count++;}
        using var engine=new EngineHost(true,256,legacyTone:false);using var physics=engine.OpenPhysics(new(0,18,1f/60,4));
        var source=TileMapAsset.LoadAsset(new AssetRoot(),"movement.tilemap.json");
        var level=new TileMovementLevel(engine,physics,source);
        try
        {
            Check(!level.Grounded,"spawn is airborne");for(int i=0;i<90;i++)level.Step(0,false);
            Check(level.Grounded&&Math.Abs(level.State.Y-11.55f)<.03f&&Math.Abs(level.State.Vy)<.05f,"player stands on tile floor");
            float groundY=level.State.Y;level.Step(0,true);Check(level.State.Vy<-6&&level.State.Y<groundY,"grounded jump");
            for(int i=0;i<10;i++)level.Step(0,false);float prior=level.State.Vy;level.Step(0,true);
            Check(level.State.Vy>prior&&!level.Grounded,"airborne jump attempt is ignored");
            for(int i=0;i<90;i++)level.Step(0,false);Check(level.Grounded&&Math.Abs(level.State.Y-groundY)<.03f,"jump lands on floor");
            for(int i=0;i<150;i++)level.Step(1,false);
            Check(level.State.X>15&&level.GoalEntries==1&&level.GoalExits==1&&!level.InGoal,"walk through goal gives paired enter/exit");
            level.Step(0,false);Check(Math.Abs(level.State.Vx)<.001f,"release stops horizontal velocity");
            for(int i=0;i<300;i++)level.Step(1,false);
            Check(level.State.X>24.5f&&level.State.X<24.66f&&level.Grounded,"holding against right wall cannot pass it");
            for(int i=0;i<500;i++)level.Step(-1,false);
            Check(level.State.X>3.34f&&level.State.X<3.5f&&level.Grounded,"holding against left wall cannot pass it");
            Check(level.GoalEntries==2&&level.GoalExits==2,"reverse crossing repeats sensor events");
            var ray=physics.RayCast(6,8,0,6,TileMovementLevel.PlayerCategory,TileMovementLevel.TerrainCategory);
            Check(ray.Hit&&level.Collision.TryGetRectangle(ray.Shape,out var rectangle)&&rectangle.Y==10,"query resolves authored floor identity");
            Span<ulong> shapes=stackalloc ulong[8];int hits=physics.QueryAabb(11,10.8f,13,12,shapes,TileMovementLevel.PlayerCategory,TileMovementLevel.GoalCategory);
            Check(hits==1,"filtered broad-phase query finds goal only");
            var clock=new TileMovementClock();clock.Advance(Input(),0);var start=level.State;uint steps=level.Steps;
            var lost=Input();lost.Flags=InputFlags.Drawable;
            for(int i=0;i<60;i++)for(int n=clock.Advance(lost,1);n>0;n--)level.Step(clock.Direction,clock.ConsumeJump());
            Check(level.Steps==steps&&level.State.X==start.X&&level.State.Y==start.Y,"focus pause freezes actual solver pose");
            uint expectedBodies=physics.State.Bodies,expectedShapes=physics.State.Shapes;ulong old=level.Player.Id;
            for(int i=0;i<12;i++)
            {
                var disposed=level;level.Dispose();physics.Step();
                Check(physics.State.Bodies==0&&physics.State.Shapes==0&&physics.State.RetiredShapes==0&&engine.Textures.Count==0,"restart releases entire scene");
                try{_ =disposed.State;throw new InvalidOperationException("disposed level accepted");}catch(ObjectDisposedException){count++;}
                level=new(engine,physics,source);
                Check(level.Player.Id!=old&&level.State.X==TileMovementLevel.SpawnX&&level.State.Y==TileMovementLevel.SpawnY&&level.State.Vx==0&&level.State.Vy==0&&level.GoalEntries==0&&level.GoalExits==0,"restart restores spawn and fresh identity");
                Check(physics.State.Bodies==expectedBodies&&physics.State.Shapes==expectedShapes&&engine.Textures.Count==1,"restart resource counts remain bounded");old=level.Player.Id;
            }
        }
        finally{level.Dispose();}
        physics.Step();Check(physics.State.Bodies==0&&physics.State.Shapes==0&&physics.State.RetiredShapes==0&&engine.Textures.Count==0,"final disposal has no live or retired resources");
        Console.WriteLine($"TILE MOVEMENT PHYSICS PASS assertions={count}");return count;
    }
}
