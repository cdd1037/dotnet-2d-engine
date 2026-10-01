namespace GameAuthoringLab;

// Dependency-free integration tests intentionally exercise the raw ABI, including
// paths rejected by the safer EngineHost wrapper. Always runs headless.
internal static unsafe class SelfTests
{
    private static int _assertions;

    public static int Run()
    {
        _assertions = 0;
        LayoutsAndOptions();
        InvalidCreation();
        LifecycleAndBatchState();
        RecreateAndManagedOwnership();
        FrameAllocations();
        _assertions += WorldSelfTests.Run();
        _assertions += WorldPersistenceTests.Run();
        _assertions += RoomGameTests.Run();
        _assertions += MissionTests.Run();
        _assertions += GameUiTests.RunAuthoring();
        _assertions += GameplayLifecycleTests.Run();
        _assertions += LifecycleOwnershipTests.Run();
        _assertions += SpriteSortTests.Run();
        _assertions += AuthoredSceneTests.Run();
        _assertions += ResourceTests.Run();
        _assertions += InputTests.Run();
        UiAuthoringTests.Run(Assert);
        Console.WriteLine($"SELF-TEST PASS assertions={_assertions} (headless ABI validation; no graphics/audio device exercised)");
        return 0;
    }

    private static void LayoutsAndOptions()
    {
        Assert(sizeof(Config) == 24, "config layout");
        Assert(sizeof(Camera) == 12, "camera layout");
        Assert(sizeof(Sprite) == 32, "sprite layout");
        Assert(sizeof(SpriteDraw) == 56, "affine draw layout");
        Assert(sizeof(Input) == 32, "input layout");
        Assert(sizeof(Stats) == 20, "stats layout");
        Config config = default;
        Input input = default;
        Assert((byte*)&config.Flags - (byte*)&config == 20, "config flags offset");
        Assert((byte*)&input.Width - (byte*)&input == 24, "input width offset");
        Assert(Native.AbiVersion() == 1, "ABI version");
        Assert(Program.Options.TryParse(["--headless"], out var options, out _) && options.Frames == 120, "bounded headless default");
        Assert(Program.Options.TryParse(["--frames", "3"], out options, out _) && options.Frames == 3, "frame argument");
        Assert(!Program.Options.TryParse(["--frames", "0"], out _, out _), "reject zero frame limit");
        Assert(!Program.Options.TryParse(["--frames", "-1"], out _, out _), "reject negative frame limit");
        Assert(!Program.Options.TryParse(["--frames"], out _, out _), "reject absent frame limit");
        Assert(!Program.Options.TryParse(["--unknown"], out _, out _), "reject unknown option");
        Assert(Program.Pressed(Native.Space, 0, Native.Space), "space press edge");
        Assert(!Program.Pressed(Native.Space, Native.Space, Native.Space), "held space does not retrigger");
        Assert(!Program.Pressed(0, Native.Space, Native.Space), "space release does not trigger");
        var camera = new Camera { Zoom = 1 };
        Program.MoveCamera(ref camera, new Input { Keys = Native.Right | Native.Down }, 1);
        Assert(camera.X == 260 && camera.Y == 260, "camera keyboard pan");
        Program.MoveCamera(ref camera, new Input { Wheel = 100 }, 0);
        Assert(camera.Zoom == 8, "camera maximum zoom");
        Program.MoveCamera(ref camera, new Input { Wheel = -100 }, 0);
        Program.MoveCamera(ref camera, new Input { Wheel = -100 }, 0);
        Assert(camera.Zoom == 0.15f, "camera minimum zoom");
        var sprites = new Sprite[259];
        Program.Animate(sprites, 1);
        float firstX = sprites[3].X;
        Program.Animate(sprites, 2);
        Assert(sprites[3].X != firstX && sprites[3].A is >= 0 and <= 1, "animated alpha sprite data");
        Console.WriteLine("PASS layouts, CLI parsing, camera, animation");
    }

    private static void InvalidCreation()
    {
        nint context = 0;
        Config config = Config.Create(true, 8);
        Fail(Native.Create(null, &context), "null config");
        Assert(context == 0, "failed create returns null handle");
        Fail(Native.Create(&config, null), "null output pointer");
        var bad = config;
        bad.Size--;
        Fail(Native.Create(&bad, &context), "config size");
        bad = config; bad.AbiVersion++;
        Fail(Native.Create(&bad, &context), "ABI mismatch");
        bad = config; bad.Width = 0;
        Fail(Native.Create(&bad, &context), "zero width");
        bad = config; bad.Height = -1;
        Fail(Native.Create(&bad, &context), "negative height");
        bad = config; bad.MaxSprites = 0;
        Fail(Native.Create(&bad, &context), "zero batch capacity");
        bad = config; bad.Flags = 0x80000000;
        Fail(Native.Create(&bad, &context), "unknown flags");
        Fail(Native.Destroy(0), "null destroy");
        Assert(Native.Backend(0) == null && Native.Error().Length > 0, "invalid backend query");
        Console.WriteLine("PASS invalid creation and null handles");
    }

