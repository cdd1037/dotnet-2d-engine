using System.Numerics;
using System.Text.Json.Nodes;
namespace GameAuthoringLab;

internal static class TileMapTests
{
    private sealed class Idle : IBehavior { public void Update(Entity e,float dt) { } }
    private static TileMapDocument Source(LoadedTileMap sample,int width,int height,params TileLayerRecord[] layers) => new()
    {
        Kind="gal-tilemap",Version=1,Name="Test grid",Width=width,Height=height,TileWidth=32,TileHeight=32,
        Resources=sample.Source.Resources,Tiles=sample.Source.Tiles,Layers=[..layers]
    };
    public static int Run()
    {
        int count=TileMapEditingTests.Run();
        void Check(bool ok,string label) { if(!ok) throw new InvalidOperationException("TILEMAP: "+label); count++; }
        void Reject<T>(Action action,string label) where T:Exception { try { action(); } catch(T) { count++;return; } throw new InvalidOperationException("TILEMAP accepted: "+label); }
        var root=new AssetRoot(); string path=root.FilePath("basics.tilemap.json"),json=File.ReadAllText(path);
        LoadedTileMap Load(string text)=>TileMapAsset.Load(text,path,root);
        var loaded=TileMapAsset.LoadAsset(root,"basics.tilemap.json"); var map=loaded.Map;
        Check(map.Width==34 && map.Height==18 && map.Layers.Length==3 && map.AssetKeys.Length==4,"sourcegen grid/atlas metadata");
        Check(map.Layers[0].Name=="decoration" && map.Layers[1].Name=="terrain" && map.Layers[2].Name=="overlay","layer numeric order and source tie order");
        Check(map.Layers[1].Opacity==1,"omitted source opacity defaults to one under sourcegen");
        string written=TileMapAsset.Write(loaded.Source,path,root); Check(TileMapAsset.Write(Load(written).Source,path,root)==written,"sourcegen stable round trip including numeric flip arrays");
        var altered=Load(json); altered.Source.Layers[0].Cells[13*34]=0; altered.Source.Layers[1].Flips![2*34+1]=0; altered.Source.Tiles.Reverse();
        Check(altered.Map.Layers[1].Cell(13*34)==1 && altered.Map.Layers[0].Flip(2*34+1)==3 && altered.Map.Tile(1).AssetKey=="soil","runtime copies cells/flags and resolves IDs independently of palette order");
        Check(TileMapAsset.Build(altered.Source,path,root).Map.Tile(1).AssetKey=="soil","reordered source palette preserves numeric IDs");
        void Invalid(Action<JsonNode> edit,string code,string at)
        {
            var node=JsonNode.Parse(json)!; edit(node);
            try { Load(node.ToJsonString()); throw new Exception("Expected invalid tile source"); }
            catch(TileMapException e) { Check(e.Code==code && e.JsonPath.Contains(at,StringComparison.Ordinal),"diagnostic "+at); }
        }
        Invalid(n=>n["version"]=2,"TILE_VERSION","version");
        Invalid(n=>n["width"]=0,"TILE_LIMIT","width");
        Invalid(n=>n["height"]=int.MaxValue,"TILE_LIMIT","height");
        Invalid(n=>n["tileWidth"]=0,"TILE_SIZE","tileWidth");
        Invalid(n=>n["layers"]![0]!["cells"]!.AsArray().RemoveAt(0),"TILE_CELLS","cells");
        Invalid(n=>n["layers"]![0]!["cells"]![0]=999,"TILE_ID","cells[0]");
        Invalid(n=>n["layers"]![0]!["cells"]![0]=-1,"TILE_ID","cells[0]");
        Invalid(n=>n["layers"]![1]!["name"]="terrain","TILE_NAME","name");
        Invalid(n=>n["layers"]![1]!["flips"]![0]=1,"TILE_VALUE","flips[0]");
        Invalid(n=>n["layers"]![1]!["flips"]![2*34+1]=-1,"TILE_VALUE","flips[69]");
        Invalid(n=>n["layers"]![1]!["flips"]![2*34+1]=4,"TILE_VALUE","flips[69]");
        Invalid(n=>n["layers"]![1]!["flips"]!.AsArray().RemoveAt(0),"TILE_CELLS","flips");
        Invalid(n=>n["layers"]![0]!["opacity"]=1.1,"TILE_VALUE","opacity");
        Invalid(n=>n["tiles"]![0]!["id"]=0,"TILE_ID","id");
        Invalid(n=>n["tiles"]![1]!["id"]=1,"TILE_ID","id");
        Invalid(n=>n["tiles"]![0]!["assetKey"]="missing","TILE_RESOURCE","assetKey");
        Invalid(n=>n["resources"]![1]!["key"]="soil","TILE_RESOURCE","key");
        Invalid(n=>n["resources"]![0]!["path"]="../regions.bmp","TILE_RESOURCE","path");
        Invalid(n=>n["resources"]![0]!["region"]!["width"]=17,"TILE_RESOURCE","region");
        Invalid(n=>n["layers"]![0]!["cells"]=null,"TILE_JSON","cells");
        Invalid(n=>n["unknown"]=1,"TILE_JSON","unknown");
        Invalid(n=>n.AsObject().Remove("width"),"TILE_JSON","$");
        Reject<TileMapException>(()=>Load(json.Replace("\"version\": 1","\"version\": 1, \"version\": 1",StringComparison.Ordinal)),"duplicate JSON property");
        Reject<TileMapException>(()=>Load(new string(' ',TileMapAsset.MaximumBytes+1)),"bounded source characters");
        Reject<TileMapException>(()=>Load(new string('界',TileMapAsset.MaximumBytes/2)),"bounded UTF-8 bytes");
        Reject<TileMapException>(()=>Load("\ud800"),"invalid Unicode string");
        string temporary=Path.Combine(Path.GetTempPath(),"gal-tile-tests-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        try
        {
            var temporaryRoot=new AssetRoot(temporary);
            using(var file=File.Create(Path.Combine(temporary,"large.tilemap.json")))file.SetLength(TileMapAsset.MaximumBytes+1);
            Reject<TileMapException>(()=>TileMapAsset.LoadAsset(temporaryRoot,"large.tilemap.json"),"file size checked before allocating source buffer");
            File.WriteAllBytes(Path.Combine(temporary,"invalid.tilemap.json"),[0xff,0xfe,0xff]);
            Reject<TileMapException>(()=>TileMapAsset.LoadAsset(temporaryRoot,"invalid.tilemap.json"),"malformed UTF-8 file");
        }
        finally { Directory.Delete(temporary,true); }
        var oversized=Source(loaded,256,256,new TileLayerRecord(){Name="a",Order=0,Cells=[]},new TileLayerRecord(){Name="b",Order=0,Cells=[]},new TileLayerRecord(){Name="c",Order=0,Cells=[]});
        Reject<TileMapException>(()=>TileMapAsset.Build(oversized,path,root),"cell slot cap before runtime allocation");

        using var engine=new EngineHost(true,4096); using var instance=new TileMapInstance(engine,loaded,new(-64,-32,1));
        Check(engine.Textures.Count==1 && engine.Textures.Loads==1,"map aliases share one retained cache entry");
        var batch=new SpriteBatch();
        instance.ExtractSprites(batch,new(-64,-32,32,32)); Check(batch.Count==0,"empty cell culling");
        instance.ExtractSprites(batch,new(-64,-32+13*32,32,32)); Check(batch.Count==1 && batch.RegionDraws[0].Draw.X==-64,$"half-open exact cell boundaries count={batch.Count} x={(batch.Count>0?batch.RegionDraws[0].Draw.X:999)}");
        instance.ExtractSprites(batch,new(-64+32,-32+13*32,0,32)); Check(batch.Count==0,"zero view extent");
        instance.ExtractSprites(batch,new(-64-32,-32+13*32,32,32)); Check(batch.Count==0,"view ending exactly at map origin");
        instance.ExtractSprites(batch,new(-64-.001,-32+13*32,1,1)); Check(batch.Count==1,"subpixel overlap includes complete cell");
        instance.ExtractSprites(batch,new(1e100,1e100,1,1)); Check(batch.Count==0,"far positive view clamps before casts");
        instance.ExtractSprites(batch,new(-1e100,-1e100,1,1)); Check(batch.Count==0,"far negative view clamps before casts");
        Reject<ArgumentOutOfRangeException>(()=>instance.ExtractSprites(batch,new(double.NaN,0,1,1)),"nonfinite view");
        Reject<ArgumentOutOfRangeException>(()=>instance.ExtractSprites(batch,new(0,0,-1,1)),"negative extent");
        Reject<ArgumentOutOfRangeException>(()=>instance.ExtractSprites(batch,new(double.MaxValue,0,double.MaxValue,1)),"view endpoint overflow");
        Reject<ArgumentOutOfRangeException>(()=>new TileMapInstance(engine,loaded,default),"zero default placement scale");
        Reject<ArgumentOutOfRangeException>(()=>new TileMapInstance(engine,loaded,new(0,0,float.NaN)),"invalid placement scale");
        Check(instance.TryWorldToCell(new(-64,-32),out int cx,out int cy) && cx==0 && cy==0,"world point maps to first cell");
        Check(!instance.TryWorldToCell(new(-64+34*32,-32),out _,out _) && !instance.TryWorldToCell(new(-65,-32),out _,out _) && !instance.TryWorldToCell(new(float.NaN,0),out _,out _),"cell query excludes outside/right edge/nonfinite point");
        var view=TileView.FromCamera(new(){X=-16,Y=8,Zoom=2},new Viewport(480,270,960,540));
        Check(view==new TileView(-16,8,480,270),"camera culling uses framebuffer dimensions at 2x density");
        Check(TileView.FromCamera(new(){Zoom=1},new(0,0,0,0,false))==default,"minimized viewport yields empty view");
        Reject<ArgumentOutOfRangeException>(()=>TileView.FromCamera(new(){Zoom=0},new(0,0,0,0,false)),"invalid camera rejected even with inactive viewport");

        // Compare chunk-assisted extraction against a simple row-major oracle across both chunk axes.
        var random=new Random(121); var cells=new int[34*34]; var flips=new int[cells.Length];
        for(int i=0;i<cells.Length;i++) if(random.Next(5)==0){cells[i]=1+random.Next(4);flips[i]=random.Next(4);}
        var denseSource=Source(loaded,34,34,new TileLayerRecord(){Name="first",Order=7,Cells=cells,Flips=flips},new TileLayerRecord(){Name="second",Order=7,Cells=(int[])cells.Clone(),Opacity=.5f});
        var dense=TileMapAsset.Build(denseSource,path,root); using var chunked=new TileMapInstance(engine,dense,new(-20,-40,.5f));
        for(int iteration=0;iteration<40;iteration++)
        {
            var bounds=new TileView(random.Next(-100,500),random.Next(-100,500),random.Next(1,300),random.Next(1,300));
            chunked.ExtractSprites(batch,bounds); int n=0;
            foreach(var layer in dense.Map.Layers) for(int y=0;y<34;y++) for(int x=0;x<34;x++)
            {
                int i=y*34+x; float px=-20+x*16,py=-40+y*16;
                if(layer.Cell(i)==0 || px+16<=bounds.X || py+16<=bounds.Y || px>=bounds.X+bounds.Width || py>=bounds.Y+bounds.Height)continue;
                var draw=batch.RegionDraws[n++]; Check(draw.Draw.X==px && draw.Draw.Y==py && draw.Draw.A==layer.Opacity && draw.Flags==layer.Flip(i),"chunk cull/order oracle");
            }
            Check(n==batch.Count,"chunk oracle draw count");
        }
        var world=new World(); var entity=world.Create("ordinary sprite"); entity.Sprite=new(8,8,Layer:7);
        world.ExtractSprites(batch); chunked.AppendSprites(batch,new(-20,-40,600,600));
        Check(batch.RegionDraws[0].Draw.Width==8 && batch.RegionDraws[1].Draw.Width==32,"append preserves existing equal-layer order");
        Check(batch.RegionResolver is null,"map resolver does not replace caller resolver");
        var wide=TileView.FromCamera(new(){Zoom=.01f},new(960,540,960,540));
        var tooMany=new int[65*65]; Array.Fill(tooMany,1); var tooDense=TileMapAsset.Build(Source(loaded,65,65,new TileLayerRecord(){Name="full",Order=0,Cells=tooMany}),path,root);
        using(var overBudget=new TileMapInstance(engine,tooDense,new(0,0)))
        {
            world.ExtractSprites(batch); Reject<InvalidOperationException>(()=>overBudget.AppendSprites(batch,wide),"whole-frame budget rejected before appending");
            Check(batch.Count==1,"budget failure retains previous complete batch");
        }
        var stableView=new TileView(-30,-50,600,600); for(int i=0;i<128;i++)chunked.ExtractSprites(batch,stableView);
        long before=GC.GetAllocatedBytesForCurrentThread(); for(int i=0;i<1000;i++)chunked.ExtractSprites(batch,stableView);
        Check(GC.GetAllocatedBytesForCurrentThread()==before,"warmed chunk extraction allocates zero");

        for(int i=0;i<128;i++){world.ExtractSprites(batch);chunked.AppendSprites(batch,stableView);}
        before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++){world.ExtractSprites(batch);chunked.AppendSprites(batch,stableView);}
        Check(GC.GetAllocatedBytesForCurrentThread()==before,"warmed combined map/entity stable sorting allocates zero");
        Exception? threadFailure=null;var thread=new Thread(()=>{try{instance.ExtractSprites(batch,stableView);}catch(Exception e){threadFailure=e;}});thread.Start();thread.Join();
        Check(threadFailure is InvalidOperationException,"map extraction obeys engine thread ownership");
        var plan=TileCollisionPlan.Create(map,new(64,64),new(32));
        Check(plan.Rectangles.Length>0 && plan.Rectangles.Length<20,"solid cell union merged into bounded rectangles");
        var coverage=new int[map.Width*map.Height]; foreach(var r in plan.Rectangles)for(int y=r.Y;y<r.Y+r.Height;y++)for(int x=r.X;x<r.X+r.Width;x++)coverage[y*map.Width+x]++;
        bool exact=true; for(int i=0;i<coverage.Length;i++){bool solid=false;foreach(var layer in map.Layers)if(layer.Cell(i)!=0&&map.Tile(layer.Cell(i)).Solid)solid=true;if(coverage[i]!=(solid?1:0))exact=false;}
        Check(exact,"greedy rectangles cover every unioned solid cell exactly once");
        var checker=new int[32*32]; for(int y=0;y<32;y++)for(int x=0;x<32;x++)if((x+y)%2==0)checker[y*32+x]=1;
        var checkerMap=TileMapAsset.Build(Source(loaded,32,32,new TileLayerRecord(){Name="checker",Order=0,Cells=checker}),path,root).Map;
        Reject<InvalidOperationException>(()=>TileCollisionPlan.Create(checkerMap,new(0,0),new(32)),"separate collision rectangle cap");
        Reject<InvalidOperationException>(()=>TileCollisionPlan.Create(map,new(64,64),new(.01f)),"physics unit/shape bounds before native mutation");
        Reject<ArgumentOutOfRangeException>(()=>TileCollisionPlan.Create(map,new(64,64),default),"invalid default meter scale");
        for(int i=0;i<3;i++)
        {
            var scene=world.CreateScene("map"); var anchor=world.Create("map anchor",scene); var attached=new TileMapInstance(engine,loaded,new(0,0));
            world.AttachBehavior(anchor,new Idle(),life=>life.OnDetach(attached.Dispose)); world.UnloadScene(scene);
            Check(attached.IsDisposed && engine.Textures.Count==1,"scene teardown releases only that map instance");
            Reject<ObjectDisposedException>(()=>attached.ExtractSprites(batch,stableView),"disposed map cannot extract");
        }
        chunked.Dispose(); instance.Dispose(); Check(engine.Textures.Count==0,"all instance texture leases released");
        using(var late=new TileMapInstance(engine,loaded,new(0,0))){engine.Dispose();late.Dispose();Check(late.IsDisposed,"late instance disposal after engine destruction");}
        Console.WriteLine($"TILEMAP SELF-TEST PASS assertions={count}"); return count;
    }

