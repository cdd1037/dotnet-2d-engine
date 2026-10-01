using System.Numerics;
namespace GameAuthoringLab;

internal enum TweenEase { Linear, EaseIn, EaseOut, SmoothStep }

/// <summary>Typed value interpolation, not a property binding. The caller applies Value explicitly.</summary>
internal sealed class Tween<T> : TimingOperation where T : struct
{
    private readonly T _from, _to;
    private readonly Func<T, T, double, T> _interpolate;
    private double _elapsed;
    public double DurationSeconds { get; }
    public TweenEase Ease { get; }
    public double Progress => DurationSeconds == 0 ? State == PlaybackState.Completed ? 1 : 0 : _elapsed / DurationSeconds;
    public T Value { get; private set; }
    internal Tween(T from, T to, double seconds, TweenEase ease, ClockDomain domain, Func<T, T, double, T> interpolate) : base(domain)
    {
        TimingStep.ValidateDuration(seconds, true);
        if (ease is < TweenEase.Linear or > TweenEase.SmoothStep) throw new ArgumentOutOfRangeException(nameof(ease));
        _from = from; _to = to; DurationSeconds = seconds; Ease = ease; _interpolate = interpolate; Value = from;
    }
    protected override void ResetCore() { _elapsed = 0; Value = _from; }
    protected override void AdvanceCore(double seconds)
    {
        _elapsed = Math.Min(_elapsed + seconds, DurationSeconds);
        if (_elapsed >= DurationSeconds) { Value = _to; Complete(); return; }
        double t = Progress;
        t = Ease switch { TweenEase.EaseIn => t * t, TweenEase.EaseOut => t * (2 - t), TweenEase.SmoothStep => t * t * (3 - 2 * t), _ => t };
        Value = _interpolate(_from, _to, t);
    }
}

internal static class Tween
{
    private static float Lerp(float from, float to, double t) => (float)((1 - t) * from + t * to);
    private static void Finite(float value) { if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "Tween values must be finite."); }
    public static Tween<float> Float(float from, float to, double seconds, TweenEase ease = TweenEase.Linear, ClockDomain domain = ClockDomain.Game)
    { Finite(from); Finite(to); return new(from, to, seconds, ease, domain, static (a, b, t) => Lerp(a, b, t)); }
    public static Tween<Vector2> Vector(Vector2 from, Vector2 to, double seconds, TweenEase ease = TweenEase.Linear, ClockDomain domain = ClockDomain.Game)
    {
        Finite(from.X); Finite(from.Y); Finite(to.X); Finite(to.Y);
        return new(from, to, seconds, ease, domain, static (a, b, t) => new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t)));
    }
    /// <summary>Straight RGBA channels in [0,1]. Numeric interpolation, not linear-light or premultiplied blending.</summary>
    public static Tween<Vector4> Color(Vector4 from, Vector4 to, double seconds, TweenEase ease = TweenEase.Linear, ClockDomain domain = ClockDomain.Game)
    {
        static void Validate(Vector4 v)
        {
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || !float.IsFinite(v.W)
                || v.X < 0 || v.X > 1 || v.Y < 0 || v.Y > 1 || v.Z < 0 || v.Z > 1 || v.W < 0 || v.W > 1)
                throw new ArgumentOutOfRangeException(nameof(v), "RGBA channels must be finite and in [0,1].");
        }
        Validate(from); Validate(to);
        return new(from, to, seconds, ease, domain, static (a, b, t) => new(Lerp(a.X, b.X, t), Lerp(a.Y, b.Y, t), Lerp(a.Z, b.Z, t), Lerp(a.W, b.W, t)));
    }
}
