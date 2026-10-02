namespace GameAuthoringLab;

internal static class FrameEventTests
{
    public static int Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("FRAME EVENTS: " + name); count++; }
        void Reject<T>(Action action, string name) where T : Exception
        { try { action(); } catch (T) { count++; return; } throw new InvalidOperationException("FRAME EVENTS accepted: " + name); }
        void Events(FramePlayer player, ReadOnlySpan<FrameMarker> expected, ulong due, string name)
        { Check(player.Events.SequenceEqual(expected) && player.EventsDue == due && player.EventsDropped == due - (ulong)expected.Length, name); }
        static TimingStep Step(double seconds) => TimingStep.FromReal(seconds);

        FrameMarker[] authored = [new(2, 40), new(1, 20), new(0, 10), new(1, 30)];
        var clip = new FrameClip(["a", "b", "c"], .125, authored);
        authored[2] = new(2, 99);
        Check(clip.Markers.SequenceEqual([new(0, 10), new(1, 20), new(1, 30), new(2, 40)]), "copied stable frame ordering");
        var ids = new FrameClip(["a"], .125, [new(0, int.MinValue), new(0, 0), new(0, int.MaxValue)]);
        Check(ids.Markers.Length == 3, "event identifiers are opaque integers");
        Reject<ArgumentOutOfRangeException>(() => new FrameClip(["a"], .125, [new(-1, 1)]), "negative marker frame");
        Reject<ArgumentOutOfRangeException>(() => new FrameClip(["a"], .125, [new(1, 1)]), "marker beyond last frame");
        Reject<ArgumentOutOfRangeException>(() => new FrameClip(["a"], .125, new FrameMarker[FrameClip.MaximumMarkers + 1]), "marker capacity");
        Reject<ArgumentOutOfRangeException>(() => new FramePlayer(clip, -1), "negative event capacity");
        Reject<ArgumentOutOfRangeException>(() => new FramePlayer(clip, FramePlayer.MaximumEventCapacity + 1), "event capacity bound");
        Reject<ArgumentNullException>(() => new FramePlayer(null!, 1), "null clip");
        Reject<ArgumentOutOfRangeException>(() => new FramePlayer(clip, 1, domain: (ClockDomain)17), "event player domain");

        var player = new FramePlayer(clip);
        Check(player.EventCapacity == FramePlayer.DefaultEventCapacity, "default fixed event capacity");
        Events(player, [], 0, "construction silent");
        player.Advance(default); Events(player, [], 0, "zero first step silent");
        player.Pause(); player.Advance(Step(.5)); Events(player, [], 0, "paused first step silent");
        player.Resume(); player.Advance(TimingStep.FromReal(.5, paused: true)); Events(player, [], 0, "game-clock pause defers initial entry");
        player.Advance(Step(.0625)); Events(player, [new(0, 10)], 1, "first positive entry");
        player.Pause(); Events(player, [new(0, 10)], 1, "Pause preserves last results");
        player.Resume(); Events(player, [new(0, 10)], 1, "Resume preserves last results");
        player.Advance(Step(.0625)); Events(player, [new(1, 20), new(1, 30)], 2, "exact boundary authored order");
        player.Advance(Step(.0625)); Events(player, [], 0, "same frame does not re-emit");
        player.Advance(Step(.1875)); Events(player, [new(2, 40), new(0, 10)], 2, "exact wrap includes first frame once");
        Check(player.FrameIndex == 0 && player.ElapsedSeconds == 0, "events preserve wrapped phase");
        player.Advance(Step(.875));
        Events(player, [new(1, 20), new(1, 30), new(2, 40), new(0, 10), new(1, 20), new(1, 30), new(2, 40), new(0, 10), new(1, 20), new(1, 30)], 10, "several complete cycles then partial head");
        player.Advance(Step(.5));
        Events(player, [new(2, 40), new(0, 10), new(1, 20), new(1, 30), new(2, 40)], 5, "partial tail followed by wrap and partial head");
        player.Restart(); Events(player, [], 0, "restart clears results");
        player.Advance(Step(.5)); Events(player, [new(0, 10), new(1, 20), new(1, 30), new(2, 40), new(0, 10), new(1, 20), new(1, 30)], 7, "initial plus skipped markers");
        player.Advance(default); Events(player, [], 0, "zero advance clears results");
        player.Pause(); player.Advance(Step(1)); Events(player, [], 0, "paused advance clears results");
        player.Restart(); player.Advance(Step(.125)); player.Cancel(); Events(player, [], 0, "cancel clears results");
        player.Advance(Step(1)); Events(player, [], 0, "cancelled advance silent");
        var tinyStep = new FramePlayer(clip); tinyStep.Advance(Step(double.Epsilon));
        Events(tinyStep, [new(0, 10)], 1, "any positive selected time consumes initial entry");

        var overflow = new FramePlayer(clip, 4);
        overflow.Advance(Step(.5)); Events(overflow, [new(0, 10), new(1, 20), new(1, 30), new(2, 40)], 7, "overflow retains earliest events");
        Check(overflow.FrameIndex == 1 && overflow.ElapsedSeconds == .125, "overflow still advances full delta");
        overflow.Advance(Step(.125)); Events(overflow, [new(2, 40)], 1, "dropped events never retried");
        var oneSlot = new FramePlayer(clip, 1); oneSlot.Advance(Step(.125)); Events(oneSlot, [new(0, 10)], 3, "overflow can cut within same-frame group");
        var countsOnly = new FramePlayer(clip, 0); countsOnly.Advance(Step(.5)); Events(countsOnly, [], 7, "zero capacity counts without materialization");
        Check(countsOnly.EventCapacity == 0 && countsOnly.FrameIndex == 1, "counts-only advances normally");

        var once = new FramePlayer(clip, loop: false); once.Advance(Step(86400));
        Events(once, clip.Markers, 4, "one-shot large step enters each frame once");
        Check(once.FrameIndex == 2 && once.ElapsedSeconds == clip.DurationSeconds && once.CompletedThisAdvance, "one-shot completion clamps final frame");
        once.Advance(Step(1)); Events(once, [], 0, "completed advance clears events");
        Check(!once.CompletedThisAdvance, "completion edge not repeated");
        once.Restart(); once.Advance(Step(.25)); Events(once, clip.Markers, 4, "entry into final frame before completion");
        once.Advance(Step(.125)); Events(once, [], 0, "one-shot end is not a new frame entry");
        Check(once.CompletedThisAdvance, "completion without a marker remains observable");
        var singleClip = new FrameClip(["a"], .125, [new(0, 1)]);
        var single = new FramePlayer(singleClip, loop: false); single.Advance(Step(.125));
        Events(single, [new(0, 1)], 1, "single-frame once no phantom wrap");
        var singleLoop = new FramePlayer(singleClip); singleLoop.Advance(Step(.25));
        Events(singleLoop, [new(0, 1), new(0, 1), new(0, 1)], 3, "single-frame loop marks every entry");
        var sparse = new FramePlayer(new FrameClip(["a", "b", "c"], .125, [new(2, 2)]));
        sparse.Advance(Step(.125)); Events(sparse, [], 0, "empty initial and intermediate frames");
        sparse.Advance(Step(.5)); Events(sparse, [new(2, 2), new(2, 2)], 2, "sparse repeated cycles");
        var unmarked = new FramePlayer(new FrameClip(["a"], .000001), 0);
        unmarked.Advance(Step(86400)); Events(unmarked, [], 0, "huge unmarked advance is bounded");
        var tiny = new FramePlayer(new FrameClip(["a"], .000001, [new(0, 1)]), 2);
        tiny.Advance(Step(86400)); Events(tiny, [new(0, 1), new(0, 1)], 86_400_000_001, "billions of loops counted arithmetically");
        tiny.Advance(default); Events(tiny, [], 0, "huge advance counts are transient");
        var maximumMarkers = new FrameMarker[FrameClip.MaximumMarkers];
        for (int i = 0; i < maximumMarkers.Length; i++) maximumMarkers[i] = new(0, i);
        var maximal = new FramePlayer(new FrameClip(["a"], .000001, maximumMarkers), FramePlayer.MaximumEventCapacity);
        maximal.Advance(Step(86400)); Events(maximal, maximumMarkers.AsSpan(0, FramePlayer.MaximumEventCapacity), 353_894_400_004_096, "maximum legal event count fits ulong and bounded storage");

        var real = new FramePlayer(clip, domain: ClockDomain.RealTime); real.Advance(TimingStep.FromReal(.125, paused: true));
        Events(real, [new(0, 10), new(1, 20), new(1, 30)], 3, "real clock still enters frames during game pause");
        var scaled = new FramePlayer(clip); scaled.Advance(TimingStep.FromReal(.25, timeScale: .5));
        Events(scaled, real.Events, 3, "scaled game time controls crossings");
        scaled.Advance(TimingStep.FromReal(10, timeScale: 0)); Events(scaled, [], 0, "zero time scale clears events");
        var supplied = new FramePlayer(clip); supplied.Advance(new TimingStep(0, .125));
        Events(supplied, real.Events, 3, "explicit independent selected clock");
        var decimalLoop = new FramePlayer(new FrameClip(["a"], .1, [new(0, 7)]));
        decimalLoop.Advance(Step(.3)); Events(decimalLoop, [new(0, 7), new(0, 7), new(0, 7)], 3, "decimal modulo boundary has no epsilon");
        Check(decimalLoop.ElapsedSeconds == .3 % .1, "existing modulo phase preserved");
        decimalLoop.Advance(Step(.000001)); Events(decimalLoop, [new(0, 7)], 1, "deferred decimal wrap catches up");
        decimalLoop.Restart(); decimalLoop.Advance(Step(1));
        Check(decimalLoop.EventsDue == 10 && decimalLoop.ElapsedSeconds == 1 % .1, "quotient count agrees with near-full modulo remainder");
        double oldDecimalPhase = decimalLoop.ElapsedSeconds; decimalLoop.Advance(Step(86400));
        Check(decimalLoop.EventsDue == 864_000 && decimalLoop.Events.Length == FramePlayer.DefaultEventCapacity && decimalLoop.EventsDropped == 864_000 - FramePlayer.DefaultEventCapacity && decimalLoop.ElapsedSeconds == (oldDecimalPhase + 86400 % .1) % .1, "large decimal step carries near-full phase and keeps cycle counts aligned");
        var split = new FramePlayer(clip); var lump = new FramePlayer(clip); var splitEvents = new List<FrameMarker>();
        for (int i = 0; i < 29; i++) { split.Advance(Step(.0625)); foreach (FrameMarker marker in split.Events) splitEvents.Add(marker); }
        lump.Advance(Step(29 * .0625));
        Check(splitEvents.SequenceEqual(lump.Events.ToArray()) && split.ElapsedSeconds == lump.ElapsedSeconds && split.FrameIndex == lump.FrameIndex, "binary-exact split and lumped event streams agree");

        var switching = new FramePlayer(clip, 8); switching.Advance(Step(.125));
        switching.Play(clip); Events(switching, [new(0, 10), new(1, 20), new(1, 30)], 3, "same-clip Play preserves latest results");
        Check(switching.FrameIndex == 1 && switching.ElapsedSeconds == .125 && switching.State == PlaybackState.Running, "repeated Play keeps phase");
        switching.Advance(Step(.0625)); Events(switching, [], 0, "repeated Play does not owe another initial entry");
        switching.Pause(); switching.Play(clip, loop: false);
        Check(switching.State == PlaybackState.Paused && !switching.Loop && switching.ElapsedSeconds == .1875, "same-clip Play preserves pause while changing loop policy");
        switching.Resume(); switching.Advance(Step(1));
        Check(switching.CompletedThisAdvance && switching.State == PlaybackState.Completed, "changed loop policy completes once");
        switching.Play(clip);
        Check(switching.State == PlaybackState.Completed && switching.CompletedThisAdvance && switching.ElapsedSeconds == clip.DurationSeconds && switching.Loop, "completed same-clip Play does not restart or reinterpret endpoint");
        switching.Advance(Step(1)); Events(switching, [], 0, "completed once-to-loop stays complete until restart");
        switching.Play(clip, restart: true); Events(switching, [], 0, "explicit Play restart clears results");
        Check(switching.State == PlaybackState.Running && switching.FrameIndex == 0 && !switching.CompletedThisAdvance, "explicit Play restart runs from zero");
        switching.Advance(Step(.0625)); Events(switching, [new(0, 10)], 1, "Play restart owes initial entry");
        FrameMarker copied = switching.Events[0];
        var other = new FrameClip(["x", "y"], .25, [new(0, 80), new(1, 90)]);
        switching.Pause(); switching.Play(other, loop: false); Events(switching, [], 0, "switch clears old events immediately");
        Check(ReferenceEquals(switching.Clip, other) && switching.AssetKey == "x" && switching.State == PlaybackState.Running && !switching.Loop && switching.EventCapacity == 8, "switch runs new clip with same player storage");
        switching.Advance(default); Events(switching, [], 0, "switch waits for positive time");
        switching.Advance(Step(.25)); Events(switching, [new(0, 80), new(1, 90)], 2, "switch emits only new clip entries");
        switching.Cancel(); switching.Play(other);
        Check(switching.State == PlaybackState.Cancelled && switching.FrameIndex == 1, "same cancelled clip remains cancelled");
        var equivalent = new FrameClip(["x", "y"], .25, other.Markers);
        switching.Play(equivalent); Check(switching.State == PlaybackState.Running && switching.FrameIndex == 0, "different clip identity resets even when metadata is equal");
        switching.Advance(Step(.125)); Events(switching, [new(0, 80)], 1, "equal replacement has fresh initial entry");
        Reject<ArgumentNullException>(() => switching.Play(null!, loop: false, restart: true), "null Play");
        Events(switching, [new(0, 80)], 1, "rejected Play preserves results");
        Check(ReferenceEquals(switching.Clip, equivalent) && switching.Loop && switching.ElapsedSeconds == .125, "rejected Play preserves all state");
        Exception? wrongThread = null;
        var thread = new Thread(() => { try { switching.Play(other, restart: true); } catch (Exception error) { wrongThread = error; } });
        thread.Start(); thread.Join();
        Check(wrongThread is InvalidOperationException && ReferenceEquals(switching.Clip, equivalent) && switching.ElapsedSeconds == .125, "wrong-thread Play rejects before mutation");
        switching.Dispose(); Events(switching, [], 0, "dispose clears events");
        Reject<ObjectDisposedException>(() => switching.Play(clip), "Play after dispose");
        Check(copied == new FrameMarker(0, 10) && switching.AssetKey == "x", "copied event and last frame outlive reset and disposal");
        using (var scope = new TimingScope())
        {
            var owned = scope.Own(new FramePlayer(clip)); owned.Advance(Step(.125)); owned.Play(other); scope.Dispose();
            Check(owned.IsDisposed && ReferenceEquals(owned.Clip, other) && owned.Events.IsEmpty, "switching keeps timing-scope lifetime");
        }

        // Include overflow, switches, repeated Play, borrowed reads and game/real clocks.
        var warmed = new FramePlayer(clip, 3); var warmedReal = new FramePlayer(other, 2, domain: ClockDomain.RealTime);
        long sum = 0;
        void Tick(int index)
        {
            FrameClip active = (index & 1) == 0 ? clip : other;
            warmed.Play(active); warmed.Play(active); warmed.Advance(Step(86400));
            warmedReal.Play(other); warmedReal.Advance(TimingStep.FromReal(.125, paused: true));
            foreach (FrameMarker marker in warmed.Events) sum += marker.EventId;
            foreach (FrameMarker marker in warmedReal.Events) sum += marker.EventId;
            sum += (long)warmed.EventsDropped;
        }
        for (int i = 0; i < 256; i++) Tick(i);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) Tick(i);
        Check(GC.GetAllocatedBytesForCurrentThread() == before && sum > 0, "warmed marked playback, overflow, reads and Play allocate zero");
        Console.WriteLine($"FRAME EVENT SELF-TEST PASS assertions={count}"); return count;
    }
}
