using System.Numerics;
using System.Runtime.InteropServices;

namespace GameAuthoringLab;

// Pure managed contract tests. No window, native library, world or entity is needed.
internal static class CameraFollowTests
{
    public static int Run()
    {
        int assertions = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("CAMERA FOLLOW: " + label);
            assertions++;
        }
        void Reject(Action action, string label)
        {
            try { action(); }
            catch (ArgumentOutOfRangeException) { assertions++; return; }
            throw new InvalidOperationException("CAMERA FOLLOW accepted: " + label);
        }
        static bool At(Camera value, double x, double y, double epsilon = .0001) =>
            Math.Abs(value.X - x) <= epsilon && Math.Abs(value.Y - y) <= epsilon;
        static bool Same(Camera left, Camera right) =>
            left.X == right.X && left.Y == right.Y && left.Zoom == right.Zoom;
        static TimingStep Step(double seconds) => TimingStep.FromReal(seconds);

        var viewport = new Viewport(480, 270, 960, 540);
        var camera = new Camera { X = 10, Y = 20, Zoom = 1 };
        var target = new Vector2(880, 570);
        Check(Marshal.SizeOf<Camera>() == 12, "existing camera ABI remains three floats");
        Check(new CameraFollowOptions() == default, "constructed and zero-initialized options agree");
        var immediate = CameraFollow.Update(camera, target, viewport, Step(.01));
        Check(At(immediate, 400, 300) && immediate.Zoom == 1, "default follows target at view center on positive time");
        Check(At(camera, 10, 20), "input camera is copied without mutation");
        Check(At(CameraFollow.Snap(camera, new(-20, -30), viewport), -500, -300), "negative targets and top-left positions remain valid");
        Check(At(CameraFollow.Snap(camera, target, viewport, new(WorldOffset: new(50, -80))), 450, 220), "world offset applies before viewport centering");
        var zoomed = new Camera { X = -5, Y = -6, Zoom = 2 };
        var dpi = CameraFollow.Snap(zoomed, new(300, 250), new(400, 300, 800, 600));
        Check(At(dpi, 100, 100) && dpi.Zoom == 2, "framebuffer dimensions, not logical window dimensions, determine the view");
        foreach (float zoom in new[] { .01f, .15f, 1f, 2.5f, 100f })
        {
            var result = CameraFollow.Snap(new() { Zoom = zoom }, Vector2.Zero, viewport);
            Check(At(result, -960 / (double)zoom / 2, -540 / (double)zoom / 2, .01) && result.Zoom == zoom,
                "all supported zoom values preserve zoom and center correctly");
        }

        var half = new CameraFollowOptions(HalfLifeSeconds: .5);
        var origin = new Camera { Zoom = 1 };
        Check(At(CameraFollow.Update(origin, target, viewport, Step(.5), half), 200, 150), "one half-life closes exactly half the distance");
        Check(At(CameraFollow.Update(origin, target, viewport, Step(1), half), 300, 225), "two half-lives close three quarters");
        Check(At(CameraFollow.Update(origin, target, viewport, Step(.25), half), 400 * (1 - Math.Sqrt(.5)), 300 * (1 - Math.Sqrt(.5))),
            "fractional half-life exponential response");
        foreach (int partitions in new[] { 2, 4, 8, 30, 60, 240 })
        {
            var whole = CameraFollow.Update(camera, target, viewport, Step(2), half);
            var split = camera;
            for (int i = 0; i < partitions; i++) split = CameraFollow.Update(split, target, viewport, Step(2d / partitions), half);
            Check(At(split, whole.X, whole.Y, .001), "fixed target partition invariance within float rounding");
        }
        var unchangedTarget = new Vector2(490, 290);
        Check(Same(CameraFollow.Update(camera, unchangedTarget, viewport, Step(.00001), half), camera), "already-centered target remains exactly stationary");
        Check(At(CameraFollow.Snap(camera, target, viewport, half), 400, 300), "snap bypasses smoothing for a teleport");
        var teleported = CameraFollow.Snap(camera, new(-800, -570), viewport, half);
        Check(At(teleported, -1280, -840), "snap forgets previous position through returned camera state");
        Check(At(CameraFollow.Update(teleported, target, viewport, Step(.5), half), -440, -270), "smoothing resumes from explicit snapped state");

