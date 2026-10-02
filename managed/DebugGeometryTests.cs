namespace GameAuthoringLab;

// Pure CPU geometry/storage checks: no native context or texture upload required.
internal static class DebugGeometryTests
{
    public static int Run()
    {
        int assertions = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("DEBUG GEOMETRY: " + label);
            assertions++;
        }
        void Reject(Action action, string label)
        {
            try { action(); }
            catch (ArgumentOutOfRangeException) { Check(true, label); return; }
            throw new InvalidOperationException("DEBUG GEOMETRY: accepted " + label);
        }

        var binding = new TextureBinding(42, new TextureRegion(7, 7, 1, 1));
        Reject(() => new DebugDrawBuffer(-1, binding), "negative capacity");
        Reject(() => new DebugDrawBuffer(DebugDrawBuffer.MaximumCapacity + 1, binding), "capacity above native frame maximum");
        foreach (var region in new[] { new TextureRegion(-1, 0, 1, 1), new TextureRegion(0, -1, 1, 1),
            new TextureRegion(0, 0, 0, 1), new TextureRegion(0, 0, 1, -1), new TextureRegion(int.MaxValue, 0, 1, 1) })
            Reject(() => new DebugDrawBuffer(1, new TextureBinding(42, region)), "invalid borrowed region");

        var buffer = new DebugDrawBuffer(8, binding);
        Check(!buffer.Enabled && buffer.Capacity == 8 && buffer.Count == 0 && buffer.RegionDraws.IsEmpty,
            "disabled default and exact fixed capacity");
        Check(!buffer.TryAddLine(float.NaN, 0, 1, 0, -1) && !buffer.TryAddRectangle(0, 0, -1, -1, 0)
            && buffer.Count == 0 && buffer.DroppedPrimitiveCount == 0, "disabled calls are allocation-free no-ops even for invalid inputs");
        buffer.Enabled = true;
        Check(buffer.TryAddLine(1, 2, 5, 2, 2, .25f, .5f, .75f, .5f), "horizontal line accepted");
        var horizontal = buffer.RegionDraws[0];
        Check(horizontal.Draw.X == 1 && horizontal.Draw.Y == 1 && horizontal.Draw.Width == 4
            && horizontal.Draw.Height == 2 && horizontal.Draw.M11 == 1 && horizontal.Draw.M22 == 1,
            "horizontal stroke centered with butt endpoints");
        Check(horizontal.Draw.R == .25f && horizontal.Draw.G == .5f && horizontal.Draw.B == .75f
            && horizontal.Draw.A == .5f && horizontal.Draw.Texture == 42, "color and borrowed texture retained");
        Check(horizontal.Size == 88 && horizontal.Version == 2 && horizontal.SourceX == 7 && horizontal.SourceY == 7
            && horizontal.SourceWidth == 1 && horizontal.SourceHeight == 1 && horizontal.Flags == 0 && horizontal.Reserved == 0,
            "white atlas region and V2 ABI metadata retained");
        Check(buffer.TryAddLine(10, 20, 10, 26, 4), "vertical line accepted");
        var vertical = buffer.RegionDraws[1].Draw;
        Check(Point(vertical, 0, 0, 12, 20) && Point(vertical, 1, 0, 12, 26)
            && Point(vertical, 0, 1, 8, 20) && Point(vertical, 1, 1, 8, 26), "vertical stroke corners");
        Check(buffer.TryAddLine(0, 0, 3, 4, 2), "diagonal line accepted");
        var diagonal = buffer.RegionDraws[2].Draw;
        Check(diagonal.Width == 5 && Point(diagonal, 0, .5, 0, 0) && Point(diagonal, 1, .5, 3, 4),
            "diagonal endpoints remain on stroke centerline");
        Check(Point(diagonal, 0, 0, .8, -.6) && Point(diagonal, 1, 1, 2.2, 4.6), "diagonal perpendicular thickness");
        Check(buffer.TryAddLine(3, 4, 0, 0, 2, a: 0), "reversed transparent line retained");
        var reverse = buffer.RegionDraws[3].Draw;
        Check(Point(reverse, 0, .5, 3, 4) && Point(reverse, 1, .5, 0, 0) && reverse.A == 0,
            "reversed endpoints and zero alpha preserve insertion order");
        Check(!buffer.TryAddLine(5, 6, 5, 6, 1) && !buffer.TryAddRectangle(0, 0, 0, 5, 1)
            && !buffer.TryAddRectangle(0, 0, 5, 0, 1) && buffer.Count == 4 && buffer.DroppedPrimitiveCount == 0,
            "degenerate primitives consume no storage or drop diagnostics");