    private static void LifecycleAndBatchState()
    {
        var config = Config.Create(true, 8);
        nint context = 0;
        Ok(Native.Create(&config, &context), "create");
        try
        {
            Assert(context != 0, "created handle");
            Assert(Native.Utf8(Native.Backend(context)) == "headless-validation", "explicit headless backend");
            nint duplicate = 0;
            Fail(Native.Create(&config, &duplicate), "duplicate create");
            Assert(duplicate == 0, "duplicate output cleared");

            var input = new Input { Size = (uint)sizeof(Input) };
            Fail(Native.Poll(0, &input), "null poll context");
            Fail(Native.Poll(context, null), "null input");
            input.Size--;
            Fail(Native.Poll(context, &input), "input size");
            input.Size = (uint)sizeof(Input);
            Ok(Native.Poll(context, &input), "poll");
            Assert(input.Width == 960 && input.Height == 540, "headless dimensions");
            Assert(input.Keys == 0 && input.Quit == 0 && input.Wheel == 0, "headless input is inert");
            Assert(Native.Error().Length == 0, "successful calls clear prior error");

            var stats = new Stats { Size = (uint)sizeof(Stats) };
            Fail(Native.GetStats(context, null), "null stats");
            stats.Size--;
            Fail(Native.GetStats(context, &stats), "stats size");
            stats.Size = (uint)sizeof(Stats);
            Ok(Native.GetStats(context, &stats), "initial stats");
            Assert(stats.Frames == 0 && stats.Sprites == 0, "initial counters");

            var camera = new Camera { Zoom = 1 };
            var sprite = new Sprite { Width = 20, Height = 20, R = 1, G = 0.5f, B = 0.2f, A = 0.7f };
            Fail(Native.Submit(context, &sprite, 1), "submit before begin");
            Fail(Native.End(context), "end before begin");
            Fail(Native.Abort(context), "abort before begin");
            Fail(Native.Begin(context, null), "null camera");
            var badCamera = camera; badCamera.Zoom = 0;
            Fail(Native.Begin(context, &badCamera), "zero zoom");
            badCamera = camera; badCamera.X = float.NaN;
            Fail(Native.Begin(context, &badCamera), "NaN camera");
            badCamera = camera; badCamera.Zoom = float.PositiveInfinity;
            Fail(Native.Begin(context, &badCamera), "infinite zoom");

            Ok(Native.Begin(context, &camera), "empty begin");
            Fail(Native.Begin(context, &camera), "nested begin");
            Ok(Native.Submit(context, null, 0), "empty batch");
            Ok(Native.End(context), "empty end");
            Fail(Native.End(context), "double end");

            Sprite* batch = stackalloc Sprite[8];
            for (int i = 0; i < 8; i++) batch[i] = sprite;
            Ok(Native.Begin(context, &camera), "batch begin");
            Fail(Native.Submit(context, null, 1), "null nonempty batch");
            Fail(Native.Submit(context, batch, uint.MaxValue), "huge count rejected before reading");
            Ok(Native.Submit(context, batch, 2), "batch part one");
            Ok(Native.Submit(context, batch, 6), "batch part two");
            Fail(Native.Submit(context, &sprite, 1), "accumulated capacity overflow");
            Ok(Native.End(context), "batch end");

            Ok(Native.Begin(context, &camera), "invalid sprite begin");
            var badSprite = sprite; badSprite.X = float.NaN;
            Fail(Native.Submit(context, &badSprite, 1), "NaN sprite");
            badSprite = sprite; badSprite.Width = -1;
            Fail(Native.Submit(context, &badSprite, 1), "negative sprite extent");
            badSprite = sprite; badSprite.A = 1.1f;
            Fail(Native.Submit(context, &badSprite, 1), "invalid alpha");
            // Invalid second element must reject the whole call, including its
            // valid first element, so the eventual stats count remains exact.
            batch[1] = badSprite;
            Fail(Native.Submit(context, batch, 2), "atomic batch rejection");
            Ok(Native.Submit(context, &sprite, 1), "recovery after invalid batch");
            Ok(Native.End(context), "invalid sprite end");
            Ok(Native.Begin(context, &camera), "aborted frame begin");
            Ok(Native.Submit(context, &sprite, 1), "aborted frame submit");
            Ok(Native.Abort(context), "abort pending frame");
            Fail(Native.Abort(context), "double abort");
            Fail(Native.PlayTone(context), "headless audio unavailable");
            Ok(Native.GetStats(context, &stats), "final stats");
            Assert(stats.Frames == 3 && stats.Sprites == 9, "cumulative counters exclude rejected batches");
            Assert(stats.DrawCalls == 0 && stats.AudioPlays == 0, "headless never renders or plays audio");

            int threadResult = 0;
            string threadError = "";
            nint threadContext = context;
            var worker = new Thread(() =>
            {
                var workerInput = new Input { Size = (uint)sizeof(Input) };
                threadResult = Native.Poll(threadContext, &workerInput);
                threadError = Native.Error();
            });
            worker.Start();
            worker.Join();
            Assert(threadResult == -1 && threadError.Length > 0, "wrong-thread calls fail with thread-local error");
            Ok(Native.Poll(context, &input), "owner still usable after wrong-thread call");
        }
        finally
        {
            Ok(Native.Destroy(context), "destroy");
        }
        // Test stale pointers before any replacement allocation, avoiding ABA.
        Fail(Native.Destroy(context), "double destroy");
        Fail(Native.End(context), "stale context");
        Console.WriteLine("PASS lifecycle, batch state, invalid data, stats, thread ownership, errors");
    }

