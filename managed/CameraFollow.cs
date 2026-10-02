using System.Numerics;

namespace GameAuthoringLab;

/// <summary>World-pixel rectangle. Empty axes are allowed and center the view on that coordinate.</summary>
public readonly record struct CameraBounds(double X, double Y, double Width, double Height)
{
    internal void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Width) || !double.IsFinite(Height)
            || Width < 0 || Height < 0 || !double.IsFinite(X + Width) || !double.IsFinite(Y + Height))
            throw new ArgumentOutOfRangeException(nameof(CameraBounds), "Camera bounds require finite origins, nonnegative extents and finite right/bottom edges.");
    }
}

/// <summary>Default follows immediately on positive game time, without offset or bounds. Half-life is in seconds.</summary>
public readonly record struct CameraFollowOptions(
    Vector2 WorldOffset = default,
    double HalfLifeSeconds = 0,
    ClockDomain ClockDomain = ClockDomain.Game,
    CameraBounds? Bounds = null)
{
    internal void Validate()
    {
        if (!float.IsFinite(WorldOffset.X) || !float.IsFinite(WorldOffset.Y))
            throw new ArgumentOutOfRangeException(nameof(WorldOffset), "Camera offset must be finite.");
        if (!double.IsFinite(HalfLifeSeconds) || HalfLifeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(HalfLifeSeconds), "Half-life must be finite and nonnegative.");
        if (ClockDomain is not (ClockDomain.Game or ClockDomain.RealTime))
            throw new ArgumentOutOfRangeException(nameof(ClockDomain));
        Bounds?.Validate();
    }
}

/// <summary>
/// Pure camera positioning. The caller retains the returned Camera as smoothing state;
/// no entities, clocks, callbacks or native resources are retained. X/Y remain the view's top-left.
/// </summary>
public static class CameraFollow
{
    /// <summary>
    /// Follow a world point plus offset. Zero selected-clock time and inactive/invalid viewports
    /// preserve the current camera, including its out-of-bounds position, after input validation.
    /// Positive time constrains the desired view, smooths, then constrains the result.
    /// </summary>
    public static Camera Update(Camera currentCamera, Vector2 targetWorldPoint, Viewport viewport,
        TimingStep timingStep, CameraFollowOptions options = default)
    {
        Validate(currentCamera, targetWorldPoint, options);
        double seconds = options.ClockDomain == ClockDomain.Game ? timingStep.GameSeconds : timingStep.RealSeconds;
        if (seconds == 0 || !viewport.IsValid) return currentCamera;
        return Position(currentCamera, targetWorldPoint, viewport, options, seconds, snap: false);
    }

    /// <summary>Immediately reframe a teleported/replaced target, independently of clock or half-life. Invalid viewports preserve the camera.</summary>
    public static Camera Snap(Camera currentCamera, Vector2 targetWorldPoint, Viewport viewport,
        CameraFollowOptions options = default)
    {
        Validate(currentCamera, targetWorldPoint, options);
        if (!viewport.IsValid) return currentCamera;
        return Position(currentCamera, targetWorldPoint, viewport, options, 0, snap: true);
    }

    private static void Validate(Camera camera, Vector2 target, CameraFollowOptions options)
    {
        if (!float.IsFinite(camera.X) || !float.IsFinite(camera.Y) || !float.IsFinite(camera.Zoom)
            || camera.Zoom is < .01f or > 100)
            throw new ArgumentOutOfRangeException(nameof(camera), "Camera coordinates must be finite and zoom must be in [.01, 100].");
        if (!float.IsFinite(target.X) || !float.IsFinite(target.Y))
            throw new ArgumentOutOfRangeException(nameof(target), "Camera target must be finite.");
        options.Validate();
    }

    private static Camera Position(Camera current, Vector2 target, Viewport viewport,
        CameraFollowOptions options, double seconds, bool snap)
    {
        double width = viewport.PixelWidth / (double)current.Zoom, height = viewport.PixelHeight / (double)current.Zoom;
        double x = (double)target.X + options.WorldOffset.X - width / 2;
        double y = (double)target.Y + options.WorldOffset.Y - height / 2;
        Constrain(ref x, ref y, width, height, options.Bounds);
        if (!snap && options.HalfLifeSeconds != 0)
        {
            // Beyond 1075 half-lives the double retention factor underflows to zero.
            // Test before division so finite subnormal half-lives cannot overflow it.
            double ratio = options.HalfLifeSeconds < seconds / 1075 ? 1075 : seconds / options.HalfLifeSeconds;
            double retention = Math.Pow(.5, ratio);
            // A short series avoids subtractive cancellation for tiny deltas.
            // The complementary form retains tiny tails near a zero target.
            double exponent = Math.Log(2) * ratio;
            double blend = exponent < .0001
                ? exponent * (1 - exponent * (.5 - exponent * (1d / 6 - exponent / 24)))
                : 1 - retention;
            x = Blend(current.X, x, blend, retention);
            y = Blend(current.Y, y, blend, retention);
            Constrain(ref x, ref y, width, height, options.Bounds);
        }
        float outputX = Coordinate(x), outputY = Coordinate(y);
        // Converting to float can round an edge outward. Keep a fitting view inside
        // its world interval; an axis smaller than the view intentionally centers.
        if (options.Bounds is { } bounds)
        {
            outputX = Inward(outputX, bounds.X, bounds.Width, width);
            outputY = Inward(outputY, bounds.Y, bounds.Height, height);
        }
        return new Camera { X = outputX, Y = outputY, Zoom = current.Zoom };
    }

    private static double Blend(double current, double desired, double blend, double retention) =>
        blend <= .5 ? current + (desired - current) * blend : desired + (current - desired) * retention;

    private static void Constrain(ref double x, ref double y, double width, double height, CameraBounds? bounds)
    {
        if (bounds is not { } value) return;
        x = Axis(x, value.X, value.Width, width);
        y = Axis(y, value.Y, value.Height, height);
    }

    private static double Axis(double value, double origin, double extent, double view) =>
        extent < view ? origin + (extent - view) / 2 : Math.Clamp(value, origin, origin + (extent - view));

    private static float Coordinate(double value)
    {
        if (!double.IsFinite(value) || value < -float.MaxValue || value > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "The resulting camera position must be representable as finite float coordinates.");
        return (float)value;
    }

    private static float Inward(float value, double origin, double extent, double view)
    {
        if (extent < view) return value;
        double maximum = origin + (extent - view);
        if (value < origin) value = float.BitIncrement(value);
        if (value > maximum) value = float.BitDecrement(value);
        if (!float.IsFinite(value) || value < origin || value > maximum)
            throw new ArgumentOutOfRangeException(nameof(value), "The bounded view has no representable float camera position.");
        return value;
    }
}
