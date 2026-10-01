using System.Diagnostics;
using System.Numerics;
namespace GameAuthoringLab;

// Sample policy lives here. The timing API never discovers entities or writes their properties.
internal sealed class AnimationFixture : IDisposable
{
    private readonly TimingScope _scope = new();
    private readonly Entity _game, _real, _once, _gameLamp, _realLamp, _status;
    private bool _gameLit, _realLit;
    public World World { get; } = new();
    public AssetCatalog Catalog { get; }
    public FrameClip Clip { get; } = new(["left", "right", "red", "blue"], .25);
    public FramePlayer GameFrames { get; }
    public FramePlayer RealFrames { get; }
    public FramePlayer OnceFrames { get; }
    public Tween<Vector2> Position { get; }
    public Tween<float> Rotation { get; }
    public Tween<Vector4> Tint { get; }
    public EngineTimer GamePulse { get; }
    public EngineTimer RealPulse { get; }
    public AnimationFixture()
    {
        var source = AuthoredScene.LoadAsset(new AssetRoot(), "regions.scene.json"); Catalog = source.Catalog;
        GameFrames = _scope.Own(new FramePlayer(Clip)); RealFrames = _scope.Own(new FramePlayer(Clip,domain:ClockDomain.RealTime)); OnceFrames = _scope.Own(new FramePlayer(Clip,false));
        Position = _scope.Own(Tween.Vector(new(80,160),new(530,160),2,TweenEase.SmoothStep));
        Rotation = _scope.Own(Tween.Float(0,MathF.Tau,2));
        Tint = _scope.Own(Tween.Color(Vector4.One,new(.3f,1,.5f,.3f),2,TweenEase.EaseIn,ClockDomain.RealTime));
        GamePulse = _scope.Own(new EngineTimer(.5,true)); RealPulse = _scope.Own(new EngineTimer(.5,true,ClockDomain.RealTime));
        _game = Create("Game-clock frame/movement/rotation",80,160,80,80,"left");
        _real = Create("Real-clock frames/tint",80,330,100,100,"left");
        _once = Create("Once, keeps final blue frame",740,160,100,100,"left");
        _gameLamp = Create("Game timer pulse",280,360,40,40,"blue");
        _realLamp = Create("Real timer pulse",380,360,40,40,"blue");
        _status = Create("Pause/cancel status",20,20,24,24,"blue");
        Apply(false);
    }
    private Entity Create(string name, float x, float y, float width, float height, string key)
    { var entity = World.Create(name,transform:new(x,y)); entity.Sprite = new(width,height,AssetKey:key); return entity; }
    public void Advance(TimingStep step, bool paused)
    {
        GameFrames.Advance(step); RealFrames.Advance(step); OnceFrames.Advance(step);
        Position.Advance(step); Rotation.Advance(step); Tint.Advance(step); GamePulse.Advance(step); RealPulse.Advance(step);
        if ((GamePulse.TicksDue & 1) != 0) _gameLit = !_gameLit;
        if ((RealPulse.TicksDue & 1) != 0) _realLit = !_realLit;
        Apply(paused);
    }
    private void Apply(bool paused)
    {
        _game.LocalTransform = new(Position.Value.X,Position.Value.Y,Rotation:Rotation.Value);
        _game.Sprite = _game.Sprite!.Value with { AssetKey = GameFrames.AssetKey };
        Vector4 color = Tint.Value;
        _real.Sprite = _real.Sprite!.Value with { AssetKey = RealFrames.AssetKey, R = color.X, G = color.Y, B = color.Z, A = color.W, FlipX = true };
        _once.Sprite = _once.Sprite!.Value with { AssetKey = OnceFrames.AssetKey };
        _gameLamp.Sprite = _gameLamp.Sprite!.Value with { AssetKey = _gameLit ? "red" : "blue" };
        _realLamp.Sprite = _realLamp.Sprite!.Value with { AssetKey = _realLit ? "red" : "blue" };
        _status.Sprite = _status.Sprite!.Value with { AssetKey = paused || GameFrames.State == PlaybackState.Cancelled ? "red" : "blue" };
    }
    public void Restart()
    { GameFrames.Restart(); RealFrames.Restart(); OnceFrames.Restart(); Position.Restart(); Rotation.Restart(); Tint.Restart(); GamePulse.Restart(); RealPulse.Restart(); _gameLit = _realLit = false; Apply(false); }
    public void Cancel()
    { GameFrames.Cancel(); RealFrames.Cancel(); OnceFrames.Cancel(); Position.Cancel(); Rotation.Cancel(); Tint.Cancel(); GamePulse.Cancel(); RealPulse.Cancel(); Apply(false); }
    public void Dispose() => _scope.Dispose();
}

