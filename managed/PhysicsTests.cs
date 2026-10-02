using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
namespace GameAuthoringLab;
internal static unsafe class PhysicsTests
{
    public static int RunContracts()
    {
        int n=0;void Check(bool ok,string label){if(!ok)throw new Exception("PHYSICS: "+label);n++;}
        void Reject(Action action){try{action();}catch(ArgumentOutOfRangeException){n++;return;}throw new Exception("PHYSICS invalid definition accepted");}
        Check(sizeof(PhysicsConfig)==32&&sizeof(PhysicsBodyDef)==56&&sizeof(PhysicsShapeDef)==72,"config/body/shape ABI");
        Check(sizeof(PhysicsBodyState)==40&&sizeof(PhysicsStep)==16&&sizeof(PhysicsEvent)==40&&sizeof(PhysicsRay)==40&&sizeof(PhysicsRayHit)==48&&sizeof(PhysicsAabb)==40&&sizeof(PhysicsState)==32,"result/query ABI");
        Check(Marshal.OffsetOf<PhysicsShapeDef>(nameof(PhysicsShapeDef.Category)).ToInt32()==48&&Marshal.OffsetOf<PhysicsEvent>(nameof(PhysicsEvent.ShapeA)).ToInt32()==8,"64-bit field offsets");
        var scale=new PhysicsScale(50);Check(scale.ToMeters(250)==5&&scale.ToPixels(-3)==-150,"explicit conversion without axis inversion");
        Reject(()=>new PhysicsScale(0));Reject(()=>default(PhysicsScale).ToMeters(1));Reject(()=>scale.ToPixels(float.MaxValue));
        Reject(()=>new PhysicsSettings(StepSeconds:0).Validate());Reject(()=>new PhysicsSettings(Substeps:9).Validate());
        Reject(()=>new PhysicsBodyDefinition(PhysicsBodyType.Dynamic,X:float.NaN).Validate());Reject(()=>new PhysicsBodyDefinition(PhysicsBodyType.Static,Vx:1).Validate());
        Reject(()=>new PhysicsShapeDefinition(PhysicsShapeType.Circle,0).Validate());Reject(()=>new PhysicsShapeDefinition(PhysicsShapeType.Circle,1,1).Validate());Reject(()=>new PhysicsShapeDefinition(PhysicsShapeType.Box,1,1,Friction:-1).Validate());
        var root=new AssetRoot();var fixture=PhysicsFixture.LoadAsset(root,"basics.physics.json");Check(fixture.Bodies.Count==6&&fixture.Settings.Substeps==4,"strict authored fixture");
        var json=JsonNode.Parse(File.ReadAllText(root.Resolve("basics.physics.json")))!;json["bodies"]![0]!["a"]=0;
        try{PhysicsFixture.Load(json.ToJsonString(),"test");throw new Exception("Bad fixture accepted");}catch(PhysicsFixtureException){n++;}
        n+=PhysicsShapeFactoryTests.RunContracts();
        n+=PhysicsExtensionTests.RunContracts();
        Console.WriteLine($"PHYSICS CONTRACT PASS assertions={n}; layouts, units and sourcegen only");return n;
    }
    public static int RunSimulation()
    {
        int n=0;void Check(bool ok,string label){if(!ok)throw new Exception("PHYSICS SIM: "+label);n++;}
        void Reject<T>(Action action,string label) where T:Exception{try{action();}catch(T){n++;return;}throw new Exception("PHYSICS accepted: "+label);}
        using var engine=new EngineHost(true,16);ulong stale=0;
        using(var physics=engine.OpenPhysics())
        {
            using var ground=physics.CreateBody(new(PhysicsBodyType.Static,0,10));var groundShape=ground.AddShape(new(PhysicsShapeType.Box,10,.5f));
            using var ball=physics.CreateBody(new(PhysicsBodyType.Dynamic,0,0));using var circle=ball.AddShape(new(PhysicsShapeType.Circle,.5f));stale=ball.Id;
            bool began=false;for(int i=0;i<240;i++){var step=physics.Step();Check(step.Dropped==0,"no dropped floor events");foreach(var e in physics.Events)began|=e.Type==PhysicsEventType.ContactBegin;}
            Check(began&&ball.State.Y is >8.9f and <9.1f,"falling circle rests on static box and emits begin");
            for(int i=0;i<120;i++)physics.Step();Check(!ball.State.Awake,"resting body sleeps");
            ball.Teleport(0,0);physics.Step();Check(physics.Events.ToArray().Any(e=>e.Type==PhysicsEventType.ContactEnd),"teleport emits contact end");
            groundShape.Dispose();Check(physics.State is {Shapes:1,RetiredShapes:1},"shape destruction retains event identity until step");physics.Step();Check(physics.State.RetiredShapes==0,"retired shape slot collected after copy");
            Reject<InvalidOperationException>(()=>ground.SetVelocity(1,0),"static velocity unsupported");
            Reject<InvalidOperationException>(()=>ground.ApplyImpulse(1,0),"impulse requires dynamic body");
        }
        using(var physics=engine.OpenPhysics(new(0,0,1f/60,4)))
        {
            using var kinematic=physics.CreateBody(new(PhysicsBodyType.Kinematic,1,2,Vx:2));kinematic.AddShape(new(PhysicsShapeType.Box,.5f,.5f));
            for(int i=0;i<60;i++)physics.Step();Check(Math.Abs(kinematic.State.X-3)<.001f&&kinematic.State.Y==2,"kinematic velocity with no gravity integration");
            using var dynamicBody=physics.CreateBody(new(PhysicsBodyType.Dynamic,0,0,FixedRotation:true));dynamicBody.AddShape(new(PhysicsShapeType.Circle,.5f));
            dynamicBody.ApplyImpulse(1,0);physics.Step();float before=dynamicBody.State.Vx;Check(before>0,"impulse changes velocity");
            dynamicBody.ApplyForce(10,0);physics.Step();Check(dynamicBody.State.Vx>before,"force affects next step");dynamicBody.SetVelocity(0,0,2);physics.Step();Check(dynamicBody.State.AngularVelocity==0,"fixed rotation retained");
        }
        using(var physics=engine.OpenPhysics(new(0,0,1f/60,4)))
        {
            using var sensor=physics.CreateBody(new(PhysicsBodyType.Static,4,2));using var sensorShape=sensor.AddShape(new(PhysicsShapeType.Box,1,1,Sensor:true,Density:0));
            using var visitor=physics.CreateBody(new(PhysicsBodyType.Dynamic,4,0,Vy:4));using var visitorShape=visitor.AddShape(new(PhysicsShapeType.Circle,.2f));
            bool began=false,ended=false;for(int i=0;i<70;i++){physics.Step();foreach(var e in physics.Events){began|=e.Type==PhysicsEventType.SensorBegin;ended|=e.Type==PhysicsEventType.SensorEnd;}}
            Check(began&&ended&&visitor.State.Vy==4,"sensor begin/end without collision response");
            visitor.Teleport(4,2);visitor.SetVelocity(0,0);physics.Step();ulong deadShape=visitorShape.Id,deadBody=visitor.Id;var copied=physics.Events.ToArray();visitor.Dispose();
            Check(copied.Any(e=>e.Type==PhysicsEventType.SensorBegin)&&physics.Events.Length==copied.Length,"managed copied events survive body mutation");
            physics.Step();Check(physics.Events.ToArray().Any(e=>e.Type==PhysicsEventType.SensorEnd&&e.ShapeB==deadShape&&e.BodyB==deadBody&&e.RemovedB),"destroyed visitor end retains retired shape/body IDs");
        }
        using(var physics=engine.OpenPhysics())
        {
            using var floor=physics.CreateBody(new(PhysicsBodyType.Static,0,3));floor.AddShape(new(PhysicsShapeType.Box,10,.2f));
            using var ignored=physics.CreateBody(new(PhysicsBodyType.Dynamic,0,0));ignored.AddShape(new(PhysicsShapeType.Circle,.2f,Category:2,Mask:0));
            for(int i=0;i<90;i++)physics.Step();Check(ignored.State.Y>5,"mask prevents collision response");
        }
        using(var physics=engine.OpenPhysics())
        {
            using var floor=physics.CreateBody(new(PhysicsBodyType.Static,0,3));floor.AddShape(new(PhysicsShapeType.Box,10,.2f,Mask:0,Group:7));
            using var ball=physics.CreateBody(new(PhysicsBodyType.Dynamic,0,0));ball.AddShape(new(PhysicsShapeType.Circle,.2f,Mask:0,Group:7));
            for(int i=0;i<120;i++)physics.Step();Check(ball.State.Y is >2.5f and <2.7f,"positive group overrides empty masks");
        }
        using(var physics=engine.OpenPhysics(new(0,0,1f/60,4)))
        {
            using var left=physics.CreateBody(new(PhysicsBodyType.Dynamic,-2,0,Vx:5));left.AddShape(new(PhysicsShapeType.Circle,.5f,Restitution:1,Friction:0));
            using var right=physics.CreateBody(new(PhysicsBodyType.Dynamic,2,0,Vx:-5));right.AddShape(new(PhysicsShapeType.Circle,.5f,Restitution:1,Friction:0));
            for(int i=0;i<50;i++)physics.Step();Check(left.State.Vx<0&&right.State.Vx>0,"dynamic circles collide and rebound");
        }
        using(var physics=engine.OpenPhysics(new(0,0,1f/60,4)))
        {
            using var body=physics.CreateBody(new(PhysicsBodyType.Static));using var first=body.AddShape(new(PhysicsShapeType.Box,1,1,Category:1));using var second=body.AddShape(new(PhysicsShapeType.Circle,.5f,OffsetX:4,Category:2));
            var ray=physics.RayCast(-5,0,10,0);Check(ray.Hit&&ray.Shape==first.Id&&Math.Abs(ray.X+1)<.01f&&ray.NormalX<-.9f,"closest ray identity/point/normal");
            Check(physics.RayCast(-5,0,10,0,mask:2).Shape==second.Id,"ray category mask");
            Check(physics.RayCast(0,0,1,0).Hit==false,"closest ray explicitly ignores initial overlap");
            Check(physics.RayCast(-5,3,10,0).Hit==false,"ray miss");
            ulong[] hits=new ulong[512];int count=physics.QueryAabb(-2,-2,5,2,hits);Check(count==2&&hits[0]==first.Id&&hits[1]==second.Id,"bounded broad-phase query sorted by engine ID");
            var query=new PhysicsAabb{Size=40,Version=1,LowerX=-2,LowerY=-2,UpperX=5,UpperY=2,Category=ulong.MaxValue,Mask=ulong.MaxValue};ulong sentinel=777;uint required=0;
            Check(PhysicsNative.Aabb(engine.NativeContext,&query,&sentinel,1,&required)!=0&&required==2&&sentinel==777,"insufficient query output leaves array intact");
            Reject<ArgumentException>(()=>physics.RayCast(0,0,0,0),"zero ray");Reject<ArgumentException>(()=>physics.QueryAabb(2,0,1,1,hits),"inverted bounds");
            var invalid=new PhysicsBodyDef{Size=56,Version=1,Type=PhysicsBodyType.Dynamic,X=float.NaN,GravityScale=1};ulong rejected=99;Check(PhysicsNative.CreateBody(engine.NativeContext,&invalid,&rejected)!=0&&rejected==0,"native nonfinite input preflight");
            var cam=new Camera{Zoom=1};Native.Check(Native.Begin(engine.NativeContext,&cam),"physics frame guard");var step=new PhysicsStep{Size=16};Check(PhysicsNative.Step(engine.NativeContext,&step)!=0,"step rejected during draw frame");Native.Check(Native.Abort(engine.NativeContext),"physics abort");
        }
        using(var physics=engine.OpenPhysics(new(0,0,1f/60,4)))
        {
            using var scope=new PhysicsScope(physics);
            for(int i=0;i<33;i++)scope.CreateBody(new(PhysicsBodyType.Static)).AddShape(new(PhysicsShapeType.Circle,2,Category:1,Mask:2,Sensor:true,Density:0));
            for(int i=0;i<33;i++)scope.CreateBody(new(PhysicsBodyType.Dynamic)).AddShape(new(PhysicsShapeType.Circle,.1f,Category:2,Mask:1,Group:-1));
            var step=physics.Step();Check(step.Events==1024&&step.Dropped>0&&step.Index==1,"event overflow reported after exactly one completed step");
            Check(physics.Step().Index==2,"overflow does not require replay or corrupt next step");
        }
        using(var physics=engine.OpenPhysics(new(0,0,1f/60,4)))
        {
            using var owner=physics.CreateBody(new(PhysicsBodyType.Static));var shapes=new List<PhysicsShape>();for(int i=0;i<512;i++)shapes.Add(owner.AddShape(new(PhysicsShapeType.Circle,.1f)));
            Reject<InvalidOperationException>(()=>owner.AddShape(new(PhysicsShapeType.Circle,.1f)),"shape cap");foreach(var shape in shapes)shape.Dispose();
            Reject<InvalidOperationException>(()=>owner.AddShape(new(PhysicsShapeType.Circle,.1f)),"retired mapping occupies slot until step");physics.Step();using var reused=owner.AddShape(new(PhysicsShapeType.Circle,.1f));Check(physics.State is {Shapes:1,RetiredShapes:0},"shape slots reusable after post-step copy");
        }
        using(var physics=engine.OpenPhysics(new(0,0,1f/60,4)))
        {
            var all=new List<PhysicsBody>();for(int i=0;i<256;i++)all.Add(physics.CreateBody(new(PhysicsBodyType.Static)));
            Reject<InvalidOperationException>(()=>physics.CreateBody(new(PhysicsBodyType.Static)),"body cap");foreach(var body in all)body.Dispose();
            using var reused=physics.CreateBody(new(PhysicsBodyType.Static,Angle:.22f));Check(physics.State.Bodies==1&&Math.Abs(reused.State.Angle-.22f)<.002f,"body slot reuse and Box2D rotation within documented trig approximation");
        }
        float[]? previousFixture=null;
        for(int iteration=0;iteration<2;iteration++)
        {
            var fixture=PhysicsFixture.LoadAsset(new AssetRoot(),"basics.physics.json");using var physics=engine.OpenPhysics(fixture.Settings);using var scope=new PhysicsScope(physics);
            var bodies=fixture.Bodies.Select(source=>{var body=scope.CreateBody(source.BodyDefinition);body.AddShape(source.ShapeDefinition);return body;}).ToArray();
            for(int i=0;i<180;i++)physics.Step();var positions=bodies.SelectMany(body=>new[]{body.State.X,body.State.Y,body.State.Angle}).ToArray();
            Check(previousFixture is null||positions.SequenceEqual(previousFixture),"independent reload repeats same-binary fixed-input fixture result");previousFixture=positions;
        }
        using(var physics=engine.OpenPhysics(new(0,0,1f/60,4)))
        {
            using var persistent=physics.CreateBody(new(PhysicsBodyType.Dynamic));persistent.AddShape(new(PhysicsShapeType.Circle,.2f));var world=new World();
            for(int i=0;i<3;i++){var scene=world.CreateScene("physics scene");var owner=world.Create("physics owner",scene);var scope=new PhysicsScope(physics);world.AttachBehavior(owner,new Owner(),s=>s.OnDetach(scope.Dispose));scope.CreateBody(new(PhysicsBodyType.Dynamic,3,0)).AddShape(new(PhysicsShapeType.Box,.3f,.3f));world.UnloadScene(scene);physics.Step();Check(physics.State.Bodies==1&&physics.State.Shapes==1,"scene scope unload preserves independent body");}
            for(int i=0;i<128;i++)physics.Step();long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++){physics.Step();_ =persistent.State;_ =physics.Events.Length;}
            Check(GC.GetAllocatedBytesForCurrentThread()==before,"warmed step/state/event copy allocates zero managed bytes");
            var raw=new PhysicsBodyState{Size=40};Check(PhysicsNative.GetBody(engine.NativeContext,stale,&raw)!=0,"old world body handle rejected after reopen");
            Exception? failure=null;var thread=new Thread(()=>{try{physics.Step();}catch(Exception e){failure=e;}});thread.Start();thread.Join();Check(failure is InvalidOperationException,"physics owner thread enforced");
            physics.Dispose();Reject<ObjectDisposedException>(()=>persistent.State.ToString(),"closed world invalidates managed body");persistent.Dispose();
        }
        using(var physics=engine.OpenPhysics())
        {var body=physics.CreateBody(new(PhysicsBodyType.Dynamic));var shape=body.AddShape(new(PhysicsShapeType.Circle,1));engine.Dispose();shape.Dispose();body.Dispose();Check(true,"engine teardown and late body/shape disposal safe");}
        n+=PhysicsShapeFactoryTests.RunSimulation();
        n+=PhysicsExtensionTests.RunSimulation();
        Console.WriteLine($"PHYSICS SIMULATION PASS assertions={n}; real single-threaded Box2D, no cross-platform determinism claim");return n;
    }
    private sealed class Owner:IBehavior{public void Update(Entity entity,float deltaSeconds){}}
}
