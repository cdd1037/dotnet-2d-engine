namespace GameAuthoringLab;

/// <summary>Focused runtime-edit contracts, invoked by the existing TileMap test entry points.</summary>
internal static class TileMapEditingTests
{
    private sealed class Idle : IBehavior { public void Update(Entity entity,float dt) { } }
    private sealed class Checks
    {
        public int Count { get; private set; }
        public void That(bool ok,string label)
        { if(!ok)throw new InvalidOperationException("TILE EDIT: "+label);Count++; }
        public void Reject<T>(Action action,string label) where T:Exception
        {
            try { action(); } catch(T) { Count++;return; }
            throw new InvalidOperationException("TILE EDIT accepted: "+label);
        }
    }

    private static LoadedTileMap Build(int width,int height,params TileLayerRecord[] layers)
    {
        var assets=new AssetRoot();var sample=TileMapAsset.LoadAsset(assets,"basics.tilemap.json");
        return TileMapAsset.Build(new()
        {
            Kind="gal-tilemap",Version=1,Name="Runtime editing tests",Width=width,Height=height,TileWidth=32,TileHeight=32,
            Resources=sample.Source.Resources,Tiles=sample.Source.Tiles,Layers=[..layers]
        },assets.FilePath("runtime-edit.tilemap.json"),assets);
    }
    private static TileLayerRecord Layer(string name,int width,int height,int order=0,float opacity=1) =>
        new(){Name=name,Order=order,Opacity=opacity,Cells=new int[width*height]};

    public static int Run()
    {
        var check=new Checks();
        using(var engine=new EngineHost(true,4096))
        {
            IsolationAndChunks(engine,check);
            BatchValidation(engine,check);
            Residency(engine,check);
            Lifetime(engine,check);
            check.That(engine.Textures.Count==0,"all editable instance leases released");
        }
        using(var engine=new EngineHost(true,4096))
        {
            var asset=Build(1,1,Layer("late",1,1));
            using var instance=TileMapInstance.CreateEditable(engine,asset,new(0,0));
            engine.Dispose();
            check.Reject<ObjectDisposedException>(()=>instance.SetCells([new(0,0,0,1)]),"edit after engine close");
            instance.Dispose();instance.Dispose();check.That(instance.IsDisposed,"late editable disposal after engine close");
        }
        Console.WriteLine($"TILEMAP EDIT SELF-TEST PASS assertions={check.Count}");return check.Count;
    }

