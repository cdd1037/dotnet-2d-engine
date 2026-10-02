namespace GameAuthoringLab;

// Retains the original axis-aligned view for ABI regression tests; Draws carries
// the complete affine basis and texture identity for real rendering.
public sealed class SpriteBatch
{
    private Sprite[] _storage;
    private SpriteCommand[] _commands;
    private SpriteCommand[] _scratchCommands = [];
    private bool _commandsAvailable = true;
    private bool[] _nativeResources;
    private bool[] _scratchNativeResources = [];
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
    internal Func<string, ulong>? TextureResolver { get; set; }
    public SpriteBatch(int capacity = 0) { _commands = new SpriteCommand[capacity]; _nativeResources = new bool[capacity]; _storage = new Sprite[capacity]; _draws = new SpriteDraw[capacity]; _regionDraws = new SpriteDrawV2[capacity]; _layers = new int[capacity]; }
    public int Count { get; private set; }
    public int Capacity => _storage.Length;
    /// <summary>Borrowed sorted descriptions, valid until the next extraction/mutation.
    /// Use with RenderFrame, or copy into caller-owned reusable storage to customize materials.</summary>
    public ReadOnlySpan<SpriteCommand> Commands => _commandsAvailable ? _commands.AsSpan(0, Count)
        : throw new InvalidOperationException("Internal ABI-only resource fixtures have no typed command view.");
    internal ReadOnlySpan<Sprite> Sprites => _legacyCompatible ? _storage.AsSpan(0, Count) : throw new InvalidOperationException("Textured or rotated sprites require the affine Draws API.");
    internal ReadOnlySpan<SpriteDraw> Draws => !_requiresRegions ? _draws.AsSpan(0, Count) : throw new InvalidOperationException("Texture regions/flips require RegionDraws.");
    internal ReadOnlySpan<SpriteDrawV2> RegionDraws => _regionDraws.AsSpan(0, Count);
    internal void Reset(int capacity)
    {
        Array.Clear(_commands, 0, Count); Array.Clear(_scratchCommands);
        _commandsAvailable = true;
        Count = 0; _legacyCompatible=true; _requiresRegions=false;
        Reserve(capacity);
    }
    internal void Reserve(int capacity)
    {
        if (_storage.Length < capacity)
        {
            int size = Math.Max(capacity, _storage.Length * 2);
            Array.Resize(ref _commands, size); Array.Resize(ref _nativeResources, size); Array.Resize(ref _storage, size); Array.Resize(ref _draws, size); Array.Resize(ref _regionDraws, size); Array.Resize(ref _layers, size);
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
        _nativeResources[Count] = binding.NativeOnly;
        _commandsAvailable &= !binding.NativeOnly;
        _commands[Count] = new(transform, new(sprite.Width, sprite.Height), new(sprite.R, sprite.G, sprite.B, sprite.A), binding.Texture)
            { Region = binding.Region, FlipX = sprite.FlipX, FlipY = sprite.FlipY };
        _requiresRegions |= binding.Region is not null || sprite.FlipX || sprite.FlipY;
        _storage[Count] = new Sprite { X=transform.X,Y=transform.Y,Width=width,Height=height,R=sprite.R,G=sprite.G,B=sprite.B,A=sprite.A };
        _draws[Count] = new SpriteDraw { M11=m11,M12=m12,M21=m21,M22=m22,X=transform.X,Y=transform.Y,Width=sprite.Width,Height=sprite.Height,R=sprite.R,G=sprite.G,B=sprite.B,A=sprite.A,Texture=texture };
        // Headless cache handles are zero: source bounds were validated by TextureBank.
        _regionDraws[Count] = SpriteDrawV2.Create(_draws[Count], texture == 0 ? null : binding.Region, sprite.FlipX, sprite.FlipY);
        _layers[Count++] = sprite.Layer;
    }
    internal void ValidateResources(EngineHost engine)
    {
        engine.AssertAlive();
        for (int i = 0; i < Count; i++)
            if (!_nativeResources[i]) _ = _commands[i].Texture.Resolve(engine, _commands[i].Region);
    }
    internal void Sort()
    {
        bool ordered=true;
        for(int i=1;i<Count;i++)if(_layers[i-1]>_layers[i]){ordered=false;break;}
        if(ordered)return;
        if(_scratchLayers.Length<Capacity)
        {
            _scratchCommands=new SpriteCommand[Capacity]; _scratchNativeResources=new bool[Capacity]; _scratchStorage=new Sprite[Capacity];_scratchDraws=new SpriteDraw[Capacity];_scratchRegionDraws=new SpriteDrawV2[Capacity];_scratchLayers=new int[Capacity];
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
                    _scratchCommands[target]=_commands[source]; _scratchNativeResources[target]=_nativeResources[source]; _scratchStorage[target]=_storage[source];_scratchDraws[target]=_draws[source];_scratchRegionDraws[target]=_regionDraws[source];_scratchLayers[target]=_layers[source];
                }
                start=end;
            }
            (_commands,_scratchCommands)=(_scratchCommands,_commands);
            (_nativeResources,_scratchNativeResources)=(_scratchNativeResources,_nativeResources);
            (_storage,_scratchStorage)=(_scratchStorage,_storage);
            (_draws,_scratchDraws)=(_scratchDraws,_draws);
            (_regionDraws,_scratchRegionDraws)=(_scratchRegionDraws,_regionDraws);
            (_layers,_scratchLayers)=(_scratchLayers,_layers);
            if(width>Count/2)break;
            width*=2;
        }
    }
}
