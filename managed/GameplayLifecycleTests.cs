namespace GameAuthoringLab;

// Executable experiments, not a new public component/lifecycle API.
internal static class GameplayLifecycleTests
{
    public static int Run()
    {
        int assertions=0;
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException("LIFECYCLE: "+label);assertions++;}
        var world=new World();var scene=world.CreateScene("arena");var order=new List<string>();
        Entity player=world.Create("player"),enemy=world.Create("enemy",scene);Entity? pickup=null;
        player.Behavior=new Callback((self,dt)=>
        {
            order.Add("player");
            if(pickup is not null)return;
            world.Destroy(enemy);
            pickup=world.Create("pickup",scene);
            pickup.Behavior=new Callback((e,d)=>order.Add("pickup"));
        });
        enemy.Behavior=new Callback((e,d)=>order.Add("enemy"));
        world.Update(.1f);
        Check(string.Join(",",order)=="player"&&!enemy.IsAlive&&pickup!.IsAlive,"destroyed enemy skipped; spawned pickup deferred");
        order.Clear();world.Update(.1f);Check(string.Join(",",order)=="player,pickup","creation-order updates next tick");
        world.PickUp(pickup!,player);world.UnloadScene(scene);
        Check(player.IsAlive&&pickup!.IsAlive,"player and owned pickup persist across room unload");
        world.Destroy(player);Check(!player.IsAlive&&!pickup!.IsAlive&&world.EntityCount==0,"explicit owner destruction includes persistent pickup");

        // Clearing Behavior is insufficient to unregister delegates held by an external publisher.
        var signal=new Signal();scene=world.CreateScene("subscription-boundary");enemy=world.Create("enemy",scene);
        var unscoped=new ListeningBehavior(signal);enemy.Behavior=unscoped;
        world.UnloadScene(scene);signal.Fire();
        Check(unscoped.Events==1&&signal.Count==1,"baseline documents stale subscriber after unload (no automatic Dispose)");
        unscoped.Dispose();signal.Fire();Check(unscoped.Events==1&&signal.Count==0,"explicit detach removes subscriber");

        // Small host-owned scope solution: subscriptions retire BEFORE scene objects.
        for(int i=0;i<20;i++)
        {
            scene=world.CreateScene("arena");enemy=world.Create("enemy",scene);
            using var listener=new ListeningBehavior(signal);enemy.Behavior=listener;
            signal.Fire();Check(listener.Events==1,"new scene listener active once");
            listener.Dispose();world.UnloadScene(scene);signal.Fire();
            Check(listener.Events==1&&signal.Count==0&&world.EntityCount==0,"repeated room boundary leaves no callbacks or entities");
        }
        scene=world.CreateScene("replacement");enemy=world.Create("enemy",scene);
        using var old=new ListeningBehavior(signal);enemy.Behavior=old;
        old.Dispose();using var replacement=new ListeningBehavior(signal);enemy.Behavior=replacement;
        signal.Fire();Check(old.Events==0&&replacement.Events==1&&signal.Count==1,"explicit replacement detaches old behavior");
        replacement.Dispose();world.Destroy(enemy);Check(signal.Count==0,"destroy boundary detaches external subscription");

        // Pause must discard substep actions and elapsed backlog, independently of native UI.
        var game=new RoomGame();game.Player.LocalTransform=new Transform2D(230,280);
        var gate=new ModalGameInput(game);game.Advance(Native.Interact,RoomGame.FixedDelta/4);gate.Pause();
        var before=game.Player.LocalTransform;gate.Advance(Native.Right|Native.Interact,30);
        Check(game.Player.LocalTransform==before&&game.Held is null,"modal blocks active keys");
        gate.Resume();gate.Advance(Native.Right,30);gate.Advance(0,30);gate.Advance(Native.Right,RoomGame.FixedDelta);
        Check(game.Player.LocalTransform.X==before.X+3&&game.Held is null&&game.LastSteps==1,"resume clears pending action and backlog, waits for release");
        Console.WriteLine($"PASS gameplay lifecycle experiments ({assertions} assertions; external subscription cleanup is explicit)");return assertions;
    }
    private sealed class Callback(Action<Entity,float> run):IBehavior {public void Update(Entity entity,float dt)=>run(entity,dt);}
    private sealed class Signal
    {
        public event Action? Tick;
        public int Count=>Tick?.GetInvocationList().Length??0;
        public void Fire()=>Tick?.Invoke();
    }
    private sealed class ListeningBehavior:IBehavior,IDisposable
    {
        private Signal? _source;public int Events {get;private set;}
        public ListeningBehavior(Signal source){_source=source;source.Tick+=OnTick;}
        private void OnTick()=>Events++;
        public void Update(Entity entity,float dt){}
        public void Dispose(){if(_source is null)return;_source.Tick-=OnTick;_source=null;}
    }
}
