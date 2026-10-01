namespace GameAuthoringLab;

// Contexts must be explicitly disposed on the creating thread. A SafeHandle
// finalizer would destroy on an arbitrary GC thread and violate this ABI.
public sealed unsafe class EngineHost : IDisposable
{
    private nint _context;
    public bool Headless { get; }
    public uint MaximumSprites { get; }
    private IEngineOwned? _textures;
    private IEngineOwned? _audio;
    private IEngineOwned? _physics;
    private IEngineOwned? _ui;
    internal void AcquireUi(IEngineOwned owner) { AssertAlive(); if(_ui is not null)throw new InvalidOperationException("This engine already has a managed UI owner."); _ui=owner; }
    internal void ReleaseUi(IEngineOwned owner) { AssertThread(); if(ReferenceEquals(_ui,owner))_ui=null; }
    internal PhysicsWorld OpenPhysics(PhysicsSettings? settings=null)
    {
        AssertAlive();if(_physics is not null)throw new InvalidOperationException("A physics world is already open.");
        var world=new PhysicsWorld(this,settings??PhysicsSettings.Default);_physics=world;return world;
    }
    internal void PhysicsClosed(PhysicsWorld world){if(ReferenceEquals(_physics,world))_physics=null;}
    internal AudioSession OpenAudio(bool offline=false)
    {
        AssertAlive();if(_audio is not null)throw new InvalidOperationException("An audio session is already open.");
        var session=new AudioSession(this,offline);_audio=session;return session;
    }
    internal void AudioClosed(AudioSession session){if(ReferenceEquals(_audio,session))_audio=null;}
    public TextureCache Textures { get { AssertAlive(); return (TextureCache)(_textures ??= new TextureCache(this)); } }
    internal void AssertThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("The engine must be used on its creating thread.");
    }
    internal void AssertAlive() => _ = Context;
    internal nint NativeContext => Context;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;

    internal EngineHost(bool headless, uint maxSprites, bool legacyTone = true)
    {
        if (Native.AbiVersion() != 1)
            throw new InvalidOperationException("This host requires gal ABI version 1.");
        Headless = headless; MaximumSprites = maxSprites;
        var config = Config.Create(headless, maxSprites);
        if (!legacyTone) config.Flags &= ~Native.Audio;
        nint context = 0;
        Native.Check(Native.Create(&config, &context), "create");
        _context = context;
    }

    public static EngineHost Create(bool headless=false,uint maxSprites=1024)=>new(headless,maxSprites,legacyTone:false);

    public string Backend => Native.Utf8(Native.Backend(Context));

    private nint Context
    {
        get
        {
            AssertThread();
            ObjectDisposedException.ThrowIf(_context == 0, this);
            return _context;
        }
    }

    public InputSnapshot PollInput()
    {
        var input = new InputSnapshot { Size = (uint)sizeof(InputSnapshot), Version = 2 };
        Native.Check(Native.PollV2(Context, &input), "poll input v2");
        return input;
    }

    internal Input Poll()
    {
        var input = new Input { Size = (uint)sizeof(Input) };
        Native.Check(Native.Poll(Context, &input), "poll");
        return input;
    }

    public void Draw(in Camera camera, ReadOnlySpan<Sprite> sprites)
    {
        var value = camera;
        nint context = Context;
        Native.Check(Native.Begin(context, &value), "begin");
        try
        {
            fixed (Sprite* buffer = sprites)
                Native.Check(Native.Submit(context, buffer, (uint)sprites.Length), "submit");
        }
        catch (Exception submitError)
        {
            // A rejected submit leaves the native frame open. Discard it so
            // callers can catch bad input and safely draw the next frame.
            if (Native.Abort(context) != 0)
                throw new InvalidOperationException($"Batch recovery failed: {Native.Error()}", submitError);
            throw;
        }
        // End consumes the active frame even when presentation fails.
        Native.Check(Native.End(context), "end");
    }

    public void Draw(in Camera camera, ReadOnlySpan<SpriteDraw> draws)
    {
        var value = camera;
        nint context = Context;
        Native.Check(Native.Begin(context, &value), "begin");
        try
        {
            fixed (SpriteDraw* buffer = draws)
                Native.Check(Native.SubmitDraws(context, buffer, (uint)draws.Length), "submit affine draws");
        }
        catch { Native.Abort(context); throw; }
        Native.Check(Native.End(context), "end");
    }

    public void Draw(in Camera camera, ReadOnlySpan<SpriteDrawV2> draws)
    {
        var value = camera; nint context = Context;
        Native.Check(Native.Begin(context, &value), "begin");
        try { fixed (SpriteDrawV2* buffer = draws) Native.Check(Native.SubmitDrawsV2(context, buffer, (uint)draws.Length), "submit region draws"); }
        catch { Native.Abort(context); throw; }
        Native.Check(Native.End(context), "end");
    }
    /// <summary>Submit an overlay after the scene in one frame, with the same camera and no scissor.</summary>
    public void DrawWithOverlay(in Camera camera,ReadOnlySpan<SpriteDrawV2> scene,ReadOnlySpan<SpriteDrawV2> overlay)
    {
        var value=camera;nint context=Context;Native.Check(Native.Begin(context,&value),"begin");
        try
        {
            fixed(SpriteDrawV2* data=scene)Native.Check(Native.SubmitDrawsV2(context,data,(uint)scene.Length),"submit scene");
            fixed(SpriteDrawV2* data=overlay)Native.Check(Native.SubmitDrawsV2(context,data,(uint)overlay.Length),"submit overlay");
        }
        catch{Native.Abort(context);throw;}
        Native.Check(Native.End(context),"end");
    }
    /// <summary>Draw order is unchanged. Zero clips disables scissor; one broadcasts; otherwise clips match the final draw order.</summary>
    public void Draw(in Camera camera,ReadOnlySpan<SpriteDrawV2> draws,ReadOnlySpan<FramebufferClip> clips)
    {
        if(clips.Length!=0&&clips.Length!=1&&clips.Length!=draws.Length)throw new ArgumentException("Clip count must be zero, one, or match draw count.",nameof(clips));
        foreach(var clip in clips)clip.Validate();
        var value=camera;nint context=Context;Native.Check(Native.Begin(context,&value),"begin");
        try{fixed(SpriteDrawV2* data=draws)fixed(FramebufferClip* scissor=clips)Native.Check(ClippingNative.Submit(context,data,(uint)draws.Length,scissor,(uint)clips.Length),"submit clipped region draws");}
        catch{Native.Abort(context);throw;}
        Native.Check(Native.End(context),"end");
    }
    public void Draw(in Camera camera,ReadOnlySpan<SpriteDrawV2> draws,in FramebufferClip clip)
    {
        clip.Validate();var value=camera;nint context=Context;Native.Check(Native.Begin(context,&value),"begin");
        try{fixed(SpriteDrawV2* data=draws)fixed(FramebufferClip* scissor=&clip)Native.Check(ClippingNative.Submit(context,data,(uint)draws.Length,scissor,1),"submit clipped region draws");}
        catch{Native.Abort(context);throw;}
        Native.Check(Native.End(context),"end");
    }
    internal TextureInfo GetTextureInfo(ulong handle)
    {
        var info = new TextureInfo { Size = (uint)sizeof(TextureInfo) };
        Native.Check(Native.GetTextureInfo(Context, handle, &info), "texture dimensions"); return info;
    }

    internal ulong LoadTexture(string path)
    {
        ulong handle = 0; Native.Check(Native.LoadTexture(Context, path, &handle), "load BMP texture"); return handle;
    }
    internal void ReleaseTexture(ulong handle) => Native.Check(Native.ReleaseTexture(Context, handle), "release texture");
    public uint TextureCount { get { uint count = 0; Native.Check(Native.TextureCount(Context, &count), "texture count"); return count; } }

    internal bool TryPlayTone(out string error)
    {
        if (Native.PlayTone(Context) == 0)
        {
            error = "";
            return true;
        }
        error = Native.Error();
        return false;
    }

    public Stats GetStats()
    {
        var stats = new Stats { Size = (uint)sizeof(Stats) };
        Native.Check(Native.GetStats(Context, &stats), "get stats");
        return stats;
    }

    public void Dispose()
    {
        if (_context == 0)
            return;
        Native.Check(Native.Destroy(Context), "destroy");
        _context = 0;
        _textures?.EngineDestroyed();
        _audio?.EngineDestroyed();_audio=null;
        _physics?.EngineDestroyed();_physics=null;
        _ui?.EngineDestroyed();_ui=null;
    }
}