    private static void RecreateAndManagedOwnership()
    {
        for (int i = 0; i < 32; i++)
        {
            using var host = new EngineHost(true, 4);
            var camera = new Camera { Zoom = 1 };
            host.Draw(camera, ReadOnlySpan<Sprite>.Empty);
            Assert(host.GetStats().Frames == 1, "recreated context has independent counters");
            Assert(!host.TryPlayTone(out string error) && error.Length > 0, "managed audio failure");
        }
        var disposable = new EngineHost(true, 4);
        var cameraForRecovery = new Camera { Zoom = 1 };
        bool invalidRejected = false;
        try { disposable.Draw(cameraForRecovery, [new Sprite { Width = -1 }]); }
        catch (InvalidOperationException) { invalidRejected = true; }
        Assert(invalidRejected && disposable.GetStats().Frames == 0, "managed invalid batch aborted without counting a frame");
        disposable.Draw(cameraForRecovery, [new Sprite { Width = 1, Height = 1, A = 1 }]);
        Assert(disposable.GetStats().Frames == 1 && disposable.GetStats().Sprites == 1, "managed Draw recovers after a rejected batch");
        bool threadRejected = false;
        var worker = new Thread(() =>
        {
            try { disposable.Poll(); }
            catch (InvalidOperationException) { threadRejected = true; }
        });
        worker.Start(); worker.Join();
        Assert(threadRejected, "managed thread guard");
        disposable.Dispose();
        disposable.Dispose();
        bool disposedRejected = false;
        try { disposable.GetStats(); }
        catch (ObjectDisposedException) { disposedRejected = true; }
        Assert(disposedRejected, "managed disposed guard");
        Console.WriteLine("PASS 32 recreate cycles and managed ownership guards");
    }

    private static void Ok(int result, string label)
    {
        string error = result == 0 ? "" : Native.Error();
        Assert(result == 0, $"{label}: {error}");
    }

    private static void FrameAllocations()
    {
        const int warmupFrames = 128, measuredFrames = 1000;
        using var host = new EngineHost(true, 259);
        var sprites = new Sprite[259];
        var camera = new Camera { X = -480, Y = -270, Zoom = 1 };
        for (int i = 0; i < warmupFrames; i++)
        {
            Input input = host.Poll();
            Program.MoveCamera(ref camera, input, 1f / 60);
            Program.Animate(sprites, i / 60f);
            host.Draw(camera, sprites);
        }
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < measuredFrames; i++)
        {
            Input input = host.Poll();
            Program.MoveCamera(ref camera, input, 1f / 60);
            Program.Animate(sprites, (i + warmupFrames) / 60f);
            host.Draw(camera, sprites);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Stats stats = host.GetStats();
        Assert(stats.Frames == warmupFrames + measuredFrames, "allocation probe frame count");
        Assert(bytes == 0, $"warmed-up frame loop allocated {bytes} bytes");
        Console.WriteLine($"PASS frame allocations bytes={bytes} frames={measuredFrames} sprites_per_frame=259 warmup={warmupFrames}");
    }

    private static void Fail(int result, string label)
    {
        // Read the error before another ABI call can invalidate it.
        string error = Native.Error();
        Assert(result == -1, $"{label} should return -1; got {result}");
        Assert(error.Length > 0, $"{label} must supply an error");
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
            throw new InvalidOperationException($"SELF-TEST FAIL: {message}");
    }
}
