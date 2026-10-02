using GameAuthoringLab;
namespace Relay;

internal sealed class RoomGame
{
    public const float FixedDelta=1f/60;
    public World World {get;}
    public Entity Player {get;}
    public Entity Item {get;}
    public Entity? Held {get;private set;}
    public Scene ActiveScene {get;private set;}
    public int RoomIndex {get;private set;}
    public int ItemRoomIndex {get;private set;}
    public int TransitionCount {get;private set;}
    public int PickupCount {get;private set;}
    public int CollisionCount {get;private set;}
    public int LastSteps {get;private set;}
    private readonly Entity _status;
    private double _accumulator;
    private uint _previousKeys,_pendingActions;
    private static readonly (float X,float Y,float W,float H)[] Obstacles=[(400,190,90,100),(610,350,100,60)];
    private static Sprite2D CellSprite=>new(28,32,AssetKey:"cell",Layer:30);
    public RoomGame()
    {
        World=new World();ActiveScene=CreateRoom(0);
        Player=World.Create("Player",transform:new Transform2D(160,280));Player.Sprite=new(36,46,AssetKey:"player",Layer:20);
        Item=World.Create("PowerCell",ActiveScene,new Transform2D(266,286,Rotation:.2f));Item.Sprite=CellSprite;
        _status=World.Create("Status",transform:new Transform2D(380,32));SetStatus("status-empty");
    }
    private RoomGame(LoadedScene loaded)
    {
        World=loaded.World;GameSaveState s=loaded.State??throw new SceneFormatException("Missing two-room game state.");
        if(s.RoomIndex is <0 or >1||s.ItemRoomIndex is < -1 or >1||s.PlayerId is null||s.ItemId is null||s.ActiveSceneId is null)throw new SceneFormatException("Invalid two-room state.");
        Player=World.GetPersistent(s.PlayerId.Value);Item=World.GetPersistent(s.ItemId.Value);ActiveScene=World.GetScene(s.ActiveSceneId.Value);
        RoomIndex=s.RoomIndex;ItemRoomIndex=s.ItemRoomIndex;TransitionCount=s.TransitionCount;PickupCount=s.PickupCount;
        Held=s.HeldItemId is {} id?World.GetPersistent(id):null;
        if(Player==Item||Player.Name!="Player"||Item.Name!="PowerCell"||Player.Sprite?.AssetKey!="player"||Player.Scene!=World.PersistentScene||Player.TransformParent is not null||Player.LifetimeOwner is not null)throw new SceneFormatException("Invalid player or item identity.");
        if(ActiveScene==World.PersistentScene||ActiveScene.Name!=(RoomIndex==0?"Garden Workshop":"Field Archive")||World.LoadedScenes.Count()!=2)throw new SceneFormatException("Invalid active room.");
        if(Held is not null){if(Held!=Item||ItemRoomIndex!=-1||Item.TransformParent!=Player||Item.LifetimeOwner!=Player||Item.Scene!=World.PersistentScene||Item.Sprite?.AssetKey!="cell")throw new SceneFormatException("Invalid held item state.");}
        else if(ItemRoomIndex<0||Item.TransformParent is not null||Item.LifetimeOwner is not null||(ItemRoomIndex==RoomIndex?(Item.Scene!=ActiveScene||Item.Sprite?.AssetKey!="cell"):(Item.Scene!=World.PersistentScene||Item.Sprite is not null)))throw new SceneFormatException("Invalid dropped item state.");
        _status=Named("Status");
        Entity? backdrop=Named("RoomBackdrop");
        if(backdrop is null||backdrop.Scene!=ActiveScene||backdrop.Sprite?.AssetKey!=(RoomIndex==0?"room-a":"room-b"))throw new SceneFormatException("Invalid room asset.");
        if(!Clear(Player.LocalTransform.X,Player.LocalTransform.Y)||Player.LocalTransform.ScaleX!=1||Player.LocalTransform.ScaleY!=1||Player.LocalTransform.Rotation!=0||Player.LocalTransform.Shear!=0||Player.Sprite?.Width!=36||Player.Sprite?.Height!=46)throw new SceneFormatException("Player shape or position is incompatible with the fixed AABB collider.");
        if(_status.Scene!=World.PersistentScene||_status.TransformParent is not null||_status.LifetimeOwner is not null||_status.LocalTransform!=new Transform2D(380,32)||_status.Sprite is not {} statusSprite||statusSprite.Width!=248||statusSprite.Height!=24||statusSprite.Layer!=100||statusSprite.AssetKey is not ("status-empty" or "status-held" or "status-restored"))throw new SceneFormatException("Invalid persistent status role.");
        if(backdrop.TransformParent is not null||backdrop.LifetimeOwner is not null||backdrop.LocalTransform!=Transform2D.Identity||backdrop.Sprite?.Width!=960||backdrop.Sprite?.Height!=540||backdrop.Sprite?.Layer!=-100)throw new SceneFormatException("Room backdrop does not match its collision geometry.");
        if(World.EntityCount!=6||Named("Player")!=Player||Named("PowerCell")!=Item)throw new SceneFormatException("Unexpected game entity set.");
        ValidateCellVisual();
        ValidateFixture(Named("Workbench"),new Transform2D(400,190));ValidateFixture(Named("Shelf"),new Transform2D(610,350));
        SetStatus("status-restored");
    }
    private void ValidateCellVisual()
    {
        if(Item.Sprite is {} sprite&&(sprite.Width!=28||sprite.Height!=32||sprite.AssetKey!="cell"))throw new SceneFormatException("Invalid cell sprite role.");
        var t=Item.WorldTransform;
        if(!float.IsFinite(28*t.ScaleX)||!float.IsFinite(32*t.ScaleY))throw new SceneFormatException("Cell scaled extents overflow.");
        t.GetBasis(out float a,out float b,out float c,out float d);
        for(int i=0;i<4;i++){double x=(i&1)!=0?28:0,y=(i&2)!=0?32:0;if(!float.IsFinite((float)(a*x+c*y+t.X))||!float.IsFinite((float)(b*x+d*y+t.Y)))throw new SceneFormatException("Hidden or visible cell geometry overflows.");}
    }
    private void ValidateFixture(Entity fixture,Transform2D expected)
    {
        if(fixture.Scene!=ActiveScene||fixture.TransformParent is not null||fixture.LifetimeOwner is not null||fixture.Sprite is not null||fixture.LocalTransform!=expected)throw new SceneFormatException("Invalid collision fixture metadata.");
    }
    private Entity Named(string name)
    {
        Entity? found=null;
        foreach(Entity e in World.Entities)if(e.Name==name){if(found is not null)throw new SceneFormatException("Duplicate game role: "+name);found=e;}
        return found??throw new SceneFormatException("Missing game role: "+name);
    }
    private Scene CreateRoom(int index)
    {
        Scene room=World.CreateScene(index==0?"Garden Workshop":"Field Archive");
        Entity floor=World.Create("RoomBackdrop",room);floor.Sprite=new(960,540,AssetKey:index==0?"room-a":"room-b",Layer:-100);
        // Fixture lifetimes are real world objects; collision remains deliberately fixed AABBs.
        World.Create("Workbench",room,new Transform2D(400,190));World.Create("Shelf",room,new Transform2D(610,350));
        return room;
    }
    public void Advance(uint keys,float elapsed) => Advance(keys,keys&~_previousKeys,elapsed);
    public void Advance(uint keys,uint pressed,float elapsed)
    {
        if(!float.IsFinite(elapsed)||elapsed<0)throw new ArgumentOutOfRangeException(nameof(elapsed));
        _pendingActions|=pressed&(RelayInput.Interact|RelayInput.Drop|RelayInput.Transition);_previousKeys=keys;
        _accumulator+=Math.Min(elapsed,.25);LastSteps=0;
        while(_accumulator>=FixedDelta&&LastSteps<8){Step((keys&(RelayInput.Left|RelayInput.Right|RelayInput.Up|RelayInput.Down))|_pendingActions);_pendingActions=0;_accumulator-=FixedDelta;LastSteps++;}
        if(LastSteps==8&&_accumulator>=FixedDelta)_accumulator=0; // bounded backlog, no spiral after a pause
    }
    // Modal boundary: discard pre-pause substep actions and held-key edges.
    internal void ResetInputBoundary(){_accumulator=0;_previousKeys=0;_pendingActions=0;LastSteps=0;}
    public void Step(uint keys)
    {
        float dx=((keys&RelayInput.Right)!=0?1:0)-((keys&RelayInput.Left)!=0?1:0),dy=((keys&RelayInput.Down)!=0?1:0)-((keys&RelayInput.Up)!=0?1:0);
        if(dx!=0&&dy!=0){dx*=.70710678f;dy*=.70710678f;}
        Move(dx*180*FixedDelta,dy*180*FixedDelta);
        if((keys&RelayInput.Interact)!=0)PickUp();if((keys&RelayInput.Drop)!=0)Drop();if((keys&RelayInput.Transition)!=0)UseDoor();
        if(Held is not null){var t=Held.LocalTransform;Held.LocalTransform=t with {Rotation=t.Rotation+FixedDelta*.65f};}
        World.Update(FixedDelta);
    }
    private static bool Inside(float x,float y)=>x>=72&&x<=852&&y>=140&&y<=402;
    private static bool Clear(float x,float y)
    {
        if(!Inside(x,y))return false;
        foreach(var r in Obstacles)if(x<r.X+r.W&&x+36>r.X&&y<r.Y+r.H&&y+46>r.Y)return false;
        return true;
    }
    private void Move(float dx,float dy)
    {
        var t=Player.LocalTransform;float x=t.X,y=t.Y;
        if(Clear(x+dx,y))x+=dx;else if(dx!=0)CollisionCount++;
        if(Clear(x,y+dy))y+=dy;else if(dy!=0)CollisionCount++;
        Player.LocalTransform=t with {X=x,Y=y};
    }
    public bool PickUp()
    {
        if(Held is not null||ItemRoomIndex!=RoomIndex)return false;
        var a=Player.WorldTransform;var b=Item.WorldTransform;
        if(MathF.Abs(a.X-b.X)>64||MathF.Abs(a.Y-b.Y)>64)return false;
        World.MakePersistent(Item);World.Reparent(Item,Player,true);World.SetOwner(Item,Player);
        Held=Item;ItemRoomIndex=-1;PickupCount++;SetStatus("status-held");return true;
    }
    public bool Drop()
    {
        if(Held is null)return false;
        World.Reparent(Item,null,true);World.SetOwner(Item,null);World.MoveToScene(Item,ActiveScene);Held=null;ItemRoomIndex=RoomIndex;SetStatus("status-empty");return true;
    }
    public bool UseDoor()
    {
        var p=Player.LocalTransform;
        if(p.Y<250||p.Y>320||(RoomIndex==0?p.X<816:p.X>128))return false;
        if(Held is null&&ItemRoomIndex==RoomIndex){World.MoveToScene(Item,World.PersistentScene);Item.Sprite=null;}
        World.UnloadScene(ActiveScene);RoomIndex=1-RoomIndex;ActiveScene=CreateRoom(RoomIndex);
        Player.LocalTransform=p with {X=RoomIndex==1?110:810,Y=280};
        if(Held is null&&ItemRoomIndex==RoomIndex){World.MoveToScene(Item,ActiveScene);Item.Sprite=CellSprite;}
        TransitionCount++;return true;
    }
    private void SetStatus(string key)=>_status.Sprite=new(248,24,AssetKey:key,Layer:100);
    public string Save()=>ScenePersistence.Save(World,new GameSaveState{PlayerId=Player.PersistentId,ItemId=Item.PersistentId,ActiveSceneId=ActiveScene.PersistentId,HeldItemId=Held?.PersistentId,RoomIndex=RoomIndex,ItemRoomIndex=ItemRoomIndex,TransitionCount=TransitionCount,PickupCount=PickupCount});
    public static RoomGame Load(string json,AssetCatalog catalog)=>new(ScenePersistence.Load(json,catalog.Exists));
    public void SaveFile(string path)
    {
        string full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);string temp=full+".tmp";
        try{File.WriteAllText(temp,Save());File.Move(temp,full,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static RoomGame LoadFile(string path,AssetCatalog catalog)
    {
        if(new FileInfo(path).Length>16*1024*1024)throw new SceneFormatException("Save is too large.");
        try{return Load(File.ReadAllText(path),catalog);}
        catch(SceneFormatException exception){throw new SceneFormatException($"{path}: {exception.Message}",exception);}
    }
}
