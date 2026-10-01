namespace GameAuthoringLab;

/// <summary>
/// Synchronous engine/context-owned BMP cache. Identity is the full source path (ordinal).
/// A live entry is a retained snapshot: edits/deletion are observed only after its last lease
/// is released and a later acquire reloads. No global cache, background I/O or implicit hot reload.
/// </summary>
public sealed class TextureCache : IEngineOwned
{
    private readonly EngineHost engine;
    internal TextureCache(EngineHost engine)=>this.engine=engine;
    public const int MaximumTextures = 256;
    private sealed class Entry(ulong handle, BitmapInfo info) { public ulong Handle { get; } = handle; public BitmapInfo Info { get; } = info; public int References = 1; }
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private bool _destroyed;
    public int Count { get { CheckAccess(); return _entries.Count; } }
    public int Loads { get; private set; }
    public int Releases { get; private set; }

    internal void CheckAccess()
    {
        engine.AssertAlive();
        ObjectDisposedException.ThrowIf(_destroyed, this);
    }

    internal BitmapInfo Validate(AssetRoot assets, string logicalPath)
    {
        CheckAccess();
        return _entries.TryGetValue(assets.FilePath(logicalPath), out var entry) ? entry.Info : assets.ReadBitmapInfo(logicalPath);
    }

    public TextureLease Acquire(AssetRoot assets, string logicalPath)
    {
        CheckAccess();
        string path = assets.FilePath(logicalPath);
        if (_entries.TryGetValue(path, out var existing))
        {
            if (existing.References == int.MaxValue) throw new InvalidOperationException("Texture lease count exhausted.");
            var retained = new TextureLease(this, path, existing.Handle, existing.Info);
            existing.References++;
            return retained;
        }
        if (_entries.Count >= MaximumTextures)
            throw new AssetException("ASSET_CAPACITY", assets.DirectoryPath, logicalPath, $"At most {MaximumTextures} distinct textures per engine cache.");
        var info = assets.ReadBitmapInfo(logicalPath);
        ulong handle;
        try { handle = engine.Headless ? 0 : engine.LoadTexture(path); }
        catch (InvalidOperationException e)
        { throw new AssetException("ASSET_UPLOAD", assets.DirectoryPath, logicalPath, e.Message, e); }
        try
        {
            if (!engine.Headless) { var decoded = engine.GetTextureInfo(handle); info = new(path, decoded.Width, decoded.Height); }
            var lease = new TextureLease(this, path, handle, info);
            _entries.Add(path, new Entry(handle, info));
            Loads++;
            return lease;
        }
        catch { if (!engine.Headless) engine.ReleaseTexture(handle); throw; }
    }

    internal void Release(string path)
    {
        engine.AssertThread();
        if (_destroyed) return;
        CheckAccess();
        var entry = _entries[path];
        if (entry.References > 1) { entry.References--; return; }
        // Only mutate ownership after successful native release, permitting a failed release retry.
        if (!engine.Headless) engine.ReleaseTexture(entry.Handle);
        _entries.Remove(path); Releases++;
    }

    void IEngineOwned.EngineDestroyed()=>EngineDestroyed();
    internal void EngineDestroyed()
    {
        // Native context destruction owns its texture cleanup; outstanding leases become invalid.
        Releases += _entries.Count;
        _entries.Clear(); _destroyed = true;
    }
}

public sealed class TextureLease : IDisposable
{
    private readonly TextureCache cache; private readonly string path; private readonly ulong handle; private readonly BitmapInfo info;
    internal TextureLease(TextureCache cache,string path,ulong handle,BitmapInfo info){this.cache=cache;this.path=path;this.handle=handle;this.info=info;}
    private bool _disposed;
    public BitmapInfo Info { get { ObjectDisposedException.ThrowIf(_disposed, this); cache.CheckAccess(); return info; } }
    public ulong Handle
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); cache.CheckAccess(); return handle; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        cache.Release(path); _disposed = true;
    }
}
