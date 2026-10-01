using System.Numerics;
namespace GameAuthoringLab;

internal static class AnimationTests
{
    private sealed class Idle : IBehavior { public void Update(Entity entity, float seconds) { } }
    public static int Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("ANIMATION: " + name); count++; }
        void Reject<T>(Action action, string name) where T : Exception
        { try { action(); } catch (T) { count++; return; } throw new InvalidOperationException("ANIMATION accepted: " + name); }
        static TimingStep Step(double seconds) => TimingStep.FromReal(seconds);
        foreach (double bad in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 86401d })
        {
            Reject<ArgumentOutOfRangeException>(() => Step(bad), "invalid real delta");
            Reject<ArgumentOutOfRangeException>(() => new TimingStep(0, bad), "invalid game delta");
            Reject<ArgumentOutOfRangeException>(() => Tween.Float(0, 1, bad), "invalid tween duration");
            Reject<ArgumentOutOfRangeException>(() => new EngineTimer(bad), "invalid timer duration");
        }
        foreach (double bad in new[] { -1d, double.NaN, double.PositiveInfinity, 17d })
            Reject<ArgumentOutOfRangeException>(() => TimingStep.FromReal(1, true, bad), "time scale validated even when paused");
        Reject<ArgumentOutOfRangeException>(() => TimingStep.FromReal(86400, timeScale:2), "scaled delta rejected rather than clamped");
        Check(TimingStep.FromReal(4, true, 2) == new TimingStep(4, 0), "game pause preserves real delta");
        Check(TimingStep.FromReal(4, timeScale:.5) == new TimingStep(4, 2), "scaled game delta");
        Check(TimingStep.FromReal(4, timeScale:0) == new TimingStep(4, 0), "zero scale freezes game");
        Reject<ArgumentOutOfRangeException>(() => new EngineTimer(1, domain:(ClockDomain)42), "invalid clock domain");
        Reject<ArgumentOutOfRangeException>(() => new EngineTimer(.0000001), "minimum positive duration");
        Reject<ArgumentOutOfRangeException>(() => new EngineTimer(0, true), "zero repeat interval");
        Reject<ArgumentOutOfRangeException>(() => new FrameClip([], .25), "empty clip");
        Reject<ArgumentOutOfRangeException>(() => new FrameClip(new string[4097], .25), "clip frame cap before allocation");
        Reject<ArgumentException>(() => new FrameClip(["left", " "], .25), "empty key");
        Reject<ArgumentException>(() => new FrameClip([null!], .25), "null key");
        Reject<ArgumentOutOfRangeException>(() => new FrameClip(["left"], 0), "zero frame duration");
        Reject<ArgumentOutOfRangeException>(() => new FrameClip(["left", "right"], 86400), "bounded total duration");
        string[] keys = ["left", "right", "red", "blue"];
        var clip = new FrameClip(keys, .125); keys[0] = "changed";
        Check(clip.AssetKeys[0] == "left" && clip.DurationSeconds == .5, "copied immutable clip metadata");
        var player = new FramePlayer(clip);
        Check(player.FrameIndex == 0 && player.AssetKey == "left", "initial frame can be polled before advance");
        player.Advance(default); Check(player.FrameIndex == 0, "zero delta does nothing");
        player.Advance(Step(.125)); Check(player.FrameIndex == 1, "exact frame boundary");
        player.Advance(Step(.375)); Check(player.FrameIndex == 0 && player.ElapsedSeconds == 0, "exact cycle wraps");
        player.Advance(Step(86400)); Check(player.FrameIndex == 0 && player.State == PlaybackState.Running, "one-day delta does not loop through frames");
        player.Advance(Step(.375)); Check(player.FrameIndex == 3, "last looping frame");
        player.Pause(); player.Advance(Step(1)); Check(player.FrameIndex == 3 && player.State == PlaybackState.Paused, "local player pause");
        player.Resume(); player.Advance(Step(.125)); Check(player.FrameIndex == 0, "resume retains phase");
        var once = new FramePlayer(clip, false); once.Advance(Step(5));
        Check(once.FrameIndex == 3 && once.State == PlaybackState.Completed && once.CompletedThisAdvance, "once clamps final frame and completion edge");
        once.Advance(Step(1)); Check(!once.CompletedThisAdvance && once.FrameIndex == 3, "completed output retained without repeated completion");
        once.Restart(); Check(once.State == PlaybackState.Running && once.FrameIndex == 0, "completed player restart");
        once.Advance(Step(.25)); once.Cancel(); once.Advance(Step(1)); Check(once.FrameIndex == 2 && once.State == PlaybackState.Cancelled, "cancel preserves current frame");
        once.Restart(); Check(once.FrameIndex == 0, "cancelled player restart");
        var single = new FramePlayer(new(["left"], .25), false); single.Advance(Step(.25));
        Check(single.FrameIndex == 0 && single.CompletedThisAdvance, "single-frame once");
        var singleLoop = new FramePlayer(single.Clip); singleLoop.Advance(Step(86400));
        Check(singleLoop.FrameIndex == 0 && singleLoop.State == PlaybackState.Running, "single-frame loop");
        var gamePlayer = new FramePlayer(clip); var realPlayer = new FramePlayer(clip, domain:ClockDomain.RealTime);
        var pauseStep = TimingStep.FromReal(.125, true); gamePlayer.Advance(pauseStep); realPlayer.Advance(pauseStep);
        Check(gamePlayer.FrameIndex == 0 && realPlayer.FrameIndex == 1, "independent clock domains");

        var tween = Tween.Float(-10, 10, 1); Check(tween.Value == -10 && tween.Progress == 0, "tween initial value");
        tween.Advance(Step(.25)); Check(tween.Value == -5 && tween.Progress == .25, "numeric quarter");
        tween.Advance(Step(.25)); Check(tween.Value == 0, "numeric midpoint");
        tween.Advance(Step(86400)); Check(tween.Value == 10 && tween.Progress == 1 && tween.CompletedThisAdvance, "exact endpoint on large delta");
        tween.Advance(default); Check(!tween.CompletedThisAdvance, "zero advance clears completion edge");
        tween.Restart(); Check(tween.Value == -10, "restart restores from value");
        tween.Advance(Step(.25)); tween.Cancel(); tween.Advance(Step(1)); Check(tween.Value == -5 && tween.State == PlaybackState.Cancelled, "cancel does not snap to endpoint");
        foreach (var pair in new[] { (TweenEase.Linear, .25f), (TweenEase.EaseIn, .0625f), (TweenEase.EaseOut, .4375f), (TweenEase.SmoothStep, .15625f) })
        { var eased = Tween.Float(0, 1, 1, pair.Item1); eased.Advance(Step(.25)); Check(eased.Value == pair.Item2, "easing quarter"); }
        Reject<ArgumentOutOfRangeException>(() => Tween.Float(0, 1, 1, (TweenEase)8), "unknown easing");
        Reject<ArgumentOutOfRangeException>(() => Tween.Float(float.NaN, 1, 1), "nonfinite numeric endpoint");
        Reject<ArgumentOutOfRangeException>(() => Tween.Vector(new(0,float.PositiveInfinity), Vector2.Zero, 1), "nonfinite vector endpoint");
        Reject<ArgumentOutOfRangeException>(() => Tween.Color(Vector4.Zero, new(1,1,1,1.1f), 1), "out of range RGBA");
        Reject<ArgumentOutOfRangeException>(() => Tween.Color(new(0,0,float.NaN,0), Vector4.One, 1), "nonfinite RGBA");
        var extreme = Tween.Float(float.MaxValue, -float.MaxValue, 1); extreme.Advance(Step(.5)); Check(extreme.Value == 0, "double interpolation avoids float subtraction overflow");
        var vector = Tween.Vector(new(1,3), new(5,7), 1); vector.Advance(Step(.5)); Check(vector.Value == new Vector2(3,5), "typed Vector2");
        var color = Tween.Color(new(1,0,0,0), new(0,1,1,1), 1); color.Advance(Step(.5)); Check(color.Value == new Vector4(.5f), "straight RGBA channels including alpha");
        var immediate = Tween.Float(2,3,0); immediate.Advance(default); Check(immediate.Value == 2 && immediate.State == PlaybackState.Running, "zero duration defers until positive delta");
        immediate.Advance(TimingStep.FromReal(1,true)); Check(immediate.Value == 2, "zero duration respects paused game time");
        immediate.Advance(Step(.1)); Check(immediate.Value == 3 && immediate.CompletedThisAdvance, "zero tween completes once");
        var localPause = Tween.Float(0,1,1,domain:ClockDomain.RealTime); localPause.Advance(Step(.25)); localPause.Pause(); localPause.Advance(Step(1));
        Check(localPause.Value == .25f, "local pause also freezes real-time tween"); localPause.Resume(); localPause.Advance(Step(.25)); Check(localPause.Value == .5f, "resumed tween phase");

        var timer = new EngineTimer(.25, true); timer.Advance(Step(.125)); Check(timer.TicksDue == 0 && timer.RemainingSeconds == .125, "timer before deadline");
        timer.Advance(Step(.125)); Check(timer.TicksDue == 1 && timer.RemainingSeconds == .25, "repeat exact boundary resets remaining");
        timer.Advance(Step(1.125)); Check(timer.TicksDue == 4 && timer.RemainingSeconds == .125, "coalesced ticks retain remainder");
        timer.Advance(default); Check(timer.TicksDue == 0 && timer.RemainingSeconds == .125, "zero step clears polled ticks without changing phase");
        var many = new EngineTimer(.000001,true); many.Advance(Step(86400)); Check(many.TicksDue == 86_400_000_000, "bounded arithmetic handles billions of due ticks");
        var oneTimer = new EngineTimer(.5); oneTimer.Advance(Step(1)); Check(oneTimer.TicksDue == 1 && oneTimer.RemainingSeconds == 0 && oneTimer.CompletedThisAdvance, "oneshot deadline");
        oneTimer.Advance(Step(1)); Check(oneTimer.TicksDue == 0 && !oneTimer.CompletedThisAdvance, "no repeated oneshot tick");
        oneTimer.Restart(); Check(oneTimer.RemainingSeconds == .5, "timer restart");
        var zeroTimer = new EngineTimer(0); zeroTimer.Advance(default); Check(zeroTimer.TicksDue == 0, "zero timer deferred");
        zeroTimer.Advance(Step(1)); Check(zeroTimer.TicksDue == 1 && zeroTimer.State == PlaybackState.Completed, "zero timer once");
        var realTimer = new EngineTimer(.25,true,ClockDomain.RealTime); var gameTimer = new EngineTimer(.25,true);
        realTimer.Advance(TimingStep.FromReal(1,true)); gameTimer.Advance(TimingStep.FromReal(1,true));
        Check(realTimer.TicksDue == 4 && gameTimer.TicksDue == 0, "timer domain pause semantics");
        gameTimer.Advance(TimingStep.FromReal(1,timeScale:.5)); Check(gameTimer.TicksDue == 2, "timer scaled time");
        gameTimer.Cancel(); gameTimer.Advance(Step(1)); Check(gameTimer.TicksDue == 0, "timer cancellation silent");
        // Binary-exact steps establish reproducibility, not a cross-platform floating-point promise.
        var split = new FramePlayer(clip); var lump = new FramePlayer(clip); var splitTimer = new EngineTimer(.125,true); var lumpTimer = new EngineTimer(.125,true); ulong ticks = 0;
        for (int i=0;i<29;i++) { split.Advance(Step(.0625)); splitTimer.Advance(Step(.0625)); ticks += splitTimer.TicksDue; }
        lump.Advance(Step(29*.0625)); lumpTimer.Advance(Step(29*.0625));
        Check(split.FrameIndex == lump.FrameIndex && split.ElapsedSeconds == lump.ElapsedSeconds && ticks == lumpTimer.TicksDue && splitTimer.RemainingSeconds == lumpTimer.RemainingSeconds, "partitioned exact deltas agree");

        var decimalTimer = new EngineTimer(.1,true); decimalTimer.Advance(Step(.3));
        Check(decimalTimer.TicksDue == 2 && decimalTimer.RemainingSeconds > 0 && decimalTimer.RemainingSeconds < 1e-15, "documented IEEE decimal boundary remains explicit");
        decimalTimer.Advance(Step(.000001)); Check(decimalTimer.TicksDue == 1, "deferred decimal-boundary tick catches up");
        Reject<ArgumentOutOfRangeException>(() => new TimingScope(0), "zero scope capacity");
        Reject<ArgumentOutOfRangeException>(() => new TimingScope(257), "scope capacity upper bound");
        using var scope = new TimingScope(2); var owned = scope.Own(Tween.Float(0,1,1)); var ownedTimer = scope.Own(new EngineTimer(1));
        Reject<InvalidOperationException>(() => scope.Own(owned), "duplicate ownership");
        using var otherScope = new TimingScope(); Reject<InvalidOperationException>(() => otherScope.Own(owned), "foreign ownership");
        using var extra = new EngineTimer(1); Reject<InvalidOperationException>(() => scope.Own(extra), "bounded scope");
        owned.Dispose(); scope.Own(extra); Check(extra.Owner == scope, "disposed slot reclaimed on addition");
        scope.Dispose(); scope.Dispose(); Check(ownedTimer.IsDisposed && extra.IsDisposed && extra.State == PlaybackState.Cancelled, "scope disposal cancels live work once");
        Reject<ObjectDisposedException>(() => extra.Advance(default), "advance after disposal");
        Reject<ObjectDisposedException>(() => extra.Restart(), "restart after disposal");
        using var orphan = new EngineTimer(1); Reject<ObjectDisposedException>(() => scope.Own(orphan), "add after scope disposal");
        once.Dispose(); once.Dispose(); Check(once.AssetKey == "left" && once.State == PlaybackState.Cancelled, "dispose retains inspectable value");
        tween.Restart(); tween.Advance(Step(1)); tween.Dispose(); Check(tween.State == PlaybackState.Completed && tween.Value == 10, "completed disposal retains completed state");
        Exception? wrongThread = null; var thread = new Thread(() => { try { orphan.Advance(Step(1)); } catch(Exception e) { wrongThread = e; } }); thread.Start(); thread.Join();
        Check(wrongThread is InvalidOperationException && orphan.RemainingSeconds == 1, "wrong thread rejects before mutation");
        wrongThread = null; thread = new Thread(() => { try { otherScope.Dispose(); } catch(Exception e) { wrongThread = e; } }); thread.Start(); thread.Join();
        Check(wrongThread is InvalidOperationException, "wrong thread scope disposal rejected");

        var world = new World(); var persistent = world.Create("persistent"); var persistentScope = new TimingScope(); var persistentTimer = persistentScope.Own(new EngineTimer(10));
        world.AttachBehavior(persistent,new Idle(),life=>life.OnDetach(persistentScope.Dispose));
        for(int i=0;i<3;i++)
        {
            var scene = world.CreateScene("timing"); var entity = world.Create("animated",scene); var lifetime = new TimingScope();
            world.AttachBehavior(entity,new Idle(),life=>life.OnDetach(lifetime.Dispose));
            var scenePlayer = lifetime.Own(new FramePlayer(clip)); scenePlayer.Advance(Step(.25));
            world.UnloadScene(scene); Check(scenePlayer.IsDisposed && !persistentTimer.IsDisposed, "scene unload cancels only owned animation");
        }
        var replacement = new TimingScope(); var replacementTween = replacement.Own(Tween.Float(0,1,1));
        world.AttachBehavior(persistent,new Idle(),life=>life.OnDetach(replacement.Dispose));
        Check(persistentTimer.IsDisposed && !replacementTween.IsDisposed, "behavior replacement disposes old scope");
        var candidateScope = new TimingScope(); var candidateTimer = candidateScope.Own(new EngineTimer(1));
        Reject<AggregateException>(()=>world.AttachBehavior(persistent,new Idle(),life=>{life.OnDetach(candidateScope.Dispose);throw new InvalidOperationException("setup");}),"failed attachment");
        Check(candidateTimer.IsDisposed && !replacementTween.IsDisposed, "failed attach cancels candidate only");
        world.Destroy(persistent); Check(replacementTween.IsDisposed, "destroyed entity needs no property access during cleanup");

        using (var fixture = new AnimationFixture())
        {
            fixture.Advance(Step(.75),false); var position = fixture.Position.Value; var frame = fixture.GameFrames.FrameIndex; var tint = fixture.Tint.Value;
            fixture.Advance(TimingStep.FromReal(1,true),true);
            Check(fixture.Position.Value == position && fixture.GameFrames.FrameIndex == frame && fixture.Tint.Value != tint, "fixture game pause keeps real clock live");
            fixture.Advance(Step(.5),false); position = fixture.Position.Value; tint = fixture.Tint.Value; fixture.Cancel(); fixture.Advance(Step(1),false);
            Check(fixture.Position.Value == position && fixture.Tint.Value == tint, "fixture cancellation preserves all visible values");
            fixture.Restart(); Check(fixture.GameFrames.FrameIndex == 0 && fixture.Position.Value == new Vector2(80,160) && fixture.Tint.Value == Vector4.One, "fixture restart resets all channels");
        }
        var assets = AuthoredScene.LoadAsset(new AssetRoot(),"regions.scene.json");
        using var engine = new EngineHost(true,16); using var bank = new TextureBank(engine,assets.Catalog,clip.AssetKeys);
        var renderWorld = new World(); var actor = renderWorld.Create("animated"); actor.Sprite = new(16,16,AssetKey:"left");
        bank.Sync(renderWorld); Check(bank.LoadedCount == 4 && engine.Textures.Count == 1 && engine.Textures.Loads == 1, "all clip keys retained with one shared upload");
        Reject<AssetException>(() => new TextureBank(engine, assets.Catalog, ["absent"]), "retained keys validated at setup");
        string[] retained = ["left", "right"];
        using (var copiedBank = new TextureBank(engine,assets.Catalog,retained))
        {
            retained[0] = "absent"; copiedBank.Sync(new World()); Check(copiedBank.LoadedCount == 2, "bank copies retained keys independently of caller mutation");
        }
        var badCatalog = new AssetCatalog(assets.Catalog.Assets,new Dictionary<string,TextureAsset> { ["good"] = new("regions.bmp",new(0,0,1,1)), ["bad"] = new("regions.bmp",new(15,0,2,1)) });
        using (var badBank = new TextureBank(engine,badCatalog,["good","bad"]))
        {
            Reject<AssetException>(() => badBank.Sync(new World()), "retained region validated before lease commit");
            Check(badBank.LoadedCount == 0 && engine.Textures.Count == 1, "failed retained-key sync preserves other bank resources");
        }
        var batch = new SpriteBatch(8) { RegionResolver = bank.ResolveRegion }; var allocationPlayer = new FramePlayer(clip);
        var allocationTween = Tween.Vector(Vector2.Zero,Vector2.One,1); var allocationTimer = new EngineTimer(.125,true);
        void Tick()
        {
            allocationPlayer.Advance(Step(.0625)); allocationTween.Advance(Step(.0625)); if(allocationTween.State == PlaybackState.Completed) allocationTween.Restart(); allocationTimer.Advance(Step(.0625));
            actor.Sprite = actor.Sprite!.Value with { AssetKey = allocationPlayer.AssetKey }; bank.Sync(renderWorld); renderWorld.ExtractSprites(batch);
        }
        for(int i=0;i<128;i++)Tick(); long before = GC.GetAllocatedBytesForCurrentThread(); for(int i=0;i<1000;i++)Tick();
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "warmed playback, tween, timer and retained-key Sync/extraction allocate zero");
        Check(bank.Loads == 4 && bank.Releases == 0 && engine.Textures.Loads == 1, "frame swaps do not churn leases or native uploads");
        renderWorld.Destroy(actor); bank.Sync(renderWorld); Check(bank.LoadedCount == 4, "retained keys last until bank disposal");
        bank.Dispose(); Check(engine.Textures.Count == 0, "retained resources released");
        Console.WriteLine($"ANIMATION SELF-TEST PASS assertions={count}"); return count;
    }
}