        buffer.Clear();
        Check(buffer.Enabled && buffer.Count == 0 && buffer.Capacity == 8 && buffer.RegionDraws.IsEmpty,
            "clear preserves enable state and allocated capacity");
        Check(buffer.TryAddLine(-2, -2, -1, -2, 1, r: .1f), "line before rectangle");
        Check(buffer.TryAddRectangle(10, 20, 8, 6, 2, r: .4f, a: .5f), "rectangle appended as four quads");
        Check(buffer.TryAddLine(30, 30, 40, 30, 1, r: .8f), "line after rectangle");
        Check(buffer.Count == 6 && buffer.RegionDraws[0].Draw.R == .1f && buffer.RegionDraws[1].Draw.R == .4f
            && buffer.RegionDraws[5].Draw.R == .8f, "mixed primitives keep painter order");
        var rectangle = buffer.RegionDraws.Slice(1, 4);
        Check(Point(rectangle[0].Draw, 0, .5, 10, 20) && Point(rectangle[0].Draw, 1, .5, 18, 20), "top edge first");
        Check(Point(rectangle[1].Draw, 0, .5, 18, 20) && Point(rectangle[1].Draw, 1, .5, 18, 26), "right edge second");
        Check(Point(rectangle[2].Draw, 0, .5, 18, 26) && Point(rectangle[2].Draw, 1, .5, 10, 26), "bottom edge third");
        Check(Point(rectangle[3].Draw, 0, .5, 10, 26) && Point(rectangle[3].Draw, 1, .5, 10, 20), "left edge fourth");
        Check(!buffer.TryAddRectangle(0, 0, 1, 1, 1) && buffer.Count == 6 && buffer.DroppedPrimitiveCount == 1
            && buffer.RegionDraws[5].Draw.R == .8f, "rectangle capacity failure is atomic and counts one primitive");
        Check(buffer.TryAddLine(0, 0, 1, 1, 1) && buffer.TryAddLine(0, 0, 1, 1, 1) && buffer.Count == 8,
            "remaining slots reusable after failed rectangle");
        Check(!buffer.TryAddLine(0, 0, 1, 1, 1) && buffer.Count == 8 && buffer.DroppedPrimitiveCount == 2,
            "full buffer reports line drop without growth");
        Check(!buffer.TryAddLine(0, 0, 0, 0, 1) && buffer.DroppedPrimitiveCount == 2, "full buffer still ignores zero length");
        buffer.Enabled = false;
        Check(buffer.Count == 0 && buffer.RegionDraws.IsEmpty && buffer.DroppedPrimitiveCount == 2,
            "disable discards geometry and retains frame diagnostics");
        buffer.Enabled = true;
        Check(buffer.RegionDraws.IsEmpty, "re-enable cannot resurrect old geometry");
        buffer.Clear();
        Check(buffer.DroppedPrimitiveCount == 0, "clear resets frame diagnostics");

        var empty = new DebugDrawBuffer(0, binding) { Enabled = true };
        Check(!empty.TryAddLine(0, 0, 1, 1, 1) && !empty.TryAddRectangle(0, 0, 1, 1, 1)
            && empty.Count == 0 && empty.Capacity == 0 && empty.DroppedPrimitiveCount == 2, "zero capacity safely drops nonempty primitives");
        var four = new DebugDrawBuffer(4, binding) { Enabled = true };
        Check(four.TryAddRectangle(0, 0, 1, 1, 1) && four.Count == 4 && four.DroppedPrimitiveCount == 0,
            "exact rectangle capacity succeeds");