    public static int RunPhysics()
    {
        int count=TileMapEditingTests.RunPhysics(); void Check(bool ok,string label){if(!ok)throw new InvalidOperationException("TILE PHYSICS: "+label);count++;}
        void Reject(Action action,string label){try{action();}catch(InvalidOperationException){count++;return;}throw new InvalidOperationException("TILE PHYSICS accepted: "+label);}
        var loaded=TileMapAsset.LoadAsset(new AssetRoot(),"basics.tilemap.json");
        using var engine=new EngineHost(true,4096); using var physics=engine.OpenPhysics(); using var map=new TileMapInstance(engine,loaded,new(64,64));
        var collision=map.AttachCollision(physics,new(32)); uint rectangles=(uint)collision.Rectangles.Length;
        Check(physics.State.Bodies==rectangles && physics.State.Shapes==rectangles,"static boxes created from merged plan");
        var ray=physics.RayCast(24.5f,0,0,25); Check(ray.Shape!=0 && collision.TryGetRectangle(ray.Shape,out var r) && r.Y==13,"ray hit maps back to source tile rectangle");
        Span<ulong> ids=stackalloc ulong[512]; int hits=physics.QueryAabb(24.1f,14.9f,24.9f,15.2f,ids);
        Check(hits==1 && collision.TryGetRectangle(ids[0],out _),"broad-phase query resolves generated collision identity");
        using var ball=physics.CreateBody(new(PhysicsBodyType.Dynamic,24.5f,4)); ball.AddShape(new(PhysicsShapeType.Circle,.4f));
        for(int i=0;i<180;i++) Check(physics.Step().Dropped==0,"solver event capacity");
        Check(Math.Abs(ball.State.Y-14.6f)<.03 && Math.Abs(ball.State.Vy)<.05,"dynamic circle rests on generated tile floor");
        collision.Dispose(); collision.Dispose(); Check(physics.State.Bodies==1 && physics.State.RetiredShapes==rectangles,"map disposal preserves independent body and retires shape identities");
        physics.Step(); Check(physics.State.RetiredShapes==0,"only explicit step reclaims retired slots");
        var blockers=new List<PhysicsBody>(); int target=257-(int)rectangles;
        while(physics.State.Bodies<target)blockers.Add(physics.CreateBody(new(PhysicsBodyType.Static)));
        var before=physics.State; Reject(()=>map.AttachCollision(physics,new(32)),"body capacity preflight");
        Check(physics.State.Bodies==before.Bodies && physics.State.Steps==before.Steps,"failed capacity preflight creates no bodies and does not step");
        foreach(var body in blockers)body.Dispose();
        using(var retirement=physics.CreateBody(new(PhysicsBodyType.Static)))
        {
            for(int i=(int)physics.State.Shapes;i<512;i++)retirement.AddShape(new(PhysicsShapeType.Circle,.1f)).Dispose();
            before=physics.State; Reject(()=>map.AttachCollision(physics,new(32)),"retired shape capacity preflight");
            Check(physics.State.Shapes==before.Shapes && physics.State.RetiredShapes==before.RetiredShapes && physics.State.Steps==before.Steps,"retired-capacity rejection preserves world state");
        }
        physics.Step(); var again=map.AttachCollision(physics,new(32)); Check(again.Rectangles.Length==rectangles,"reattach succeeds after explicit reclamation");
        var world=new World();var scene=world.CreateScene("tile level");var anchor=world.Create("tile map",scene);
        world.AttachBehavior(anchor,new Idle(),life=>life.OnDetach(map.Dispose)); world.UnloadScene(scene);
        Check(map.IsDisposed && again.IsDisposed && physics.State.Bodies==1 && engine.Textures.Count==0,"scene unload owns collision and textures, preserving ball");
        for(int i=0;i<3;i++)
        {
            scene=world.CreateScene("tile reload");anchor=world.Create("tile map",scene);var repeated=new TileMapInstance(engine,loaded,new(64,64));var repeatedCollision=repeated.AttachCollision(physics,new(32));
            world.AttachBehavior(anchor,new Idle(),life=>life.OnDetach(repeated.Dispose));world.UnloadScene(scene);
            Check(repeated.IsDisposed&&repeatedCollision.IsDisposed&&physics.State.Bodies==1,"repeated scene unload keeps independent body");physics.Step();
            Check(physics.State.RetiredShapes==0&&engine.Textures.Count==0,"repeated explicit reclamation and texture cleanup");
        }
        ball.Dispose();physics.Step();Check(physics.State.Bodies==0 && physics.State.Shapes==0 && physics.State.RetiredShapes==0,"complete map/ball cleanup");
        Console.WriteLine($"TILEMAP PHYSICS PASS assertions={count} rectangles={rectangles}");return count;
    }
}