        var square = new Viewport(100, 100, 200, 200);
        var bounded = new CameraFollowOptions(HalfLifeSeconds: 1, Bounds: new(0, 0, 1000, 1000));
        Check(At(CameraFollow.Update(origin, new(5000, 5000), square, Step(1), bounded), 400, 400),
            "desired position is bounded before smoothing");
        Check(At(CameraFollow.Update(new() { X = -1000, Y = 2000, Zoom = 1 }, new(500, 500), square, Step(1), bounded), 0, 800),
            "output is bounded after smoothing an initially invalid position");
        Check(At(CameraFollow.Snap(origin, new(-500, 5000), square, bounded), 0, 800), "snap clamps both world edges");
        Check(At(CameraFollow.Snap(new() { Zoom = 2 }, new(5000, 5000), square, bounded), 900, 900),
            "bounds use the zoom-dependent visible extent");
        Check(At(CameraFollow.Snap(origin, new(-5000, 5000), square, new(Bounds: new(-600, -400, 1000, 1000))), -600, 400),
            "negative world origins and opposite edge clamps");
        Check(At(CameraFollow.Snap(origin, target, square, new(Bounds: new(10, 20, 100, 80))), -40, -40),
            "world smaller than view is centered on both axes");
        Check(At(CameraFollow.Snap(origin, new(9000, 9000), square, new(Bounds: new(10, 20, 100, 1000))), -40, 820),
            "small axis centers independently from fitting axis");
        Check(At(CameraFollow.Snap(origin, target, square, new(Bounds: new(-10, -20, 200, 200))), -10, -20),
            "exact-fit world has one fixed top-left");
        Check(At(CameraFollow.Snap(origin, target, square, new(Bounds: new(10, 20, 0, 0))), -90, -80),
            "zero-size world axes center their point");
        var resumedBounds = new CameraFollowOptions(HalfLifeSeconds: double.MaxValue, Bounds: new(10, 20, 100, 80));
        Check(At(CameraFollow.Update(origin, target, square, Step(1), resumedBounds), -40, -40),
            "small-world constraint still applies after arbitrarily slow smoothing");
        foreach (float zoom in new[] { .3f, 1.3f, 3f })
        {
            var result = CameraFollow.Snap(new() { Zoom = zoom }, new(10000, 10000), square, bounded);
            Check(result.X >= 0 && result.Y >= 0 && result.X + 200 / (double)zoom <= 1000
                && result.Y + 200 / (double)zoom <= 1000, "float conversion never rounds fitting views outside the far bounds");
        }
        var fractionalBounds = new CameraFollowOptions(Bounds: new(.1, .1, 1000, 1000));
        var fractionalStart = CameraFollow.Snap(origin, new(-100, -100), square, fractionalBounds);
        Check(fractionalStart.X >= .1 && fractionalStart.Y >= .1, "float conversion rounds near bounds inward");

        var outside = new Camera { X = -500, Y = 3000, Zoom = 1 };
        Check(Same(CameraFollow.Update(outside, target, square, default, bounded), outside), "zero delta preserves exact out-of-bounds state");
        Check(Same(CameraFollow.Update(outside, target, square, TimingStep.FromReal(1, paused: true), bounded), outside),
            "paused game time does not follow or reclamp");
        Check(Same(CameraFollow.Update(outside, target, square, TimingStep.FromReal(1, timeScale: 0)), outside),
            "zero half-life still respects zero-scaled game time");
        var realClock = half with { ClockDomain = ClockDomain.RealTime };
        Check(At(CameraFollow.Update(origin, target, viewport, TimingStep.FromReal(.5, paused: true), realClock), 200, 150),
            "explicit real-time domain continues while game time is paused");
        Check(At(CameraFollow.Update(origin, target, viewport, TimingStep.FromReal(1, timeScale: .5), half), 200, 150),
            "game smoothing uses scaled delta");
        Check(At(CameraFollow.Update(origin, target, viewport, new TimingStep(0, .5), half), 200, 150),
            "independently supplied game delta selects only game clock");
        Check(Same(CameraFollow.Update(camera, target, viewport, new TimingStep(0, .5), realClock), camera),
            "real-time zero delta does not borrow positive game delta");
        foreach (var inactive in new[] { default(Viewport), new Viewport(100, 100, 200, 200, false),
            new Viewport(0, 100, 200, 200), new Viewport(100, 100, 0, 200), new Viewport(100, 100, -1, 200),
            new Viewport(16385, 100, 200, 200), new Viewport(100, 100, 200, 16385) })
        {
            Check(Same(CameraFollow.Update(outside, target, inactive, Step(1), bounded), outside), "inactive or invalid viewport retains camera on update");
            Check(Same(CameraFollow.Snap(outside, target, inactive, bounded), outside), "inactive or invalid viewport retains camera on snap");
            Reject(() => CameraFollow.Update(default, target, inactive, default), "invalid camera rejected before inactive viewport or zero time");
            Reject(() => CameraFollow.Snap(camera, new(float.NaN, 0), inactive), "invalid target rejected before inactive viewport");
        }