        Check(buffer.TryAddLine(0, 0, 1, 0, 1), "sentinel before numeric rejections");
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reject(() => buffer.TryAddLine(bad, 0, 1, 1, 1), "nonfinite first x");
            Reject(() => buffer.TryAddLine(0, bad, 1, 1, 1), "nonfinite first y");
            Reject(() => buffer.TryAddLine(0, 0, bad, 1, 1), "nonfinite second x");
            Reject(() => buffer.TryAddLine(0, 0, 1, bad, 1), "nonfinite second y");
            Reject(() => buffer.TryAddRectangle(bad, 0, 1, 1, 1), "nonfinite rectangle x");
            Reject(() => buffer.TryAddRectangle(0, bad, 1, 1, 1), "nonfinite rectangle y");
            Reject(() => buffer.TryAddRectangle(0, 0, bad, 1, 1), "nonfinite rectangle width");
            Reject(() => buffer.TryAddRectangle(0, 0, 1, bad, 1), "nonfinite rectangle height");
        }
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reject(() => buffer.TryAddLine(0, 0, 1, 1, bad), "invalid line thickness");
            Reject(() => buffer.TryAddRectangle(0, 0, 1, 1, bad), "invalid outline thickness");
        }
        foreach (float bad in new[] { -.1f, 1.1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Reject(() => buffer.TryAddLine(0, 0, 1, 1, 1, r: bad), "invalid red");
            Reject(() => buffer.TryAddLine(0, 0, 1, 1, 1, g: bad), "invalid green");
            Reject(() => buffer.TryAddRectangle(0, 0, 1, 1, 1, b: bad), "invalid blue");
            Reject(() => buffer.TryAddRectangle(0, 0, 1, 1, 1, a: bad), "invalid alpha");
        }
        Reject(() => buffer.TryAddRectangle(0, 0, -1, 1, 1), "negative rectangle width");
        Reject(() => buffer.TryAddRectangle(0, 0, 1, -1, 1), "negative rectangle height");
        Reject(() => buffer.TryAddLine(-float.MaxValue, 0, float.MaxValue, 0, 1), "finite endpoints with unrepresentable length");
        Reject(() => buffer.TryAddLine(float.MaxValue, 0, float.MaxValue, 1, float.MaxValue), "stroke thickness overflows origin");
        Reject(() => buffer.TryAddLine(0, float.MaxValue, 1, float.MaxValue, float.MaxValue), "stroke thickness overflows final corners");
        Reject(() => buffer.TryAddRectangle(float.MaxValue, 0, float.MaxValue, 1, 1), "rectangle right-bound overflow");
        Reject(() => buffer.TryAddRectangle(0, float.MaxValue, 1, float.MaxValue, 1), "rectangle bottom-bound overflow");
        Reject(() => buffer.TryAddRectangle(1e20f, 0, 1, 1, 1), "positive width lost to float precision");
        Reject(() => buffer.TryAddRectangle(0, 1e20f, 1, 1, 1), "positive height lost to float precision");
        Reject(() => buffer.TryAddRectangle(0, 0, float.MaxValue, 1, float.MaxValue), "later rectangle edge overflow is atomic");
        Check(buffer.Count == 1 && buffer.DroppedPrimitiveCount == 0 && buffer.RegionDraws[0].Draw.Width == 1,
            "numeric rejections preserve queued sentinel and drop count");
        Reject(() => four.TryAddLine(float.NaN, 0, 1, 1, 1), "invalid input is not hidden by capacity exhaustion");
        Check(four.Count == 4 && four.DroppedPrimitiveCount == 0, "invalid full-buffer call is not a capacity drop");

        buffer.Clear();
        Check(buffer.TryAddLine(-1e30f, 0, 1e30f, 0, 1) && float.IsFinite(buffer.RegionDraws[0].Draw.Width),
            "double intermediates accept large finite lengths whose float square overflows");
        Check(buffer.TryAddLine(0, 0, float.Epsilon, 0, float.Epsilon) && buffer.RegionDraws[1].Draw.Width == float.Epsilon,
            "smallest representable nonzero segment is not mistaken for zero length");
        var headless = new DebugDrawBuffer(1, new TextureBinding(0, binding.Region)) { Enabled = true };
        Check(headless.TryAddLine(0, 0, 1, 0, 1) && headless.RegionDraws[0].Draw.Texture == 0
            && headless.RegionDraws[0].SourceWidth == 0, "headless zero handle omits region consistently with SpriteBatch");
        var wholeTexture = new DebugDrawBuffer(1, new TextureBinding(43)) { Enabled = true };
        Check(wholeTexture.TryAddLine(0, 0, 1, 0, 1) && wholeTexture.RegionDraws[0].Draw.Texture == 43
            && wholeTexture.RegionDraws[0].SourceWidth == 0, "whole solid white texture needs no region");

        var warmed = new DebugDrawBuffer(5, binding) { Enabled = true };
        for (int i = 0; i < 128; i++) Exercise(warmed);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int observed = 0;
        for (int i = 0; i < 1000; i++) observed += Exercise(warmed);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0 && observed == 11000, "warmed append, clear, span, capacity and enable paths allocate zero bytes");
        var semantic = new DebugDrawBuffer(4, default(TextureBinding)) { Enabled = true };
        foreach (var line in new[] { (0f, 0f, 0f, 100_000_000f), (10_000_000f, -10_000_000f, -30_000_000f, 20_000_000f),
            (0f, 0f, 100_000_000f, 0f), (0f, 0f, 3f, 4f) })
        {
            semantic.Clear(); semantic.TryAddLine(line.Item1, line.Item2, line.Item3, line.Item4, 1);
            var raw = semantic.RegionDraws[0].Draw;
            var command = semantic.Commands[0] with { Tint = System.Numerics.Vector4.One };
            command.GetBasis(out float m11, out float m12, out float m21, out float m22);
            Check((m11, m12, m21, m22) == (raw.M11, raw.M12, raw.M21, raw.M22),
                "typed debug material/tint copies preserve the exact affine basis, including long vertical lines");
            for (int corner = 0; corner < 4; corner++)
            {
                double x = (corner & 1) == 0 ? 0 : raw.Width, y = (corner & 2) == 0 ? 0 : raw.Height;
                Check(command.Transform.X + m11 * x + m21 * y == raw.X + raw.M11 * x + raw.M21 * y
                    && command.Transform.Y + m12 * x + m22 * y == raw.Y + raw.M12 * x + raw.M22 * y,
                    "direct and typed debug submissions retain exactly equal corners");
            }
            var moved = command with { Transform = new(5, 7) };
            moved.GetBasis(out m11, out m12, out m21, out m22);
            Check((m11, m12, m21, m22) == (1f, 0f, 0f, 1f), "explicit Transform replacement resets the exact debug basis");
        }
        Console.WriteLine($"PASS debug geometry ({assertions} CPU assertions; warmed allocations={allocated})");
        return assertions;
    }

    private static bool Point(in SpriteDraw draw, double u, double v, double x, double y)
        => Math.Abs(draw.X + draw.M11 * (u * draw.Width) + draw.M21 * (v * draw.Height) - x) < .00001
        && Math.Abs(draw.Y + draw.M12 * (u * draw.Width) + draw.M22 * (v * draw.Height) - y) < .00001;

    private static int Exercise(DebugDrawBuffer buffer)
    {
        buffer.Enabled = false;
        buffer.TryAddLine(float.NaN, 0, 1, 1, -1);
        buffer.TryAddRectangle(0, 0, -1, -1, -1);
        buffer.Enabled = true;
        buffer.Clear();
        buffer.TryAddLine(0, 0, 3, 4, 2, a: .5f);
        buffer.TryAddRectangle(10, 20, 8, 6, 2);
        buffer.TryAddLine(0, 0, 1, 1, 1);
        return buffer.Count + buffer.RegionDraws.Length + buffer.DroppedPrimitiveCount;
    }
}
