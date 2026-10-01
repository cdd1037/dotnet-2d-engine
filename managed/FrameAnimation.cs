namespace GameAuthoringLab;

/// <summary>Immutable fixed-rate frames identified by logical resource keys, independent of atlas placement.</summary>
public sealed class FrameClip
{
    public const int MaximumFrames = 4096;
    private readonly string[] _keys;
    public ReadOnlySpan<string> AssetKeys => _keys;
    public int FrameCount => _keys.Length;
    public double FrameSeconds { get; }
    public double DurationSeconds { get; }
    public FrameClip(ReadOnlySpan<string> assetKeys, double frameSeconds)
    {
        if (assetKeys.Length is < 1 or > MaximumFrames) throw new ArgumentOutOfRangeException(nameof(assetKeys));
        TimingStep.ValidateDuration(frameSeconds, false);
        TimingStep.ValidateSeconds(frameSeconds * assetKeys.Length, nameof(frameSeconds));
        foreach (string key in assetKeys)
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Every frame requires a nonempty resource key.", nameof(assetKeys));
        _keys = assetKeys.ToArray(); FrameSeconds = frameSeconds; DurationSeconds = frameSeconds * _keys.Length;
    }
}

public sealed class FramePlayer : TimingOperation
{
    private double _elapsed;
    public FrameClip Clip { get; }
    public bool Loop { get; }
    public int FrameIndex { get; private set; }
    public string AssetKey => Clip.AssetKeys[FrameIndex];
    public double ElapsedSeconds => _elapsed;
    public FramePlayer(FrameClip clip, bool loop = true, ClockDomain domain = ClockDomain.Game) : base(domain)
    { ArgumentNullException.ThrowIfNull(clip); Clip = clip; Loop = loop; }
    protected override void ResetCore() { _elapsed = 0; FrameIndex = 0; }
    protected override void AdvanceCore(double seconds)
    {
        if (Loop) _elapsed = (_elapsed + seconds % Clip.DurationSeconds) % Clip.DurationSeconds;
        else { _elapsed = Math.Min(_elapsed + seconds, Clip.DurationSeconds); if (_elapsed >= Clip.DurationSeconds) Complete(); }
        FrameIndex = Math.Min((int)(_elapsed / Clip.FrameSeconds), Clip.FrameCount - 1);
    }
}
