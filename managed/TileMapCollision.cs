namespace GameAuthoringLab;

public readonly record struct TileRectangle(int X,int Y,int Width,int Height);
/// <summary>Pure CPU greedy union plan. Visual opacity/flips do not change an entire-cell solid box.</summary>
internal sealed class TileCollisionPlan
{
    public const int MaximumRectangles=128;
    private readonly TileRectangle[] _rectangles;
    private readonly PhysicsBodyDefinition[] _bodies;
    private readonly PhysicsShapeDefinition[] _shapes;
    public ReadOnlySpan<TileRectangle> Rectangles=>_rectangles;
    internal PhysicsBodyDefinition Body(int index)=>_bodies[index];
    internal PhysicsShapeDefinition Shape(int index)=>_shapes[index];
    private TileCollisionPlan(TileRectangle[] rectangles,PhysicsBodyDefinition[] bodies,PhysicsShapeDefinition[] shapes)
    { _rectangles=rectangles; _bodies=bodies; _shapes=shapes; }
    public static TileCollisionPlan Create(TileMap map,TileMapPlacement placement,PhysicsScale scale,float friction=.6f,ulong category=1,ulong mask=ulong.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(map); placement.Validate(map); _=scale.ToMeters(0);
        var occupied=new bool[map.Width*map.Height];
        foreach(var layer in map.Layers) for(int i=0;i<occupied.Length;i++) if(layer.Cell(i)!=0 && map.Tile(layer.Cell(i)).Solid) occupied[i]=true;
        var rectangles=new List<TileRectangle>();
        for(int y=0;y<map.Height;y++) for(int x=0;x<map.Width;x++)
        {
            if(!occupied[y*map.Width+x]) continue;
            int width=1; while(x+width<map.Width && occupied[y*map.Width+x+width]) width++;
            int height=1;
            while(y+height<map.Height)
            {
                bool full=true; for(int xx=0;xx<width;xx++) if(!occupied[(y+height)*map.Width+x+xx]) { full=false; break; }
                if(!full) break; height++;
            }
            if(rectangles.Count==MaximumRectangles) throw new InvalidOperationException($"Tile map '{map.Name}' needs more than {MaximumRectangles} collision rectangles; simplify solid geometry before attaching physics.");
            rectangles.Add(new(x,y,width,height));
            for(int yy=y;yy<y+height;yy++) Array.Fill(occupied,false,yy*map.Width+x,width);
        }
        var bodies=new PhysicsBodyDefinition[rectangles.Count]; var shapes=new PhysicsShapeDefinition[rectangles.Count];
        // Validate all candidates before the first native mutation, including empty-map material inputs.
        var material=new PhysicsShapeDefinition(PhysicsShapeType.Box,1,1,Friction:friction,Category:category,Mask:mask); material.Validate();
        for(int i=0;i<rectangles.Count;i++)
        {
            var r=rectangles[i]; double tileWidth=map.TileWidth*(double)placement.Scale,tileHeight=map.TileHeight*(double)placement.Scale;
            float x=scale.ToMeters((float)(placement.X+(r.X+r.Width*.5)*tileWidth)),y=scale.ToMeters((float)(placement.Y+(r.Y+r.Height*.5)*tileHeight));
            float halfWidth=scale.ToMeters((float)(r.Width*tileWidth*.5)),halfHeight=scale.ToMeters((float)(r.Height*tileHeight*.5));
            bodies[i]=new(PhysicsBodyType.Static,x,y); shapes[i]=material with { A=halfWidth,B=halfHeight };
            try { bodies[i].Validate(); shapes[i].Validate(); }
            catch(ArgumentOutOfRangeException e) { throw new InvalidOperationException($"Tile collision rectangle ({r.X},{r.Y},{r.Width},{r.Height}) exceeds physics meter/shape bounds; adjust pixel scale or solid layout.",e); }
        }
        return new(rectangles.ToArray(),bodies,shapes);
    }
}
public sealed class TileMapCollision : IDisposable
{
    private readonly PhysicsScope _scope;
    private readonly TileCollisionPlan _plan;
    private readonly ulong[] _shapeIds;
    public bool IsDisposed { get; private set; }
    public ReadOnlySpan<TileRectangle> Rectangles=>_plan.Rectangles;
    private TileMapCollision(PhysicsScope scope,TileCollisionPlan plan,ulong[] shapes) { _scope=scope; _plan=plan; _shapeIds=shapes; }
    internal static TileMapCollision Create(PhysicsWorld world,TileCollisionPlan plan)
    {
        var state=world.State; int count=plan.Rectangles.Length;
        if(state.Bodies+count>256 || state.Shapes+state.RetiredShapes+count>512) throw new InvalidOperationException("Tile collision exceeds remaining world body/shape capacity, including retired shape identities; no bodies were created.");
        var scope=new PhysicsScope(world); var ids=new ulong[count];
        try
        {
            for(int i=0;i<count;i++) { var body=scope.CreateBody(plan.Body(i)); ids[i]=body.AddShape(plan.Shape(i)).Id; }
            return new(scope,plan,ids);
        }
        catch { scope.Dispose(); throw; } // Rollback retires shapes until the caller's next explicit physics step.
    }
    public bool TryGetRectangle(ulong shapeId,out TileRectangle rectangle)
    {
        ObjectDisposedException.ThrowIf(IsDisposed,this);
        int index=Array.IndexOf(_shapeIds,shapeId); rectangle=index<0?default:_plan.Rectangles[index]; return index>=0;
    }
    public void Dispose() { if(IsDisposed)return; _scope.Dispose(); IsDisposed=true; }
}
