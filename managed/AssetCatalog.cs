using System.Collections.ObjectModel;

namespace GameAuthoringLab;

/// <summary>Immutable key-to-logical-path mapping; sample names are only the default catalog.</summary>
internal sealed class AssetCatalog
{
    private static readonly string[] SampleKeys = ["room-a", "room-b", "player", "cell", "status-empty", "status-held", "status-restored"];
    private readonly IReadOnlyDictionary<string, string> _paths;
    private readonly IReadOnlyDictionary<string, TextureAsset> _textures;
    public AssetRoot Assets { get; }
    public string Root => Assets.DirectoryPath;
    public IReadOnlyDictionary<string, string> Paths => _paths;

    public AssetCatalog(string? root = null) : this(new AssetRoot(root), SampleKeys.ToDictionary(key => key, key => key + ".bmp", StringComparer.Ordinal)) { }

    public AssetCatalog(AssetRoot assets, IReadOnlyDictionary<string, string> paths)
        : this(assets, paths.ToDictionary(pair => pair.Key, pair => new TextureAsset(pair.Value), StringComparer.Ordinal)) { }

    public AssetCatalog(AssetRoot assets, IReadOnlyDictionary<string, TextureAsset> textures)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(textures);
        Assets = assets;
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        var regions = new Dictionary<string, TextureAsset>(StringComparer.Ordinal);
        foreach (var pair in textures)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)) throw new ArgumentException("Resource key must be nonempty.", nameof(textures));
            ArgumentNullException.ThrowIfNull(pair.Value);
            assets.ValidateLogicalPath(pair.Value.Path);
            if (pair.Value.Region is {} region) region.Validate(4096, 4096);
            copy.Add(pair.Key, pair.Value.Path); regions.Add(pair.Key, pair.Value);
        }
        _paths = new ReadOnlyDictionary<string, string>(copy);
        _textures = new ReadOnlyDictionary<string, TextureAsset>(regions);
    }

    public bool Exists(string key)
    {
        if (!_paths.TryGetValue(key, out string? logicalPath)) return false;
        try { Assets.Resolve(logicalPath); return true; }
        catch (AssetException) { return false; }
    }

    public string LogicalPathFor(string key) => _paths.TryGetValue(key, out string? path) ? path
        : throw new AssetException("ASSET_KEY", Root, key, "Unregistered resource key.");
    public TextureAsset TextureFor(string key) => _textures.TryGetValue(key, out var texture) ? texture
        : throw new AssetException("ASSET_KEY", Root, key, "Unregistered resource key.");
    internal void ValidateRegion(string key, BitmapInfo info)
    {
        try { TextureFor(key).Region?.Validate(info.Width, info.Height); }
        catch (ArgumentOutOfRangeException e) { throw new AssetException("ASSET_REGION", Root, key, e.Message, e); }
    }
    public string PathFor(string key) => Assets.ValidateBitmap(LogicalPathFor(key));
}

/// <summary>
/// One world's leases. Synchronization prepares all additions before releasing old resources.
/// Validation/upload failures release only candidate leases, retaining the previous usable set.
/// Repeated Sync on an unchanged world does no file I/O and allocates no managed memory.
/// </summary>
internal sealed class TextureBank : IDisposable
{
    public const int MaximumRetainedKeys = 4096;
    private readonly TextureCache _cache;
    private readonly AssetCatalog _catalog;
    private readonly string[] _retainedKeys;
    private readonly Dictionary<string, TextureLease> _loaded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _needed = new(StringComparer.Ordinal);
    private readonly List<string> _remove = [];
    private readonly List<KeyValuePair<string, TextureLease>> _pending = [];
    private bool _disposed;
    public int LoadedCount => _loaded.Count;
    public int Loads { get; private set; }
    public int Releases { get; private set; }
    public TextureBank(EngineHost engine, AssetCatalog catalog) : this(engine, catalog, []) { }

    // Pin known animation frames for this bank's lifetime. Copy and validate keys at
    // setup; actual file/region validation and leasing remain transactional in Sync.
    public TextureBank(EngineHost engine, AssetCatalog catalog, ReadOnlySpan<string> retainedKeys)
    {
        ArgumentNullException.ThrowIfNull(engine); ArgumentNullException.ThrowIfNull(catalog);
        if (retainedKeys.Length > MaximumRetainedKeys) throw new ArgumentOutOfRangeException(nameof(retainedKeys));
        foreach (string key in retainedKeys)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Retained resource keys must be nonempty.", nameof(retainedKeys));
            _ = catalog.TextureFor(key);
        }
        _cache = engine.Textures; _catalog = catalog; _retainedKeys = retainedKeys.ToArray();
    }

    public ulong Resolve(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _loaded.TryGetValue(key, out var lease) ? lease.Handle : throw new InvalidOperationException($"Texture not synchronized: {key}");
    }

    public TextureBinding ResolveRegion(string key) => new(Resolve(key), _catalog.TextureFor(key).Region);

    public void Sync(World world)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cache.CheckAccess();
        _needed.Clear();
        foreach (string key in _retainedKeys) _needed.Add(key);
        var entities = world.Entities;
        for (int i = 0; i < entities.Count; i++)
            if (entities[i].IsAlive && entities[i].Sprite?.AssetKey is {} key) _needed.Add(key);
        foreach (string key in _needed)
            if (!_loaded.ContainsKey(key)) _catalog.ValidateRegion(key, _cache.Validate(_catalog.Assets, _catalog.LogicalPathFor(key)));
        _pending.Clear();
        try
        {
            foreach (string key in _needed)
                if (!_loaded.ContainsKey(key))
                {
                    var lease = _cache.Acquire(_catalog.Assets, _catalog.LogicalPathFor(key));
                    _pending.Add(new(key, lease));
                    _catalog.ValidateRegion(key, lease.Info);
                }
        }
        catch
        {
            foreach (var pair in _pending) pair.Value.Dispose();
            _pending.Clear();
            throw;
        }
        foreach (var pair in _pending) { _loaded.Add(pair.Key, pair.Value); Loads++; }
        _pending.Clear();
        _remove.Clear();
        foreach (string key in _loaded.Keys) if (!_needed.Contains(key)) _remove.Add(key);
        foreach (string key in _remove) { _loaded[key].Dispose(); _loaded.Remove(key); Releases++; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        // Leases can be discarded after engine destruction; they never release into a new context.
        _remove.Clear();
        foreach (string key in _loaded.Keys) _remove.Add(key);
        foreach (string key in _remove) { _loaded[key].Dispose(); _loaded.Remove(key); Releases++; }
        _disposed = true;
    }
}
