namespace GameAuthoringLab;

/// <summary>
/// Reusable, fixed-capacity world-space line geometry in insertion (painter) order.
/// The caller supplies and retains a solid white texture or atlas region; this buffer owns no native resources.
/// </summary>
public sealed class DebugDrawBuffer
{
    // The native engine supports at most this many sprites in one frame. Callers
    // must still budget debug and scene draws against their EngineHost capacity.
    public const int MaximumCapacity = 65536;
    private readonly SpriteDrawV2[] _draws;
    private readonly TextureBinding _whiteTexture;
    private bool _enabled;

    /// <param name="capacity">Number of line quads, from zero through MaximumCapacity.</param>
    /// <param name="whiteTexture">
    /// Borrowed solid white texture or region, valid for the submitting engine. Region bounds
    /// are checked structurally here and against the actual texture by the engine on submission.
    /// Handle zero is suitable for headless validation only and omits region coordinates:
    /// the graphics fallback is a soft round sprite.
    /// </param>
    public DebugDrawBuffer(int capacity, TextureBinding whiteTexture)
    {
        if (capacity < 0 || capacity > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        whiteTexture.Region?.Validate(int.MaxValue, int.MaxValue);
        _draws = new SpriteDrawV2[capacity];
        _whiteTexture = whiteTexture;
    }

    /// <summary>
    /// Defaults to false. Disabled additions return false without validation or allocation.
    /// Disabling discards queued geometry, preventing old draws from returning on re-enable.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; if (!value) Count = 0; }
    }

    public int Capacity => _draws.Length;
    public int Count { get; private set; }
    /// <summary>
    /// Primitives rejected for capacity since Clear, saturating at int.MaxValue.
    /// A rectangle is one primitive (four quads); disabled, degenerate and invalid inputs do not count.
    /// </summary>
    public int DroppedPrimitiveCount { get; private set; }
    /// <summary>Borrowed storage, valid until the next mutation; submit with EngineHost.Draw.</summary>
    public ReadOnlySpan<SpriteDrawV2> RegionDraws => _draws.AsSpan(0, Count);

    /// <summary>Starts a new frame by clearing geometry and dropped-primitive diagnostics.</summary>
    public void Clear() { Count = 0; DroppedPrimitiveCount = 0; }

    /// <summary>
    /// Adds a butt-ended line with positive thickness centered on its endpoints, in world units.
    /// Returns false when disabled, zero length, or full. Invalid inputs throw before mutation.
    /// Colors must be finite and in [0, 1]; all generated corners must fit in float world coordinates.
    /// </summary>
    public bool TryAddLine(float x1, float y1, float x2, float y2, float thickness,
        float r = 1, float g = 1, float b = 1, float a = 1)
    {
        if (!Enabled) return false;
        ValidateFinite(x1, nameof(x1)); ValidateFinite(y1, nameof(y1));
        ValidateFinite(x2, nameof(x2)); ValidateFinite(y2, nameof(y2));
        ValidateStyle(thickness, r, g, b, a);
        if (x1 == x2 && y1 == y2) return false;
        var draw = MakeLine(x1, y1, x2, y2, thickness, r, g, b, a);
        if (!HasCapacity(1)) return false;
        _draws[Count++] = draw;
        return true;
    }

    /// <summary>
    /// Adds an axis-aligned rectangle outline, atomically reserving four line quads.
    /// Edges run top, right, bottom, left, clockwise in a y-down world; adjacent centered
    /// strokes overlap at corners (including normal alpha blending). Zero width or height
    /// is a no-op returning false; disabled or full buffers also return false.
    /// Negative extents and unrepresentable bounds throw before mutation.
    /// </summary>
    public bool TryAddRectangle(float x, float y, float width, float height, float thickness,
        float r = 1, float g = 1, float b = 1, float a = 1)
    {
        if (!Enabled) return false;
        ValidateFinite(x, nameof(x)); ValidateFinite(y, nameof(y));
        ValidateExtent(width, nameof(width)); ValidateExtent(height, nameof(height));
        ValidateStyle(thickness, r, g, b, a);
        if (width == 0 || height == 0) return false;
        float right = Coordinate((double)x + width), bottom = Coordinate((double)y + height);
        if (right <= x)
            throw new ArgumentOutOfRangeException(nameof(width), "Rectangle bounds must remain distinct float coordinates.");
        if (bottom <= y)
            throw new ArgumentOutOfRangeException(nameof(height), "Rectangle bounds must remain distinct float coordinates.");

        // Build and validate all edges before publishing any of them. A failed
        // geometry check or capacity reservation leaves the previous batch intact.
        var top = MakeLine(x, y, right, y, thickness, r, g, b, a);
        var end = MakeLine(right, y, right, bottom, thickness, r, g, b, a);
        var lower = MakeLine(right, bottom, x, bottom, thickness, r, g, b, a);
        var start = MakeLine(x, bottom, x, y, thickness, r, g, b, a);
        if (!HasCapacity(4)) return false;
        _draws[Count] = top; _draws[Count + 1] = end;
        _draws[Count + 2] = lower; _draws[Count + 3] = start;
        Count += 4;
        return true;
    }

    private bool HasCapacity(int required)
    {
        if (required <= Capacity - Count) return true;
        if (DroppedPrimitiveCount < int.MaxValue) DroppedPrimitiveCount++;
        return false;
    }

    private SpriteDrawV2 MakeLine(float x1, float y1, float x2, float y2, float thickness,
        float r, float g, float b, float a)
    {
        double dx = (double)x2 - x1, dy = (double)y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        float width = Coordinate(length);
        double ux = dx / length, uy = dy / length, half = thickness * .5;
        var draw = new SpriteDraw
        {
            M11 = (float)ux, M12 = (float)uy, M21 = (float)-uy, M22 = (float)ux,
            X = Coordinate(x1 + uy * half), Y = Coordinate(y1 - ux * half),
            Width = width, Height = thickness, R = r, G = g, B = b, A = a, Texture = _whiteTexture.Handle
        };
        // Validate the actual rounded affine representation, using the native
        // renderer's double intermediates rather than overflowing float products.
        for (int corner = 0; corner < 4; corner++)
        {
            double px = (corner & 1) == 0 ? 0 : draw.Width;
            double py = (corner & 2) == 0 ? 0 : draw.Height;
            Coordinate(draw.M11 * px + draw.M21 * py + draw.X);
            Coordinate(draw.M12 * px + draw.M22 * py + draw.Y);
        }
        return SpriteDrawV2.Create(draw, _whiteTexture.Handle == 0 ? null : _whiteTexture.Region);
    }

    private static float Coordinate(double value)
    {
        if (!double.IsFinite(value) || value < -float.MaxValue || value > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "Debug geometry exceeds float world-coordinate range.");
        return (float)value;
    }

    private static void ValidateFinite(float value, string parameter)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(parameter, "A finite value is required.");
    }

    private static void ValidateExtent(float value, string parameter)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(parameter, "A finite nonnegative extent is required.");
    }

    private static void ValidateStyle(float thickness, float r, float g, float b, float a)
    {
        if (!float.IsFinite(thickness) || thickness <= 0)
            throw new ArgumentOutOfRangeException(nameof(thickness), "A finite positive thickness is required.");
        ValidateColor(r, nameof(r)); ValidateColor(g, nameof(g));
        ValidateColor(b, nameof(b)); ValidateColor(a, nameof(a));
    }

    private static void ValidateColor(float value, string parameter)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(parameter, "Color components must be in [0, 1].");
    }
}