        foreach (float bad in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity })
        {
            Reject(() => CameraFollow.Update(new() { X = bad, Zoom = 1 }, target, viewport, default), "nonfinite current x");
            Reject(() => CameraFollow.Snap(new() { Y = bad, Zoom = 1 }, target, viewport), "nonfinite current y");
            Reject(() => CameraFollow.Update(camera, new(bad, 0), viewport, default), "nonfinite target x");
            Reject(() => CameraFollow.Snap(camera, new(0, bad), viewport), "nonfinite target y");
            Reject(() => CameraFollow.Update(camera, target, viewport, default, new(WorldOffset: new(bad, 0))), "nonfinite offset x");
            Reject(() => CameraFollow.Snap(camera, target, viewport, new(WorldOffset: new(0, bad))), "nonfinite offset y");
        }
        foreach (float bad in new[] { 0f, -1f, .009f, 100.01f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Reject(() => CameraFollow.Snap(new() { Zoom = bad }, target, viewport), "unsupported zoom");
        foreach (double bad in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Reject(() => CameraFollow.Update(camera, target, viewport, default, new(HalfLifeSeconds: bad)), "invalid half-life even at zero delta");
            Reject(() => CameraFollow.Snap(camera, target, viewport, new(HalfLifeSeconds: bad)), "snap still validates unused half-life");
        }
        Reject(() => CameraFollow.Update(camera, target, viewport, Step(1), new(ClockDomain: (ClockDomain)42)), "unknown clock domain");
        Reject(() => CameraFollow.Snap(camera, target, viewport, new(ClockDomain: (ClockDomain)(-1))), "snap validates clock domain");
        foreach (double bad in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        {
            Reject(() => CameraFollow.Snap(camera, target, viewport, new(Bounds: new(bad, 0, 1, 1))), "nonfinite bounds x");
            Reject(() => CameraFollow.Snap(camera, target, viewport, new(Bounds: new(0, bad, 1, 1))), "nonfinite bounds y");
            Reject(() => CameraFollow.Update(camera, target, viewport, default, new(Bounds: new(0, 0, bad, 1))), "nonfinite bounds width before zero-time return");
            Reject(() => CameraFollow.Update(camera, target, viewport, default, new(Bounds: new(0, 0, 1, bad))), "nonfinite bounds height before zero-time return");
        }
        Reject(() => CameraFollow.Snap(camera, target, viewport, new(Bounds: new(0, 0, -1, 1))), "negative bounds width");
        Reject(() => CameraFollow.Snap(camera, target, viewport, new(Bounds: new(0, 0, 1, -1))), "negative bounds height");
        Reject(() => CameraFollow.Snap(camera, target, viewport, new(Bounds: new(double.MaxValue, 0, double.MaxValue, 1))), "overflowing bounds right edge");
        Reject(() => CameraFollow.Snap(camera, target, viewport, new(Bounds: new(0, double.MaxValue, 1, double.MaxValue))), "overflowing bounds bottom edge");
        Reject(() => CameraFollow.Snap(camera, new(float.MaxValue, 0), viewport, new(WorldOffset: new(float.MaxValue, 0))), "finite inputs producing unrepresentable output x");
        Reject(() => CameraFollow.Snap(camera, new(0, -float.MaxValue), viewport, new(WorldOffset: new(0, -float.MaxValue))), "finite inputs producing unrepresentable output y");
        Reject(() => CameraFollow.Snap(camera, target, viewport, new(Bounds: new(double.MaxValue, 0, 1, 1))), "unrepresentable bounded position");
        Reject(() => CameraFollow.Snap(camera, target, square, new(Bounds: new(.1, 0, 200, 200))), "exact-fit interval without any float position fails explicitly");
        Check(At(CameraFollow.Snap(camera, new(float.MaxValue, -float.MaxValue), square,
            new(WorldOffset: new(float.MaxValue, -float.MaxValue), Bounds: new(0, 0, 1000, 1000))), 800, 0),
            "double arithmetic allows extreme finite off-world targets to be bounded safely");
        var extreme = CameraFollow.Update(new() { X = float.MaxValue, Y = -float.MaxValue, Zoom = 1 },
            new(-float.MaxValue, float.MaxValue), viewport, Step(1), new(HalfLifeSeconds: 1));
        Check(At(extreme, 0, 0), "opposite extreme positions do not overflow float subtraction");
        Check(At(CameraFollow.Update(camera, target, viewport, Step(1), new(HalfLifeSeconds: double.Epsilon)), 400, 300),
            "positive subnormal half-life converges without division overflow");
        Check(Same(CameraFollow.Update(camera, target, viewport, Step(double.Epsilon), new(HalfLifeSeconds: double.MaxValue)), camera),
            "subnormal delta and very long half-life safely produce unobservable movement");
        var tiny = CameraFollow.Update(origin, new(1e30f, 1e30f), viewport, Step(1e-18), new(HalfLifeSeconds: 1));
        Check(tiny.X > 6e11f && tiny.X < 8e11f && tiny.Y == tiny.X, "tiny blend preserves measurable movement toward a large target");
        var tail = CameraFollow.Update(new() { X = 1e30f, Y = 1e30f, Zoom = 1 }, new(480, 270), viewport, Step(100), new(HalfLifeSeconds: 1));
        Check(tail.X > .7f && tail.X < .9f && tail.Y == tail.X, "long smoothing steps retain representable residual distance");

        var running = camera;
        for (int i = 0; i < 128; i++) running = CameraFollow.Update(running, target, viewport, Step(.016), bounded);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
        {
            running = CameraFollow.Update(running, target, viewport, Step(.016), bounded);
            running = CameraFollow.Snap(running, target, viewport, bounded);
        }
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "warmed bounded update and snap allocate zero bytes");
        Check(float.IsFinite(running.X) && float.IsFinite(running.Y), "repeated results remain finite");
        Console.WriteLine($"CAMERA FOLLOW SELF-TEST PASS assertions={assertions}");
        return assertions;
    }
}
