namespace GameAuthoringLab;

internal static class RoomGameTests
{
    private static int _count;
    public static int Run()
    {
        _count=0;var catalog=SampleAssets.Catalog();
        foreach(string key in new[]{"room-a","room-b","player","cell","status-empty","status-held","status-restored"})Check(File.Exists(catalog.PathFor(key)),"registered BMP exists");
        var game=new RoomGame();RoomGame restored=Exercise(game,catalog);
        string saved=restored.Save();
        Expect(()=>RoomGame.Load("{broken",catalog));
        Expect(()=>RoomGame.Load(saved.Replace("room-b","missing-art",StringComparison.Ordinal),catalog));
        Check(restored.Player.IsAlive&&restored.RoomIndex==1,"failed load does not mutate current game");
        Expect(()=>RoomGame.Load(Change(saved,"Player",e=>{e["transform"]!["x"]=420;e["transform"]!["y"]=200;}),catalog));
        Expect(()=>RoomGame.Load(Change(saved,"Player",e=>e["transform"]!["rotation"]=.2),catalog));
        Expect(()=>RoomGame.Load(Change(saved,"Player",e=>e["sprite"]!["width"]=100),catalog));
        Expect(()=>RoomGame.Load(Change(saved,"Status",e=>e["sceneId"]=restored.ActiveScene.PersistentId.ToString()),catalog));
        Expect(()=>RoomGame.Load(Change(saved,"Status",e=>{e["sprite"]=null;e["transform"]!["scaleX"]=3e38;}),catalog));
        Expect(()=>RoomGame.Load(Change(saved,"RoomBackdrop",e=>e["transform"]!["rotation"]=.1),catalog));
        var fixedGame=new RoomGame();float x=fixedGame.Player.LocalTransform.X;
        fixedGame.Advance(Native.Right,1f);Check(fixedGame.LastSteps==8,"fixed-step backlog capped");Check(MathF.Abs(fixedGame.Player.LocalTransform.X-x-24)<.01f,"capped fixed-step movement");
        var a=new RoomGame();var b=new RoomGame();a.Advance(Native.Right,1f/30);b.Advance(Native.Right,1f/60);b.Advance(Native.Right,1f/60);Check(a.Player.LocalTransform==b.Player.LocalTransform,"fixed-step partition equivalence");
        Expect(()=>fixedGame.Advance(0,float.NaN));
        FrameAllocations(catalog);
        Console.WriteLine("PASS two-room movement/AABB, pickup, transition, drop, persistence, missing assets and bounded fixed-step");return _count;
    }
    public static RoomGame Exercise(RoomGame game,AssetCatalog catalog,Action<RoomGame>? frame=null)
    {
        Guid player=game.Player.PersistentId,item=game.Item.PersistentId;EntityId oldPlayer=game.Player.Id;Scene oldScene=game.ActiveScene;
        Entity floor=game.World.Entities.Single(e=>e.Name=="RoomBackdrop");frame?.Invoke(game);
        for(int i=0;i<23;i++)Tick(game,Native.Right,frame);
        Transform2D itemBefore=game.Item.WorldTransform;Check(game.PickUp(),"pickup within range");Near(game.Item.WorldTransform,itemBefore);Check(game.Item.PersistentId==item,"pickup identity");frame?.Invoke(game);
        string heldSave=game.Save();RoomGame heldReload=RoomGame.Load(heldSave,catalog);Check(heldReload.Held==heldReload.Item&&heldReload.Item.TransformParent==heldReload.Player,"held relationship survives save");
        for(int i=0;i<30;i++)Tick(game,Native.Up,frame);for(int i=0;i<80;i++)Tick(game,Native.Right,frame);
        Check(game.CollisionCount>0&&game.Player.LocalTransform.X<=364.01f,"AABB blocks workbench");
        for(int i=0;i<37;i++)Tick(game,Native.Down,frame);for(int i=0;i<151;i++)Tick(game,Native.Right,frame);
        Check(game.UseDoor(),"door transition in range");Check(game.RoomIndex==1&&!oldScene.IsLoaded&&!floor.IsAlive,"old room fixtures unload");
        Check(game.Player.PersistentId==player&&game.Item.PersistentId==item&&game.Item.IsAlive&&game.Held==game.Item,"player and carried item survive transition");frame?.Invoke(game);
        for(int i=0;i<44;i++)Tick(game,Native.Right,frame);
        Transform2D dropped=game.Item.WorldTransform;Check(game.Drop(),"drop carried item");Near(game.Item.WorldTransform,dropped);Check(game.Item.Scene==game.ActiveScene&&game.Item.TransformParent is null&&game.Item.LifetimeOwner is null,"drop has room lifetime");frame?.Invoke(game);
        string json=game.Save();RoomGame restored=RoomGame.Load(json,catalog);
        Check(restored.Player.PersistentId==player&&restored.Item.PersistentId==item&&restored.Player.Id!=oldPlayer,"persistent IDs survive restart, runtime IDs change");
        Near(restored.Item.WorldTransform,dropped);Check(restored.Held is null&&restored.RoomIndex==1&&restored.TransitionCount==1&&restored.PickupCount==1,"game state restored");frame?.Invoke(restored);
        // A dropped cell remains the same logical entity while its room is inactive.
        RoomGame revisit=RoomGame.Load(json,catalog);for(int i=0;i<45;i++)revisit.Step(Native.Left);
        Check(revisit.UseDoor()&&revisit.Item.Sprite is null,"inactive room item hidden without destruction");
        Check(revisit.Item.PersistentId==item&&revisit.Item.IsAlive,"inactive room cell identity retained");
        string hiddenSave=revisit.Save();
        Expect(()=>RoomGame.Load(Change(hiddenSave,"PowerCell",e=>e["transform"]!["scaleX"]=1e38),catalog));
        Expect(()=>RoomGame.Load(Change(hiddenSave,"PowerCell",e=>{e["transform"]!["scaleX"]=1.3e37;e["transform"]!["rotation"]=Math.PI/4;}),catalog));
        for(int i=0;i<3;i++)revisit.Step(Native.Right);
        Check(revisit.UseDoor()&&revisit.Item.Sprite?.AssetKey=="cell","returning restores dropped cell");Near(revisit.Item.WorldTransform,dropped);
        return restored;
    }
    private static void FrameAllocations(AssetCatalog catalog)
    {
        using var engine=new EngineHost(true,64);using var bank=new TextureBank(engine,catalog);
        var game=new RoomGame();var batch=new SpriteBatch(64){TextureResolver=bank.Resolve};var camera=new Camera{Zoom=1};
        for(int i=0;i<128;i++){game.Advance(0,RoomGame.FixedDelta);bank.Sync(game.World);game.World.ExtractSprites(batch);engine.Draw(camera,batch.Draws);}
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++){game.Advance(0,RoomGame.FixedDelta);bank.Sync(game.World);game.World.ExtractSprites(batch);engine.Draw(camera,batch.Draws);}
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;Check(bytes==0,"room simulation/resource-sync/affine batch loop allocates zero bytes");
        Console.WriteLine($"PASS room frame allocations bytes={bytes} frames=1000 fixed_step=60Hz");
    }
    private static string Change(string json,string name,Action<System.Text.Json.Nodes.JsonObject> edit)
    {
        var document=System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        var entity=document["entities"]!.AsArray().Single(e=>e!["name"]!.GetValue<string>()==name)!.AsObject();edit(entity);return document.ToJsonString();
    }
    private static void Tick(RoomGame g,uint keys,Action<RoomGame>? frame){g.Step(keys);frame?.Invoke(g);}
    private static void Near(Transform2D a,Transform2D b)
    {
        a.GetBasis(out float a1,out float a2,out float a3,out float a4);b.GetBasis(out float b1,out float b2,out float b3,out float b4);
        Check(MathF.Abs(a.X-b.X)<.001f&&MathF.Abs(a.Y-b.Y)<.001f&&MathF.Abs(a1-b1)<.001f&&MathF.Abs(a2-b2)<.001f&&MathF.Abs(a3-b3)<.001f&&MathF.Abs(a4-b4)<.001f,"world affine preserved");
    }
    private static void Check(bool value,string message){_count++;if(!value)throw new InvalidOperationException("Room test: "+message);}
    private static void Expect(Action action){try{action();}catch(Exception e) when(e is SceneFormatException or FileNotFoundException or ArgumentOutOfRangeException){_count++;return;}throw new InvalidOperationException("Expected rejected room data");}
}
