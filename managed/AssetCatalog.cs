using System.Collections.ObjectModel;

namespace GameAuthoringLab;

/// <summary>Immutable key-to-logical-path mapping; sample names are only the default catalog.</summary>
internal sealed class AssetCatalog
{
    private static readonly string[] SampleKeys = ["room-a", "room-b", "player", "cell", "status-empty", "status-held", "status-restored"];
    private readonly IReadOnlyDictionary<string, string> _paths;
    public AssetRoot Assets { get; }
    public string Root => Assets.DirectoryPath;
    public IReadOnlyDictionary<string, string> Paths => _paths;

    public AssetCatalog(string? root = null) : this(new AssetRoot(root), SampleKeys.ToDictionary(key => key, key => key + ".bmp", StringComparer.Ordinal)) { }

    public AssetCatalog(AssetRoot assets, IReadOnlyDictionary<string, string> paths)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(paths);
        Assets = assets;
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in paths)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)) throw new ArgumentException("Resource key must be nonempty.", nameof(paths));
            assets.ValidateLogicalPath(pair.Value);
            copy.Add(pair.Key, pair.Value);
        }
        _paths = new ReadOnlyDictionary<string, string>(copy);
    }

    public bool Exists(string key)
    {
        if (!_paths.TryGetValue(key, out string? logicalPath)) return false;
        try { Assets.Resolve(logicalPath); return true; }
        catch (AssetException) { return false; }
    }

    public string LogicalPathFor(string key) => _paths.TryGetValue(key, out string? path) ? path
        : throw new AssetException("ASSET_KEY", Root, key, "Unregistered resource key.");
    public string PathFor(string key) => Assets.ValidateBitmap(LogicalPathFor(key));
}

/// <summary>
/// One world's leases. Synchronization prepares all additions before releasing old resources.
/// Validation/upload failures release only candidate leases, retaining the previous usable set.
/// Repeated Sync on an unchanged world does no file I/O and allocates no managed memory.
/// </summary>
internal sealed class TextureBank : IDisposable
{
    private readonly TextureCache _cache;
    private readonly AssetCatalog _catalog;
    private readonly Dictionary<string, TextureLease> _loaded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _needed = new(StringComparer.Ordinal);
    private readonly List<string> _remove = [];
    private readonly List<KeyValuePair<string, TextureLease>> _pending = [];
    private bool _disposed;
    public int LoadedCount => _loaded.Count;
    public int Loads { get; private set; }
    public int Releases { get; private set; }
    public TextureBank(EngineHost engine, AssetCatalog catalog) { _cache = engine.Textures; _catalog = catalog; }

    public ulong Resolve(string key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _loaded.TryGetValue(key, out var lease) ? lease.Handle : throw new InvalidOperationException($"Texture not synchronized: {key}");
    }

    public void Sync(World world)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cache.CheckAccess();
        _needed.Clear(); var entities = world.Entities;
        for (int i = 0; i < entities.Count; i++)
            if (entities[i].IsAlive && entities[i].Sprite?.AssetKey is {} key) _needed.Add(key);
        foreach (string key in _needed)
            if (!_loaded.ContainsKey(key)) _cache.Validate(_catalog.Assets, _catalog.LogicalPathFor(key));
        _pending.Clear();
        try
        {
            foreach (string key in _needed)
                if (!_loaded.ContainsKey(key))
                    _pending.Add(new(key, _cache.Acquire(_catalog.Assets, _catalog.LogicalPathFor(key))));
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