internal static class AnimationDemo
{
    public static int Run(bool headless, int frames, bool scenario)
    {
        if (scenario && frames == 0) frames = 16;
        using var fixture = new AnimationFixture(); using var engine = new EngineHost(headless,32,legacyTone:false);
        using var bank = new TextureBank(engine,fixture.Catalog,fixture.Clip.AssetKeys);
        var batch = new SpriteBatch(16) { RegionResolver = bank.ResolveRegion };
        var actions = new InputActionMap(InputBinding.Key(1,PhysicalKey.Space),InputBinding.Key(2,PhysicalKey.T),InputBinding.Key(4,PhysicalKey.F),InputBinding.Key(8,PhysicalKey.Escape));
        var stopwatch = Stopwatch.StartNew(); double previous = stopwatch.Elapsed.TotalSeconds; bool paused = false; int rendered = 0;
        string? captures = Environment.GetEnvironmentVariable("GAL_ANIMATION_CAPTURE_DIR");
        if (!headless && captures is not null) Directory.CreateDirectory(captures);
        Console.WriteLine("ANIMATION | top: game clock + one-shot at right | lower sprite: real clock | lamps: game/real timers");
        Console.WriteLine("Space: pause game clock (real clock keeps advancing) | F: cancel all, retaining values | T: restart all | Escape: exit");
        while (frames == 0 || rendered < frames)
        {
            var input = engine.PollInput(); var action = actions.Update(input);
            if (input.Quit != 0 || (action.Pressed & 8) != 0) break;
            // A scripted tick corresponds to one drawable frame, including after restore.
            if (scenario && !headless && !input.Drawable) { Thread.Sleep(1); continue; }
            double now = stopwatch.Elapsed.TotalSeconds; double seconds = scenario ? rendered == 0 ? 0 : .25 : headless ? 1d/60 : Math.Clamp(now-previous,0,.1); previous = now;
            if (scenario)
            {
                paused = rendered is >= 4 and < 8;
                if (rendered == 10) fixture.Cancel();
                if (rendered == 11) fixture.Restart();
            }
            else if (input.Focused && input.Drawable)
            {
                if ((action.Pressed & 1) != 0) paused = !paused;
                if ((action.Pressed & 2) != 0) fixture.Restart();
                if ((action.Pressed & 4) != 0) fixture.Cancel();
            }
            // Focus/minimize freezes both domains in this sample; a game pause only freezes game time.
            if (!headless && !scenario && (!input.Focused || !input.Drawable)) seconds = 0;
            fixture.Advance(TimingStep.FromReal(seconds,paused),paused);
            bank.Sync(fixture.World); fixture.World.ExtractSprites(batch);
            if (input.Drawable || headless)
            {
                if (!headless && captures is not null && rendered is 0 or 1 or 2 or 3 or 7 or 9 or 10 or 11)
                    Native.Check(UiNative.Capture(engine.NativeContext,Path.GetFullPath(Path.Combine(captures,$"frame-{rendered:D2}.bmp"))),"animation capture");
                engine.Draw(new Camera { Zoom = 1 },batch.RegionDraws); rendered++;
            }
            if (!scenario && !headless) Thread.Sleep(1);
        }
        int uploads = engine.Textures.Loads; int loads = bank.Loads; bank.Dispose(); fixture.Dispose();
        if (engine.Textures.Count != 0 || engine.TextureCount != 0) throw new InvalidOperationException("Animation resources were not released.");
        Console.WriteLine($"ANIMATION DONE frames={rendered} key_leases={loads} texture_cache_loads={uploads} gpu_uploads={(headless ? 0 : uploads)} textures=0 game={fixture.GameFrames.FrameIndex} real={fixture.RealFrames.FrameIndex}"); return 0;
    }
}
