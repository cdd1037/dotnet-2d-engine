namespace GameAuthoringLab;

/// <summary>A frame-entry marker. EventId is an opaque caller-owned integer, including zero or negative values.</summary>
public readonly record struct FrameMarker(int FrameIndex, int EventId);

/// <summary>Immutable fixed-rate frames identified by logical resource keys, independent of atlas placement.</summary>
public sealed class FrameClip
{
    public const int MaximumFrames = 4096;
    public const int MaximumMarkers = 4096;
    private readonly string[] _keys;
    private readonly FrameMarker[] _markers;
    private readonly int[] _markerStarts;
    public ReadOnlySpan<string> AssetKeys => _keys;
    /// <summary>Copied markers in ascending frame order, preserving authored order within each frame.</summary>
    public ReadOnlySpan<FrameMarker> Markers => _markers;
    public int FrameCount => _keys.Length;
    public double FrameSeconds { get; }
    public double DurationSeconds { get; }
    public FrameClip(ReadOnlySpan<string> assetKeys, double frameSeconds) : this(assetKeys, frameSeconds, []) { }
    public FrameClip(ReadOnlySpan<string> assetKeys, double frameSeconds, ReadOnlySpan<FrameMarker> markers)
    {
        if (assetKeys.Length is < 1 or > MaximumFrames) throw new ArgumentOutOfRangeException(nameof(assetKeys));
        if (markers.Length > MaximumMarkers) throw new ArgumentOutOfRangeException(nameof(markers));
        TimingStep.ValidateDuration(frameSeconds, false);
        TimingStep.ValidateSeconds(frameSeconds * assetKeys.Length, nameof(frameSeconds));
        foreach (string key in assetKeys)
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Every frame requires a nonempty resource key.", nameof(assetKeys));
        foreach (FrameMarker marker in markers)
            if ((uint)marker.FrameIndex >= (uint)assetKeys.Length) throw new ArgumentOutOfRangeException(nameof(markers), "Marker frames must be inside the clip.");
        _keys = assetKeys.ToArray(); FrameSeconds = frameSeconds; DurationSeconds = frameSeconds * _keys.Length;
        if (markers.IsEmpty) { _markers = []; _markerStarts = []; return; }
        // Stable counting sort at setup: no sorting, searching or temporary arrays during playback.
        _markers = new FrameMarker[markers.Length]; _markerStarts = new int[FrameCount + 1];
        foreach (FrameMarker marker in markers) _markerStarts[marker.FrameIndex + 1]++;
        for (int i = 1; i < _markerStarts.Length; i++) _markerStarts[i] += _markerStarts[i - 1];
        int[] positions = (int[])_markerStarts.Clone();
        foreach (FrameMarker marker in markers) _markers[positions[marker.FrameIndex]++] = marker;
    }
    internal int MarkerStart(int frame) => _markerStarts.Length == 0 ? 0 : _markerStarts[frame];
}

/// <summary>Explicit-clock playback with bounded, polled frame-entry events and no resource loading.</summary>
public sealed class FramePlayer : TimingOperation
{
    public const int DefaultEventCapacity = 64;
    public const int MaximumEventCapacity = 1024;
    private readonly FrameMarker[] _events;
    private int _eventCount;
    private bool _initialEntryPending = true;
    private double _elapsed;
    public FrameClip Clip { get; private set; }
    public bool Loop { get; private set; }
    public int FrameIndex { get; private set; }
    public string AssetKey => Clip.AssetKeys[FrameIndex];
    public double ElapsedSeconds => _elapsed;
    public int EventCapacity => _events.Length;
    /// <summary>Borrowed earliest events from the latest Advance. Copy values before the next Advance, Cancel, Restart, switching/restarting Play, or Dispose.</summary>
    public ReadOnlySpan<FrameMarker> Events => _events.AsSpan(0, _eventCount);
    public ulong EventsDue { get; private set; }
    public ulong EventsDropped => EventsDue - (ulong)_eventCount;
    public FramePlayer(FrameClip clip, bool loop = true, ClockDomain domain = ClockDomain.Game)
        : this(clip, DefaultEventCapacity, loop, domain) { }
    public FramePlayer(FrameClip clip, int eventCapacity, bool loop = true, ClockDomain domain = ClockDomain.Game) : base(domain)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (eventCapacity is < 0 or > MaximumEventCapacity) throw new ArgumentOutOfRangeException(nameof(eventCapacity));
        Clip = clip; Loop = loop; _events = new FrameMarker[eventCapacity];
    }
    /// <summary>Switch/restart resets and runs. Repeating the same clip reference without restart retains phase, state and results, updating only the loop policy.</summary>
    public void Play(FrameClip clip, bool loop = true, bool restart = false)
    {
        CheckAccess(); ArgumentNullException.ThrowIfNull(clip);
        if (ReferenceEquals(Clip, clip) && !restart) { Loop = loop; return; }
        Clip = clip; Loop = loop; Restart();
    }
    protected override void ClearAdvanceResult() { _eventCount = 0; EventsDue = 0; }
    protected override void ResetCore() { _elapsed = 0; FrameIndex = 0; _initialEntryPending = true; }
    protected override void AdvanceCore(double seconds)
    {
        int previousFrame = FrameIndex;
        ulong cycles = 0;
        if (Loop)
        {
            double remainder = seconds % Clip.DurationSeconds;
            // Recover the integral quotient from the SAME remainder as phase advancement.
            // floor(seconds / duration) can round up at decimal boundaries (e.g. 1 / .1),
            // while % still has an almost-full remainder. The bounded ratio is <= 86.4e9;
            // rounding its recovered integer is safe and adds no event-time epsilon.
            cycles = (ulong)Math.Round((seconds - remainder) / Clip.DurationSeconds);
            _elapsed += remainder;
            if (_elapsed >= Clip.DurationSeconds) { _elapsed -= Clip.DurationSeconds; cycles++; }
        }
        else { _elapsed = Math.Min(_elapsed + seconds, Clip.DurationSeconds); if (_elapsed >= Clip.DurationSeconds) Complete(); }
        FrameIndex = Math.Min((int)(_elapsed / Clip.FrameSeconds), Clip.FrameCount - 1);
        if (_initialEntryPending) { AppendFrames(0, 1); _initialEntryPending = false; }
        if (cycles == 0) AppendFrames(previousFrame + 1, FrameIndex + 1);
        else
        {
            AppendFrames(previousFrame + 1, Clip.FrameCount);
            AppendCycles(cycles - 1);
            AppendFrames(0, FrameIndex + 1);
        }
    }
    private void AppendFrames(int firstFrame, int endFrame)
    {
        int first = Clip.MarkerStart(firstFrame), end = Clip.MarkerStart(endFrame);
        EventsDue += (ulong)(end - first);
        Append(Clip.Markers.Slice(first, end - first));
    }
    private void AppendCycles(ulong cycles)
    {
        ReadOnlySpan<FrameMarker> markers = Clip.Markers;
        EventsDue += cycles * (ulong)markers.Length;
        // Only iterate materialized events. Even billions of missed cycles cost at
        // most EventCapacity copies; zero markers/capacity never enter the loop.
        while (cycles != 0 && !markers.IsEmpty && _eventCount < _events.Length) { Append(markers); cycles--; }
    }
    private void Append(ReadOnlySpan<FrameMarker> markers)
    {
        int count = Math.Min(markers.Length, _events.Length - _eventCount);
        markers[..count].CopyTo(_events.AsSpan(_eventCount)); _eventCount += count;
    }
}
