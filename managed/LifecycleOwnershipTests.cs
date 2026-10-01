namespace GameAuthoringLab;
internal static class LifecycleOwnershipTests
{
    public static int Run()
    {
        int count=0;void Check(bool ok,string name){if(!ok)throw new InvalidOperationException("LIFETIME: "+name);count++;}
        void Fails(Action action,string name){try{action();}catch(AggregateException){count++;return;}throw new InvalidOperationException(name);}
        var world=new World();var room=world.CreateScene("room");var entity=world.Create("enemy",room);
        var order=new List<int>();var first=new Behavior();var next=new Behavior();
        world.AttachBehavior(entity,first,s=>{s.OnDetach(()=>order.Add(1));s.OnDetach(()=>order.Add(2));});
        entity.Behavior=first;Check(order.Count==0,"same-instance assignment does not detach");
        world.AttachBehavior(entity,next,s=>s.OnDetach(()=>order.Add(3)));
        Check(entity.Behavior==next&&string.Join(",",order)=="2,1","replacement commits then reverse cleanup");
        entity.Behavior=null;Check(string.Join(",",order)=="2,1,3","plain replacement retires registered ownership");
        entity.Behavior=null;Check(order.Count==3,"cleanup exactly once");
        BehaviorLifetime? escaped=null;world.AttachBehavior(entity,first,s=>escaped=s);
        bool sealedScope=false;try{escaped!.OnDetach(()=>{});}catch(InvalidOperationException){sealedScope=true;}Check(sealedScope,"registration ends after attach");
        Fails(()=>world.AttachBehavior(entity,next,s=>{s.OnDetach(()=>order.Add(4));throw new InvalidOperationException("setup");}),"setup error expected");
        Check(entity.Behavior==first&&order[^1]==4,"failed attachment rolls candidate back, retains old");
        Fails(()=>world.AttachBehavior(entity,next,s=>{s.OnDetach(()=>throw new InvalidOperationException("rollback"));throw new InvalidOperationException("setup");}),"double error expected");
        Check(entity.Behavior==first,"rollback failure preserves original attachment");

        world.AttachBehavior(entity,first,s=>{s.OnDetach(()=>order.Add(5));s.OnDetach(()=>throw new InvalidOperationException("cleanup"));});
        Fails(()=>world.AttachBehavior(entity,next,s=>s.OnDetach(()=>order.Add(6))),"old cleanup failure expected");
        Check(entity.Behavior==next&&order[^1]==5,"cleanup failure does not undo committed replacement or skip other cleanup");
        int blocked=0;
        world.AttachBehavior(entity,next,s=>s.OnDetach(()=>
        {
            Action[] mutations=[()=>world.Create("illegal"),()=>world.CreateScene("illegal"),()=>world.Update(0),()=>world.Destroy(entity),()=>entity.Behavior=null,()=>entity.Sprite=null,()=>entity.LocalTransform=Transform2D.Identity,()=>world.ExtractSprites(new SpriteBatch())];
            foreach(var mutation in mutations)try{mutation();}catch(InvalidOperationException){blocked++;}
        }));
        entity.Behavior=null;Check(blocked==8&&world.EntityCount==1,"cleanup cannot reenter world mutations or update/extraction");
        Fails(()=>world.AttachBehavior(entity,next,s=>world.Destroy(entity)),"attach reentrancy expected");
        Check(entity.IsAlive&&entity.Behavior is null,"attach cannot destroy its target");

        bool committed=false;int cleanups=0;
        world.AttachBehavior(entity,first,s=>s.OnDetach(()=>{committed=!entity.IsAlive&&!room.IsLoaded&&!world.TryGet(entity.Id,out _);cleanups++;throw new Exception("detach");}));
        Entity child=world.CreateChild(entity,"child");world.AttachBehavior(child,next,s=>s.OnDetach(()=>cleanups++));
        Fails(()=>world.UnloadScene(room),"unload cleanup failure expected");
        Check(committed&&cleanups==2&&world.EntityCount==0,"unload fully commits, all entities cleaned despite errors");
        Check(world.Create("after-error").IsAlive,"mutation guard recovered after cleanup error");

        // Destruction during Update remains valid, even when callback failure aborts the tick.
        var ticking=new World();Entity actor=ticking.Create("actor"),later=ticking.Create("later");
        world=new World(); // independent scope state must not affect another world
        ticking.AttachBehavior(actor,new Behavior(()=>ticking.Destroy(actor)),s=>s.OnDetach(()=>throw new Exception("expected")));
        later.Behavior=new Behavior();Fails(()=>ticking.Update(.1f),"update cleanup error expected");
        Check(!actor.IsAlive&&ticking.EntityCount==1,"update finally compacts destroyed actor");
        ticking.Update(.1f);Check(((Behavior)later.Behavior!).Ticks==1,"update guard recovered after cleanup failure");

        var signal=new Signal();world=new World();
        for(int i=0;i<25;i++)
        {
            room=world.CreateScene("scene");entity=world.Create("enemy",room);int hits=0;Action handler=()=>hits++;
            world.AttachBehavior(entity,new Behavior(),s=>{s.OnDetach(()=>signal.Tick-=handler);signal.Tick+=handler;});
            signal.Fire();world.UnloadScene(room);signal.Fire();Check(hits==1&&signal.Count==0,"owned subscription does not survive scene unload");
        }
        // Numeric teardown preflight must fail before ownership or callbacks change.
        var atomic=new World();var doomedRoom=atomic.CreateScene("overflow");var parent=atomic.Create("parent",doomedRoom,new Transform2D(0,0,1e30f,1e30f));
        var survivor=atomic.Create("survivor",transform:new Transform2D(0,0,1e30f,1e30f));atomic.Reparent(survivor,parent,false);int releases=0;
        atomic.AttachBehavior(parent,new Behavior(),s=>s.OnDetach(()=>releases++));
        bool rejected=false;try{atomic.UnloadScene(doomedRoom);}catch(ArgumentOutOfRangeException){rejected=true;}
        Check(rejected&&parent.IsAlive&&doomedRoom.IsLoaded&&releases==0,"failed transform preflight preserves attachment and world");
        survivor.LocalTransform=Transform2D.Identity;atomic.UnloadScene(doomedRoom);
        Check(releases==1&&!parent.IsAlive&&survivor.IsAlive,"repaired teardown releases once");

        // Pickup transfers entity lifetime, not attachment lifetime; explicit destruction ends it.
        var owner=world.Create("player");room=world.CreateScene("pickup-room");var item=world.Create("pickup",room);int detached=0;
        world.AttachBehavior(item,new Behavior(),s=>s.OnDetach(()=>detached++));world.PickUp(item,owner);world.UnloadScene(room);
        Check(detached==0&&item.IsAlive,"persistent pickup keeps attachment across unload");world.Destroy(owner);Check(detached==1,"owner destruction retires pickup attachment");
        Console.WriteLine($"PASS explicit behavior ownership ({count} assertions)");return count;
    }
    private sealed class Behavior(Action? action=null):IBehavior
    {
        public int Ticks;public void Update(Entity entity,float dt){Ticks++;action?.Invoke();}
    }
    private sealed class Signal {public event Action? Tick;public int Count=>Tick?.GetInvocationList().Length??0;public void Fire()=>Tick?.Invoke();}
}
