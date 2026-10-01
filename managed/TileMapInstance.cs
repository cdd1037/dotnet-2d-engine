using System.Numerics;
namespace GameAuthoringLab;

public readonly record struct TileMapPlacement(float X, float Y, float Scale = 1)
{
    internal void Validate(TileMap map)
    {
        if(!float.IsFinite(X)||!float.IsFinite(Y)||Math.Abs(X)>10_000_000||Math.Abs(Y)>10_000_000||!float.IsFinite(Scale)||Scale is <.01f or >100
            || !float.IsFinite((float)(X+(double)map.Width*map.TileWidth*Scale)) || !float.IsFinite((float)(Y+(double)map.Height*map.TileHeight*Scale)))
            throw new ArgumentOutOfRangeException(nameof(TileMapPlacement),"Finite origin within +/-10000000 pixels and uniform scale in [.01,100] required.");
    }
}
/// <summary>Half-open world pixel bounds for visibility culling, not a scissor rectangle.</summary>
public readonly record struct TileView(double X, double Y, double Width, double Height)
{
    internal void Validate()
    {
        if(!double.IsFinite(X)||!double.IsFinite(Y)||!double.IsFinite(Width)||!double.IsFinite(Height)||Width<0||Height<0||!double.IsFinite(X+Width)||!double.IsFinite(Y+Height))
            throw new ArgumentOutOfRangeException(nameof(TileView),"Expected finite nonnegative view extents.");
    }
    public static TileView FromCamera(Camera camera,Viewport viewport)
    {
        if(!float.IsFinite(camera.X)||!float.IsFinite(camera.Y)||!float.IsFinite(camera.Zoom)||camera.Zoom is <.01f or >100) throw new ArgumentOutOfRangeException(nameof(camera));
        return viewport.IsValid ? new(camera.X,camera.Y,viewport.PixelWidth/(double)camera.Zoom,viewport.PixelHeight/(double)camera.Zoom) : default;
    }
}
/// <summary>Immutable placement; owns texture leases and optional generated static collision.</summary>
public sealed class TileMapInstance : IDisposable
{
    private readonly EngineHost _engine;
    private readonly TextureBank _bank;
    private readonly Func<string,TextureBinding> _resolver;
    private TileMapCollision? _collision;
    public TileMap Map { get; }
    public TileMapPlacement Placement { get; }
    public bool IsDisposed { get; private set; }
    public TileMapInstance(EngineHost engine,LoadedTileMap asset,TileMapPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(engine); ArgumentNullException.ThrowIfNull(asset); engine.AssertAlive(); placement.Validate(asset.Map);
        _engine=engine; Map=asset.Map; Placement=placement; _bank=new(engine,asset.Catalog,Map.AssetKeys); _resolver=_bank.ResolveRegion;
        try { _bank.Sync(new World()); } catch { _bank.Dispose(); throw; }
    }
    private void CheckAccess() { _engine.AssertAlive(); ObjectDisposedException.ThrowIf(IsDisposed,this); }
    public bool TryWorldToCell(Vector2 world,out int x,out int y)
    {
        CheckAccess(); x=y=0;
        if(!float.IsFinite(world.X)||!float.IsFinite(world.Y)) return false;
        double cx=((double)world.X-Placement.X)/(Map.TileWidth*(double)Placement.Scale),cy=((double)world.Y-Placement.Y)/(Map.TileHeight*(double)Placement.Scale);
        if(cx<0||cy<0||cx>=Map.Width||cy>=Map.Height) return false;
        x=(int)Math.Floor(cx); y=(int)Math.Floor(cy); return true;
    }
    public void ExtractSprites(SpriteBatch batch,TileView view)
    { ArgumentNullException.ThrowIfNull(batch); CheckAccess(); view.Validate(); batch.Reset(0); AppendSprites(batch,view); }
    /// <summary>Append to entity/map draws, then stable-sort by numeric layer. Existing equal-layer draws stay first.</summary>
    public void AppendSprites(SpriteBatch batch,TileView view)
    {
        ArgumentNullException.ThrowIfNull(batch); CheckAccess(); view.Validate();
        int count=Visit(view,null);
        if((long)batch.Count+count>_engine.MaximumSprites) throw new InvalidOperationException($"Tile map '{Map.Name}' exceeds the configured whole-frame sprite capacity {_engine.MaximumSprites}; cull more or use a larger supported frame budget.");
        batch.Reserve(batch.Count+count); var old=batch.RegionResolver;
        try { batch.RegionResolver=_resolver; Visit(view,batch); batch.Sort(); }
        catch { batch.Reset(0); throw; } // Never expose a plausible partially appended frame.
        finally { batch.RegionResolver=old; }
    }
    private int Visit(TileView view,SpriteBatch? batch)
    {
        if(view.Width==0||view.Height==0) return 0;
        double width=Map.TileWidth*(double)Placement.Scale,height=Map.TileHeight*(double)Placement.Scale;
        static int Bound(double value,int limit,bool upper) => (int)Math.Clamp(upper?Math.Ceiling(value):Math.Floor(value),0,limit);
        int x0=Bound((view.X-Placement.X)/width,Map.Width,false),x1=Bound((view.X+view.Width-Placement.X)/width,Map.Width,true);
        int y0=Bound((view.Y-Placement.Y)/height,Map.Height,false),y1=Bound((view.Y+view.Height-Placement.Y)/height,Map.Height,true);
        if(x0>=x1||y0>=y1) return 0;
        int chunksWide=(Map.Width+TileMap.ChunkSize-1)/TileMap.ChunkSize,count=0;
        foreach(var layer in Map.Layers)
        {
            if(layer.Opacity==0) continue;
            for(int y=y0;y<y1;y++)
                for(int chunk=x0/TileMap.ChunkSize;chunk<=(x1-1)/TileMap.ChunkSize;chunk++)
                {
                    if(!layer.ChunkOccupied((y/TileMap.ChunkSize)*chunksWide+chunk)) continue;
                    int start=Math.Max(x0,chunk*TileMap.ChunkSize),end=Math.Min(x1,(chunk+1)*TileMap.ChunkSize);
                    for(int x=start;x<end;x++)
                    {
                        int index=y*Map.Width+x,id=layer.Cell(index); if(id==0) continue; count++;
                        if(batch is not null)
                        {
                            byte flip=layer.Flip(index);
                            var transform=new Transform2D((float)(Placement.X+x*width),(float)(Placement.Y+y*height),Placement.Scale,Placement.Scale);
                            var sprite=new Sprite2D(Map.TileWidth,Map.TileHeight,A:layer.Opacity,AssetKey:Map.Tile(id).AssetKey,Layer:layer.Order,FlipX:(flip&1)!=0,FlipY:(flip&2)!=0);
                            batch.Add(transform,sprite);
                        }
                    }
                }
        }
        return count;
    }
    public TileMapCollision AttachCollision(PhysicsWorld world,PhysicsScale scale,float friction=.6f,ulong category=1,ulong mask=ulong.MaxValue)
    {
        CheckAccess(); ArgumentNullException.ThrowIfNull(world);
        if(world.Context!=_engine.NativeContext) throw new InvalidOperationException("Tile collision must belong to this instance's engine context.");
        if(_collision is not null && !_collision.IsDisposed) throw new InvalidOperationException("This tile map already has collision.");
        var plan=TileCollisionPlan.Create(Map,Placement,scale,friction,category,mask);
        return _collision=TileMapCollision.Create(world,plan);
    }
    public void Dispose()
    {
        _engine.AssertThread(); if(IsDisposed) return;
        _collision?.Dispose(); _bank.Dispose(); IsDisposed=true;
    }
}