    private static void IsolationAndChunks(EngineHost engine,Checks check)
    {
        const int width=34,height=34;
        // Sorted layer indices deliberately differ from authoring indices; equal orders retain source order.
        var asset=Build(width,height,Layer("late",width,height,7,.5f),Layer("early",width,height,-2),Layer("peer",width,height,7,.75f));
        using var first=TileMapInstance.CreateEditable(engine,asset,new(-20,-40,.5f));
        using var second=TileMapInstance.CreateEditable(engine,asset,new(-20,-40,.5f));
        using var readOnly=new TileMapInstance(engine,asset,new(0,0));
        var original=asset.Map;
        check.That(first.IsEditable&&second.IsEditable&&!readOnly.IsEditable,"editing is an explicit opt-in");
        check.Reject<InvalidOperationException>(()=>readOnly.SetCells([new(0,0,0,1)]),"legacy instance remains read-only");
        check.Reject<InvalidOperationException>(()=>readOnly.SetCells([]),"read-only rejects empty edit batch");
        var edits=new TileCellEdit[]
        {
            new(0,15,15,1,1),new(0,16,15,2,2),new(0,15,16,3,3),new(0,16,16,4),
            new(0,31,31,1,3),new(0,32,31,2,2),new(0,31,32,3,1),new(0,32,32,4),
            new(0,0,0,3),new(0,33,33,4,3),new(1,16,16,2,3)
        };
        first.SetCells(edits);var committed=first.Map;
        check.That(!ReferenceEquals(committed,original)&&ReferenceEquals(second.Map,original)&&ReferenceEquals(readOnly.Map,original),"edit isolates sibling and read-only instances");
        check.That(!ReferenceEquals(committed.Layers[0],original.Layers[0])&&!ReferenceEquals(committed.Layers[1],original.Layers[1])&&ReferenceEquals(committed.Layers[2],original.Layers[2]),"copy-on-write copies changed layers and shares untouched layer");
        check.That(committed.Layers[0].Name=="early"&&committed.Layers[1].Name=="late"&&committed.Layers[2].Name=="peer","edit indices use sorted runtime layers");
        check.That(original.Layers[0].Cell(15*width+15)==0&&asset.Source.Layers.All(layer=>layer.Cells.All(id=>id==0)),"saved snapshot and authoring arrays remain unchanged");
        int[][] expected=[new int[width*height],new int[width*height],new int[width*height]];
        int[][] flags=[new int[width*height],new int[width*height],new int[width*height]];
        foreach(var edit in edits){int i=edit.Y*width+edit.X;expected[edit.LayerIndex][i]=edit.TileId;flags[edit.LayerIndex][i]=edit.Flip;}
        edits[0]=new(0,15,15,0);
        check.That(committed.Layers[0].Cell(15*width+15)==1,"caller edit storage is not retained");
        var batch=new SpriteBatch();
        void Oracle(TileView view)
        {
            first.ExtractSprites(batch,view);int n=0;bool matches=true;
            for(int l=0;l<expected.Length;l++)for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                int i=y*width+x;float px=-20+x*16,py=-40+y*16;
                if(expected[l][i]==0||px+16<=view.X||py+16<=view.Y||px>=view.X+view.Width||py>=view.Y+view.Height)continue;
                if(n>=batch.Count){matches=false;continue;}
                var draw=batch.RegionDraws[n++];
                matches&=draw.Draw.X==px&&draw.Draw.Y==py&&draw.Draw.Width==32&&draw.Draw.Height==32&&draw.Draw.A==first.Map.Layers[l].Opacity&&draw.Flags==flags[l][i];
            }
            check.That(matches&&n==batch.Count,"edited chunk extraction matches independent row-major oracle");
        }
        Oracle(new(-20,-40,width*16,height*16));
        var random=new Random(2468);
        for(int i=0;i<35;i++)Oracle(new(random.Next(-60,540),random.Next(-80,540),random.Next(1,300),random.Next(1,300)));
        first.SetCells([new(0,0,0,0),new(0,15,15,0),new(0,16,15,0),new(0,15,16,0),new(0,16,16,0),new(1,16,16,0)]);
        foreach(var target in new[]{(0,0),(15,15),(16,15),(15,16),(16,16)}){expected[0][target.Item2*width+target.Item1]=0;flags[0][target.Item2*width+target.Item1]=0;}
        expected[1][16*width+16]=0;flags[1][16*width+16]=0;
        Oracle(new(-20,-40,32*16,32*16));
        first.ExtractSprites(batch,new(-20,-40,16*16,16*16));check.That(batch.Count==0,"last occupied cell removal clears its chunk");
        first.SetCells([new(0,1,1,4,2)]);expected[0][width+1]=4;flags[0][width+1]=2;
        Oracle(new(-20,-40,width*16,height*16));
        check.That(committed.Layers[0].Cell(0)==3&&committed.Layers[0].Cell(width+1)==0&&committed.Layers[0].Flip(15*width+15)==1,"later commits preserve saved cells, flips and occupancy snapshots");
        var beforeSharedEdit=first.Map;first.SetCells([new(2,33,0,3,1)]);
        check.That(ReferenceEquals(first.Map.Layers[0],beforeSharedEdit.Layers[0])&&!ReferenceEquals(first.Map.Layers[2],original.Layers[2])&&original.Layers[2].Cell(33)==0,"editing formerly shared layer preserves other layers and original snapshot");
        second.SetCells([new(0,2,2,1)]);
        check.That(first.Map.Layers[0].Cell(2*width+2)==0&&second.Map.Layers[0].Cell(width+1)==0,"both editable instances remain independent after their own commits");
        var stableView=new TileView(-20,-40,width*16,height*16);
        for(int i=0;i<128;i++)first.ExtractSprites(batch,stableView);
        long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)first.ExtractSprites(batch,stableView);
        check.That(GC.GetAllocatedBytesForCurrentThread()==allocated,"warmed extraction remains allocation-free after editing");
    }

    private static void BatchValidation(EngineHost engine,Checks check)
    {
        var layer=Layer("validation",65,65);layer.Cells[0]=1;
        var asset=Build(65,65,layer);using var instance=TileMapInstance.CreateEditable(engine,asset,new(0,0));
        var snapshot=instance.Map;int loads=engine.Textures.Loads,releases=engine.Textures.Releases;
        instance.SetCells([]);instance.SetCells([new(0,0,0,1)]);
        check.That(ReferenceEquals(snapshot,instance.Map),"empty and identical batches preserve snapshot identity");
        TileCellEdit[] invalid=[new(-1,0,0,1),new(1,0,0,1),new(0,-1,0,1),new(0,65,0,1),new(0,0,-1,1),new(0,0,65,1),new(0,int.MaxValue,int.MaxValue,1),new(0,0,0,-1),new(0,0,0,999),new(0,0,0,1025),new(0,0,0,1,-1),new(0,0,0,1,4),new(0,0,0,0,1)];
        foreach(var edit in invalid)
        {
            check.Reject<ArgumentException>(()=>instance.SetCells([new(0,1,1,4,3),edit]),"invalid edit in otherwise valid batch");
            check.That(ReferenceEquals(snapshot,instance.Map)&&instance.Map.Layers[0].Cell(66)==0,"invalid batch commits no cells or snapshot");
        }
        check.Reject<ArgumentException>(()=>instance.SetCells([new(0,1,1,1),new(0,1,1,2)]),"duplicate target with different values");
        check.Reject<ArgumentException>(()=>instance.SetCells([new(0,0,0,1),new(0,0,0,1)]),"duplicate no-op target");
        check.That(TileMapInstance.MaximumCellEdits==4096,"documented maximum batch size");
        var oversized=new TileCellEdit[TileMapInstance.MaximumCellEdits+1];
        for(int i=0;i<oversized.Length;i++)oversized[i]=new(0,i%65,i/65,3,i%4);
        check.Reject<ArgumentException>(()=>instance.SetCells(oversized),"over-capacity batch");
        check.That(ReferenceEquals(snapshot,instance.Map)&&engine.Textures.Loads==loads&&engine.Textures.Releases==releases,"all rejected edits preserve map and residency");
        var maximum=oversized[..TileMapInstance.MaximumCellEdits];instance.SetCells(maximum);
        check.That(instance.Map.Layers[0].Cell(4095)==3&&instance.Map.Layers[0].Flip(4095)==3&&instance.Map.Layers[0].Cell(4096)==0,"maximum-sized batch commits every entry and nothing beyond it");
        snapshot=instance.Map;instance.SetCells(maximum);
        check.That(ReferenceEquals(snapshot,instance.Map),"maximum-sized no-op batch preserves identity");
    }

    private static void Residency(EngineHost engine,Checks check)
    {
        string directory=Path.Combine(Path.GetTempPath(),"gal-tile-edit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var sampleRoot=new AssetRoot();string bitmap=sampleRoot.Resolve("regions.bmp");
            File.Copy(bitmap,Path.Combine(directory,"used.bmp"));File.Copy(bitmap,Path.Combine(directory,"unused.bmp"));File.Copy(bitmap,Path.Combine(directory,"unreferenced.bmp"));
            var assets=new AssetRoot(directory);var asset=TileMapAsset.Build(new()
            {
                Kind="gal-tilemap",Version=1,Name="Distinct texture residency",Width=2,Height=1,TileWidth=32,TileHeight=32,
                Resources=[new(){Key="used",Path="used.bmp"},new(){Key="unused",Path="unused.bmp"},new(){Key="unreferenced",Path="unreferenced.bmp"}],
                Tiles=[new(){Id=1,AssetKey="used"},new(){Id=2,AssetKey="unused"},new(){Id=3,AssetKey="unused"}],
                Layers=[new(){Name="tiles",Order=0,Cells=[1,0]}]
            },assets.FilePath("residency.tilemap.json"),assets);
            int baseline=engine.Textures.Count;
            using var readOnly=new TileMapInstance(engine,asset,new(0,0));
            check.That(engine.Textures.Count==baseline+1,"legacy residency includes only actually used texture");
            using var editable=TileMapInstance.CreateEditable(engine,asset,new(0,0));
            check.That(engine.Textures.Count==baseline+2,"editable setup pins distinct unused palette texture, excludes nonpalette resource and shares aliases");
            int loads=engine.Textures.Loads,releases=engine.Textures.Releases;
            File.Delete(Path.Combine(directory,"unused.bmp"));
            editable.SetCells([new(0,1,0,2,3)]);var batch=new SpriteBatch();editable.ExtractSprites(batch,new(0,0,64,32));
            check.That(batch.Count==2&&batch.RegionDraws[1].Flags==3,"unused palette entry draws from resident snapshot after source deletion");
            editable.SetCells([new(0,0,0,0),new(0,1,0,0)]);
            editable.SetCells([new(0,1,0,3,2)]);
            check.That(engine.Textures.Count==baseline+2&&engine.Textures.Loads==loads&&engine.Textures.Releases==releases,"edit and erase never load or release textures");
            editable.Dispose();check.That(engine.Textures.Count==baseline+1,"editable disposal releases palette-only lease, preserves sibling lease");
            using var laterReadOnly=new TileMapInstance(engine,asset,new(0,0));
            check.That(engine.Textures.Count==baseline+1,"read-only setup ignores deleted unused palette source");
            check.Reject<AssetException>(()=>TileMapInstance.CreateEditable(engine,asset,new(0,0)),"missing unused palette source rejects setup");
            check.That(engine.Textures.Count==baseline+1,"failed editable setup leaks no texture lease");
        }
        finally { Directory.Delete(directory,true); }
    }

    private static void Lifetime(EngineHost engine,Checks check)
    {
        var asset=Build(2,2,Layer("lifetime",2,2));using var instance=TileMapInstance.CreateEditable(engine,asset,new(0,0));
        Exception? editFailure=null,disposeFailure=null,createFailure=null;
        var thread=new Thread(()=>
        {
            try { instance.SetCells([new(0,0,0,1)]); } catch(Exception e) { editFailure=e; }
            try { instance.Dispose(); } catch(Exception e) { disposeFailure=e; }
            try { TileMapInstance.CreateEditable(engine,asset,new(0,0)); } catch(Exception e) { createFailure=e; }
        });thread.Start();thread.Join();
        check.That(editFailure is InvalidOperationException&&disposeFailure is InvalidOperationException&&createFailure is InvalidOperationException,"edit, setup and disposal obey engine-thread ownership");
        check.That(!instance.IsDisposed&&instance.Map.Layers[0].Cell(0)==0,"foreign-thread operations preserve live instance");
        var world=new World();var scene=world.CreateScene("editable map");var anchor=world.Create("map anchor",scene);
        world.AttachBehavior(anchor,new Idle(),life=>life.OnDetach(instance.Dispose));
        instance.SetCells([new(0,0,0,1)]);world.UnloadScene(scene);
        check.That(instance.IsDisposed,"scene unload disposes edited instance");
        check.Reject<ObjectDisposedException>(()=>instance.SetCells([new(0,0,0,0)]),"disposed editable instance rejects edits");
        check.Reject<ObjectDisposedException>(()=>instance.SetCells([]),"disposed editable instance rejects even empty batch");
    }

    public static int RunPhysics()
    {
        var check=new Checks();
        using var engine=new EngineHost(true,4096);using var physics=engine.OpenPhysics();
        CollisionReplacement(engine,physics,check);
        CapacityAndRetry(engine,physics,check);
        RectangleLimit(engine,physics,check);
        ScaledPlacement(engine,physics,check);
        check.That(physics.State.Bodies==0&&physics.State.Shapes==0&&engine.Textures.Count==0,"edited collision fixtures release all live resources");
        physics.Step();check.That(physics.State.RetiredShapes==0,"caller step reclaims final retired edits");
        ClosedWorld(engine,physics,check);
        Console.WriteLine($"TILEMAP EDIT PHYSICS PASS assertions={check.Count}");return check.Count;
    }

    private static ulong[] ShapeIds(PhysicsWorld physics,float width,float height)
    { var ids=new ulong[512];int count=physics.QueryAabb(-1,-1,width+1,height+1,ids);return ids[..count]; }

    private static void CollisionReplacement(EngineHost engine,PhysicsWorld physics,Checks check)
    {
        var floor=Layer("floor",8,4);floor.Cells[8]=1;floor.Cells[9]=2;
        var hidden=Layer("hidden",8,4,1,0);hidden.Cells[9]=1;
        var asset=Build(8,4,hidden,floor);using var instance=TileMapInstance.CreateEditable(engine,asset,new(0,0));
        var collision=instance.AttachCollision(physics,new(32),friction:.25f,category:4,mask:8);
        using var independent=physics.CreateBody(new(PhysicsBodyType.Dynamic,100,100));
        independent.AddShape(new(PhysicsShapeType.Circle,.2f));var independentBefore=independent.State;
        var originalIds=ShapeIds(physics,8,4);var originalRectangles=collision.Rectangles;var before=physics.State;
        check.That(originalIds.Length==1&&collision.TryGetRectangle(originalIds[0],out var original)&&original==new TileRectangle(0,1,2,1),"initial union has one mapped rectangle");
        instance.SetCells([new(0,0,0,3,3),new(0,0,1,2,2)]);
        check.That(ShapeIds(physics,8,4).SequenceEqual(originalIds)&&physics.State==before,"decorative, solid-type and flip-only edits do not churn physics identities");
        instance.SetCells([new(0,0,1,0),new(1,0,1,1,3),new(0,1,1,0)]);
        check.That(ShapeIds(physics,8,4).SequenceEqual(originalIds)&&physics.State==before,"cross-layer transfer and overlap removal preserve unchanged solid union including hidden layer");
        var snapshot=instance.Map;
        check.Reject<ArgumentException>(()=>instance.SetCells([new(1,0,1,0),new(0,7,3,999)]),"invalid batch with pending collision change");
        check.That(ReferenceEquals(snapshot,instance.Map)&&physics.State==before&&ShapeIds(physics,8,4).SequenceEqual(originalIds),"invalid batch preserves native collision and visual snapshot atomically");
        instance.SetCells([new(1,1,1,0),new(0,3,1,1,1)]);
        var replacementIds=ShapeIds(physics,8,4);
        check.That(collision.Rectangles.Length==2&&collision.Rectangles[0]==new TileRectangle(0,1,1,1)&&collision.Rectangles[1]==new TileRectangle(3,1,1,1),"existing collision object exposes replacement rectangle plan synchronously");
        check.That(replacementIds.Length==2&&!replacementIds.Contains(originalIds[0])&&!collision.TryGetRectangle(originalIds[0],out _),"retired identity is absent from immediate queries and lookup");
        check.That(replacementIds.All(id=>collision.TryGetRectangle(id,out _))&&originalRectangles.SequenceEqual(new[]{new TileRectangle(0,1,2,1)}),"new query identities resolve and borrowed old rectangle span stays unchanged");
        var hit=physics.RayCast(3.5f,-1,0,5,category:8,mask:4);
        check.That(hit.Hit&&collision.TryGetRectangle(hit.Shape,out var rectangle)&&rectangle==new TileRectangle(3,1,1,1),"ray sees newly painted solid without an intervening step");
        check.That(!physics.RayCast(3.5f,-1,0,5,category:1,mask:1).Hit,"replacement retains collision filter settings");
        check.That(!physics.RayCast(1.5f,-1,0,5).Hit&&physics.State.Steps==before.Steps&&independent.State==independentBefore,"removed solid is immediately absent and edit never advances unrelated simulation");
        check.Reject<InvalidOperationException>(()=>instance.AttachCollision(physics,new(32)),"replacement preserves single live attachment");
        instance.SetCells([new(1,0,1,0),new(0,3,1,0)]);
        check.That(collision.Rectangles.Length==0&&ShapeIds(physics,8,4).Length==0&&physics.State.Bodies==1,"erase-to-empty removes generated bodies synchronously");
        instance.SetCells([new(0,7,3,2,3)]);
        var refillIds=ShapeIds(physics,8,4);
        check.That(collision.Rectangles.Length==1&&refillIds.Length==1&&collision.TryGetRectangle(refillIds[0],out rectangle)&&rectangle==new TileRectangle(7,3,1,1),"empty-to-solid repopulates same collision object");
        check.That(!collision.TryGetRectangle(0,out _)&&!collision.TryGetRectangle(ulong.MaxValue,out _),"collision lookup rejects unknown identity");
        var world=new World();var scene=world.CreateScene("edited collision scene");var anchor=world.Create("edited map",scene);
        world.AttachBehavior(anchor,new Idle(),life=>life.OnDetach(instance.Dispose));world.UnloadScene(scene);
        check.That(instance.IsDisposed&&collision.IsDisposed&&physics.State.Bodies==1&&physics.State.Shapes==1&&engine.Textures.Count==0,"scene unload releases latest collision and textures, preserves independent body");
        check.Reject<ObjectDisposedException>(()=>collision.TryGetRectangle(refillIds[0],out _),"disposed updated collision rejects lookup");
        independent.Dispose();physics.Step();
    }

    private static void CapacityAndRetry(EngineHost engine,PhysicsWorld physics,Checks check)
    {
        var source=Layer("capacity",4,1);source.Cells[0]=1;
        using var instance=TileMapInstance.CreateEditable(engine,Build(4,1,source),new(0,0));var collision=instance.AttachCollision(physics,new(32));
        var blockers=new List<PhysicsBody>();
        try
        {
            while(physics.State.Bodies<256)blockers.Add(physics.CreateBody(new(PhysicsBodyType.Static,100,100)));
            var before=physics.State;var ids=ShapeIds(physics,4,1);var snapshot=instance.Map;
            check.Reject<InvalidOperationException>(()=>instance.SetCells([new(0,0,0,0),new(0,1,0,1)]),"replacement requires old plus candidate body headroom");
            check.That(ReferenceEquals(snapshot,instance.Map)&&physics.State==before&&ShapeIds(physics,4,1).SequenceEqual(ids)&&collision.TryGetRectangle(ids[0],out _),"body-budget failure preserves snapshot, queries and world counters");
            instance.SetCells([new(0,0,0,2,3)]);
            check.That(physics.State==before&&ShapeIds(physics,4,1).SequenceEqual(ids),"unchanged solid union is allowed at full body capacity");
            blockers[^1].Dispose();blockers.RemoveAt(blockers.Count-1);
            instance.SetCells([new(0,0,0,0),new(0,1,0,1)]);
            check.That(collision.Rectangles[0]==new TileRectangle(1,0,1,1)&&physics.State.Bodies==255&&physics.State.Steps==before.Steps,"released headroom permits atomic retry without stepping");
        }
        finally { foreach(var body in blockers)body.Dispose(); }
        physics.Step();
        using(var retirement=physics.CreateBody(new(PhysicsBodyType.Static,100,100)))
        {
            while(physics.State.Shapes+physics.State.RetiredShapes<512)retirement.AddShape(new(PhysicsShapeType.Circle,.1f)).Dispose();
            var before=physics.State;var snapshot=instance.Map;var ids=ShapeIds(physics,4,1);
            check.Reject<InvalidOperationException>(()=>instance.SetCells([new(0,1,0,0),new(0,2,0,1)]),"live plus retired shape capacity blocks candidate replacement");
            check.That(ReferenceEquals(snapshot,instance.Map)&&physics.State==before&&ShapeIds(physics,4,1).SequenceEqual(ids),"retired-capacity failure preserves old collision and performs no implicit step");
            instance.SetCells([new(0,1,0,2,1)]);
            check.That(physics.State==before&&ShapeIds(physics,4,1).SequenceEqual(ids),"flip and solid alias edits remain possible with exhausted retired slots");
            physics.Step();uint steps=physics.State.Steps;
            instance.SetCells([new(0,1,0,0),new(0,2,0,1)]);
            check.That(collision.Rectangles[0]==new TileRectangle(2,0,1,1)&&physics.State.Steps==steps&&physics.State.RetiredShapes==1,"same rejected edit succeeds only after explicit shape reclamation");
        }
        collision.Dispose();instance.SetCells([new(0,2,0,0),new(0,3,0,1)]);
        check.That(physics.State.Shapes==0&&instance.Map.Layers[0].Cell(3)==1,"early collision disposal leaves editable visuals usable");
        var reattached=instance.AttachCollision(physics,new(32));
        check.That(reattached.Rectangles[0]==new TileRectangle(3,0,1,1),"reattachment uses most recent edited snapshot");
        instance.Dispose();physics.Step();
    }

    private static void RectangleLimit(EngineHost engine,PhysicsWorld physics,Checks check)
    {
        var source=Layer("checker",32,32);source.Cells[0]=1;
        using(var instance=TileMapInstance.CreateEditable(engine,Build(32,32,source),new(0,0)))
        {
            var collision=instance.AttachCollision(physics,new(32));var before=physics.State;var snapshot=instance.Map;var ids=ShapeIds(physics,32,32);
            var edits=new List<TileCellEdit>();
            for(int y=0;y<32&&edits.Count<129;y++)for(int x=0;x<32&&edits.Count<129;x++)if((x+y)%2==0)edits.Add(new(0,x,y,1));
            check.Reject<InvalidOperationException>(()=>instance.SetCells(edits.ToArray()),"129 disjoint rectangles exceed per-map collision cap");
            check.That(ReferenceEquals(snapshot,instance.Map)&&physics.State==before&&ShapeIds(physics,32,32).SequenceEqual(ids),"rectangle-cap rejection happens before visual or native mutation");
            instance.SetCells(edits.Take(128).ToArray());
            check.That(collision.Rectangles.Length==128&&physics.State.Bodies==128&&physics.State.Shapes==128&&physics.State.Steps==before.Steps,"128 rectangles fit exact cap using candidate headroom");
        }
        physics.Step();
        var wide=Layer("too-wide",256,1);wide.Cells[0]=1;
        using(var instance=TileMapInstance.CreateEditable(engine,Build(256,1,wide),new(0,0)))
        {
            var collision=instance.AttachCollision(physics,new(32));var before=physics.State;var snapshot=instance.Map;
            var edits=Enumerable.Range(0,201).Select(x=>new TileCellEdit(0,x,0,1)).ToArray();
            check.Reject<InvalidOperationException>(()=>instance.SetCells(edits),"merged replacement beyond physics half-extent limit");
            check.That(ReferenceEquals(snapshot,instance.Map)&&physics.State==before&&collision.Rectangles[0]==new TileRectangle(0,0,1,1),"shape-bound preflight failure preserves existing collision");
        }
    }

    private static void ScaledPlacement(EngineHost engine,PhysicsWorld physics,Checks check)
    {
        physics.Step();var source=Layer("scaled",3,2);source.Cells[0]=1;
        using var instance=TileMapInstance.CreateEditable(engine,Build(3,2,source),new(64,32,2));
        var collision=instance.AttachCollision(physics,new(16));var before=physics.State;
        var initial=physics.RayCast(5,0,0,12);check.That(initial.Hit&&Math.Abs(initial.Y-2)<.001f,"initial collision uses translated scaled placement in explicit meter units");
        instance.SetCells([new(0,0,0,0),new(0,2,1,2,3)]);
        var downward=physics.RayCast(14,0,0,12);var upward=physics.RayCast(14,11,0,-8);var leftward=physics.RayCast(20,8,-10,0);
        check.That(downward.Hit&&Math.Abs(downward.Y-6)<.001f&&collision.TryGetRectangle(downward.Shape,out var rectangle)&&rectangle==new TileRectangle(2,1,1,1),"replacement preserves translation, placement scale and attached physics scale");
        check.That(upward.Hit&&Math.Abs(upward.Y-10)<.001f&&leftward.Hit&&Math.Abs(leftward.X-16)<.001f,"replacement preserves full scaled box dimensions");
        check.That(!physics.RayCast(5,0,0,12).Hit&&physics.State.Steps==before.Steps,"scaled edit removes old box without advancing simulation");
    }

    private static void ClosedWorld(EngineHost engine,PhysicsWorld physics,Checks check)
    {
        var source=Layer("world lifetime",2,1);source.Cells[0]=1;
        using var instance=TileMapInstance.CreateEditable(engine,Build(2,1,source),new(0,0));
        var collision=instance.AttachCollision(physics,new(32));var snapshot=instance.Map;var rectangles=collision.Rectangles;
        physics.Dispose();
        check.Reject<ObjectDisposedException>(()=>instance.SetCells([new(0,0,0,0),new(0,1,0,1)]),"solid edit cannot rebuild against closed physics world");
        check.That(ReferenceEquals(snapshot,instance.Map)&&collision.Rectangles.SequenceEqual(rectangles),"closed-world failure preserves visual snapshot and rectangle plan");
        using var reopened=engine.OpenPhysics();using var independent=reopened.CreateBody(new(PhysicsBodyType.Static,100,100));
        independent.AddShape(new(PhysicsShapeType.Circle,.2f));var before=reopened.State;
        collision.Dispose();
        check.That(reopened.State==before&&independent.Id!=0,"old collision disposal never releases handles into reopened world");
        instance.SetCells([new(0,0,0,0),new(0,1,0,1)]);var replacement=instance.AttachCollision(reopened,new(32));
        check.That(replacement.Rectangles[0]==new TileRectangle(1,0,1,1)&&reopened.State.Bodies==2,"edited instance reattaches to new physics world after stale collision disposal");
        instance.Dispose();
        check.That(replacement.IsDisposed&&reopened.State.Bodies==1&&reopened.State.Shapes==1&&engine.Textures.Count==0,"reopened-world cleanup preserves independent body");
        independent.Dispose();reopened.Step();check.That(reopened.State.Bodies==0&&reopened.State.Shapes==0&&reopened.State.RetiredShapes==0,"reopened world finishes with no leaked identities");
    }
}
