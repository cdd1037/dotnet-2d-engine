namespace GameAuthoringLab;

// Retains the original axis-aligned view for ABI regression tests; Draws carries
// the complete affine basis and texture identity for real rendering.
internal sealed class SpriteBatch
{
    private Sprite[] _storage;
    private SpriteDraw[] _draws;
    private SpriteDrawV2[] _regionDraws;
    private SpriteDrawV2[] _scratchRegionDraws=[];
    private bool _requiresRegions;
    public Func<string, TextureBinding>? RegionResolver { get; set; }
    private int[] _layers;
    private Sprite[] _scratchStorage=[];
    private SpriteDraw[] _scratchDraws=[];
    private int[] _scratchLayers=[];
    private bool _legacyCompatible=true;
    public Func<string, ulong>? TextureResolver { get; set; }
    public SpriteBatch(int capacity = 0) { _storage = new Sprite[capacity]; _draws = new SpriteDraw[capacity]; _regionDraws = new SpriteDrawV2[capacity]; _layers = new int[capacity]; }
    public int Count { get; private set; }
    public int Capacity => _storage.Length;
    public ReadOnlySpan<Sprite> Sprites => _legacyCompatible ? _storage.AsSpan(0, Count) : throw new InvalidOperationException("Textured or rotated sprites require the affine Draws API.");
    public ReadOnlySpan<SpriteDraw> Draws => !_requiresRegions ? _draws.AsSpan(0, Count) : throw new InvalidOperationException("Texture regions/flips require RegionDraws.");
    public ReadOnlySpan<SpriteDrawV2> RegionDraws => _regionDraws.AsSpan(0, Count);
    internal void Reset(int capacity)
    {
        Count = 0; _legacyCompatible=true; _requiresRegions=false;
        Reserve(capacity);
    }
    internal void Reserve(int capacity)
    {
        if (_storage.Length < capacity)
        {
            int size = Math.Max(capacity, _storage.Length * 2);
            Array.Resize(ref _storage, size); Array.Resize(ref _draws, size); Array.Resize(ref _regionDraws, size); Array.Resize(ref _layers, size);
        }
    }
    internal void Add(in Transform2D transform, in Sprite2D sprite)
    {
        if(sprite.AssetKey is not null||transform.Rotation!=0||transform.Shear!=0||sprite.FlipX||sprite.FlipY)_legacyCompatible=false;
        float width = sprite.Width * transform.ScaleX, height = sprite.Height * transform.ScaleY;
        if (!float.IsFinite(width) || !float.IsFinite(height)) throw new InvalidOperationException("World sprite extents overflowed.");
        transform.GetBasis(out float m11, out float m12, out float m21, out float m22);
        TextureBinding binding = sprite.AssetKey is { } key ? RegionResolver?.Invoke(key) ?? new TextureBinding(TextureResolver?.Invoke(key) ?? throw new InvalidOperationException($"No texture resolver for {key}.")) : default;
        ulong texture = binding.Handle;
        _requiresRegions |= binding.Region is not null || sprite.FlipX || sprite.FlipY;
        _storage[Count] = new Sprite { X=transform.X,Y=transform.Y,Width=width,Height=height,R=sprite.R,G=sprite.G,B=sprite.B,A=sprite.A };
        _draws[Count] = new SpriteDraw { M11=m11,M12=m12,M21=m21,M22=m22,X=transform.X,Y=transform.Y,Width=sprite.Width,Height=sprite.Height,R=sprite.R,G=sprite.G,B=sprite.B,A=sprite.A,Texture=texture };
        // Headless cache handles are zero: source bounds were validated by TextureBank.
        _regionDraws[Count] = SpriteDrawV2.Create(_draws[Count], texture == 0 ? null : binding.Region, sprite.FlipX, sprite.FlipY);
        _layers[Count++] = sprite.Layer;
    }
    internal void Sort()
    {
        bool ordered=true;
        for(int i=1;i<Count;i++)if(_layers[i-1]>_layers[i]){ordered=false;break;}
        if(ordered)return;
        if(_scratchLayers.Length<Capacity)
        {
            _scratchStorage=new Sprite[Capacity];_scratchDraws=new SpriteDraw[Capacity];_scratchRegionDraws=new SpriteDrawV2[Capacity];_scratchLayers=new int[Capacity];
        }
        // Bottom-up stable merge sort. Left wins equal layers, preserving entity
        // creation/snapshot order and alpha order; textures never alter ordering.
        for(int width=1;width<Count;)
        {
            for(int start=0;start<Count;)
            {
                int middle=start+Math.Min(width,Count-start),end=middle+Math.Min(width,Count-middle);
                int left=start,right=middle;
                for(int target=start;target<end;target++)
                {
                    int source=left<middle&&(right>=end||_layers[left]<=_layers[right])?left++:right++;
                    _scratchStorage[target]=_storage[source];_scratchDraws[target]=_draws[source];_scratchRegionDraws[target]=_regionDraws[source];_scratchLayers[target]=_layers[source];
                }
                start=end;
            }
            (_storage,_scratchStorage)=(_scratchStorage,_storage);
            (_draws,_scratchDraws)=(_scratchDraws,_draws);
            (_regionDraws,_scratchRegionDraws)=(_scratchRegionDraws,_regionDraws);
            (_layers,_scratchLayers)=(_scratchLayers,_layers);
            if(width>Count/2)break;
            width*=2;
        }
    }
}
