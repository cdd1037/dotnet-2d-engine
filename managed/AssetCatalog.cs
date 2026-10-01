namespace GameAuthoringLab;

internal sealed class AssetCatalog
{
    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal) { "room-a", "room-b", "player", "cell", "status-empty", "status-held", "status-restored" };
    public string Root { get; }
    public AssetCatalog(string? root = null)
    {
        Root = Path.GetFullPath(root ?? Environment.GetEnvironmentVariable("GAL_ASSET_ROOT")
            ?? (Directory.Exists(Path.Combine(AppContext.BaseDirectory,"assets")) ? Path.Combine(AppContext.BaseDirectory,"assets") : "assets"));
    }
    public bool Exists(string key) => Keys.Contains(key) && File.Exists(Path.Combine(Root,key+".bmp"));
    public string PathFor(string key)
    {
        if(!Exists(key)) throw new FileNotFoundException($"Missing or unregistered asset: {key}");
        string path=Path.Combine(Root,key+".bmp");
        using var stream=File.OpenRead(path); Span<byte> header=stackalloc byte[54];
        if(stream.Read(header)!=header.Length||header[0]!='B'||header[1]!='M')throw new InvalidDataException($"Invalid BMP asset: {key}");
        int width=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header[18..]);
        int height=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header[22..]);
        if(width is <1 or >4096 || height is <1 or >4096)throw new InvalidDataException($"Invalid BMP dimensions: {key}");
        return path;
    }
}

internal sealed class TextureBank : IDisposable
{
    private readonly EngineHost _engine; private readonly AssetCatalog _catalog;
    private readonly Dictionary<string,ulong> _loaded=new(StringComparer.Ordinal);
    private readonly HashSet<string> _needed=new(StringComparer.Ordinal);
    private readonly List<string> _remove=[]; private readonly List<string> _added=[];
    public int LoadedCount=>_loaded.Count;
    public int Loads {get;private set;} public int Releases {get;private set;}
    public TextureBank(EngineHost engine,AssetCatalog catalog){_engine=engine;_catalog=catalog;}
    public ulong Resolve(string key)=>_loaded.TryGetValue(key,out ulong value)?value:throw new InvalidOperationException($"Texture not synchronized: {key}");
    public void Sync(World world)
    {
        _needed.Clear();var entities=world.Entities;
        for(int i=0;i<entities.Count;i++)if(entities[i].IsAlive&&entities[i].Sprite?.AssetKey is {} key)_needed.Add(key);
        // Validate every path before changing ownership. Upload failures release newly added resources.
        foreach(string key in _needed)if(!_loaded.ContainsKey(key))_catalog.PathFor(key);
        _added.Clear();
        try{foreach(string key in _needed)if(!_loaded.ContainsKey(key)){string path=_catalog.PathFor(key);ulong value=_engine.Headless?0:_engine.LoadTexture(path);_loaded.Add(key,value);_added.Add(key);Loads++;}}
        catch{foreach(string key in _added){if(!_engine.Headless)_engine.ReleaseTexture(_loaded[key]);_loaded.Remove(key);Releases++;}throw;}
        _remove.Clear();foreach(string key in _loaded.Keys)if(!_needed.Contains(key))_remove.Add(key);
        foreach(string key in _remove){if(!_engine.Headless)_engine.ReleaseTexture(_loaded[key]);_loaded.Remove(key);Releases++;}
    }
    public void Dispose(){foreach(ulong id in _loaded.Values)if(!_engine.Headless)_engine.ReleaseTexture(id);_loaded.Clear();}
}
