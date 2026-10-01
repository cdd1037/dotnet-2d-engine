namespace GameAuthoringLab;

public enum ClockDomain { Game, RealTime }
public enum PlaybackState { Running, Paused, Completed, Cancelled }

/// <summary>Caller-supplied deltas, never wall-clock timestamps. Default is a zero step.</summary>
public readonly record struct TimingStep
{
    public const double MaximumSeconds = 86_400;
    public double RealSeconds { get; }
    public double GameSeconds { get; }
    public TimingStep(double realSeconds, double gameSeconds)
    {
        ValidateSeconds(realSeconds, nameof(realSeconds)); ValidateSeconds(gameSeconds, nameof(gameSeconds));
        RealSeconds = realSeconds; GameSeconds = gameSeconds;
    }
    public static TimingStep FromReal(double seconds, bool paused = false, double timeScale = 1)
    {
        ValidateSeconds(seconds, nameof(seconds));
        if (!double.IsFinite(timeScale) || timeScale < 0 || timeScale > 16)
            throw new ArgumentOutOfRangeException(nameof(timeScale), "Time scale must be finite and in [0, 16].");
        return new(seconds, paused ? 0 : seconds * timeScale);
    }
    internal double For(ClockDomain domain) => domain == ClockDomain.Game ? GameSeconds : RealSeconds;
    internal static void ValidateSeconds(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > MaximumSeconds)
            throw new ArgumentOutOfRangeException(name, "Seconds must be finite and in [0, 86400]; deltas are rejected, never silently clamped.");
    }
    internal static void ValidateDuration(double value, bool allowZero)
    {
        ValidateSeconds(value, nameof(value));
        if (value < .000001 && !(allowZero && value == 0))
            throw new ArgumentOutOfRangeException(nameof(value), "Positive durations must be at least one microsecond.");
    }
}

/// <summary>Main-thread mutable playback. Values are polled; no callbacks or implicit entity mutation.</summary>
public abstract class TimingOperation : IDisposable
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    public ClockDomain Domain { get; }
    public PlaybackState State { get; private set; } = PlaybackState.Running;
    public bool IsDisposed { get; private set; }
    public bool CompletedThisAdvance { get; private set; }
    internal TimingScope? Owner;
    private protected TimingOperation(ClockDomain domain)
    {
        if (domain is not (ClockDomain.Game or ClockDomain.RealTime)) throw new ArgumentOutOfRangeException(nameof(domain));
        Domain = domain;
    }
    public void Advance(TimingStep step)
    {
        CheckAccess(); CompletedThisAdvance = false; ClearAdvanceResult();
        double seconds = step.For(Domain);
        // Zero never consumes an immediate timer/tween, including a paused game clock.
        if (State != PlaybackState.Running || seconds == 0) return;
        AdvanceCore(seconds);
        CompletedThisAdvance = State == PlaybackState.Completed;
    }
    public void Pause() { CheckAccess(); if (State == PlaybackState.Running) State = PlaybackState.Paused; }
    public void Resume() { CheckAccess(); if (State == PlaybackState.Paused) State = PlaybackState.Running; }
    public void Cancel() { CheckAccess(); if (State is PlaybackState.Running or PlaybackState.Paused) State = PlaybackState.Cancelled; CompletedThisAdvance = false; ClearAdvanceResult(); }
    public void Restart() { CheckAccess(); ResetCore(); ClearAdvanceResult(); CompletedThisAdvance = false; State = PlaybackState.Running; }
    protected void Complete() => State = PlaybackState.Completed;
    protected abstract void AdvanceCore(double seconds);
    protected abstract void ResetCore();
    protected virtual void ClearAdvanceResult() { }
    internal void CheckAccess()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Timing operations belong to their creating thread.");
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }
    public void Dispose()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Timing operations belong to their creating thread.");
        if (IsDisposed) return;
        Cancel(); IsDisposed = true;
        // Last value/state remains readable, but cannot be restarted or advanced.
    }
}

/// <summary>Bounded lifetime ownership only. No global clock, scheduler, callbacks, or automatic advancement.</summary>
public sealed class TimingScope : IDisposable
{
    public const int MaximumOperations = 256;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<TimingOperation> _operations;
    private readonly int _capacity;
    private bool _disposed;
    public TimingScope(int capacity = MaximumOperations)
    {
        if (capacity is < 1 or > MaximumOperations) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity; _operations = new(capacity);
    }
    public T Own<T>(T operation) where T : TimingOperation
    {
        CheckAccess(); ArgumentNullException.ThrowIfNull(operation); operation.CheckAccess();
        if (operation.Owner is not null) throw new InvalidOperationException("A timing operation already has a lifetime owner.");
        // Reclaim explicitly disposed items at setup time, not on every frame.
        for (int i = _operations.Count - 1; i >= 0; i--)
            if (_operations[i].IsDisposed) { _operations[i].Owner = null; _operations.RemoveAt(i); }
        if (_operations.Count >= _capacity) throw new InvalidOperationException("Timing scope capacity reached; dispose finished work before adding more.");
        operation.Owner = this; _operations.Add(operation); return operation;
    }
    private void CheckAccess()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Timing scopes belong to their creating thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    public void Dispose()
    {
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Timing scopes belong to their creating thread.");
        if (_disposed) return;
        for (int i = _operations.Count - 1; i >= 0; i--) { _operations[i].Dispose(); _operations[i].Owner = null; }
        _operations.Clear(); _disposed = true;
    }
}

/// <summary>Polling timer. Repeating expirations coalesce into TicksDue; never one callback per missed tick.</summary>
public sealed class EngineTimer : TimingOperation
{
    private double _elapsed;
    public double DurationSeconds { get; }
    public bool Repeating { get; }
    public double RemainingSeconds => Math.Max(0, DurationSeconds - _elapsed);
    public ulong TicksDue { get; private set; }
    public EngineTimer(double seconds, bool repeating = false, ClockDomain domain = ClockDomain.Game) : base(domain)
    { TimingStep.ValidateDuration(seconds, !repeating); DurationSeconds = seconds; Repeating = repeating; }
    protected override void ClearAdvanceResult() => TicksDue = 0;
    protected override void ResetCore() => _elapsed = 0;
    protected override void AdvanceCore(double seconds)
    {
        double elapsed = _elapsed + seconds;
        if (!Repeating)
        {
            _elapsed = Math.Min(elapsed, DurationSeconds);
            if (_elapsed >= DurationSeconds) { TicksDue = 1; Complete(); }
        }
        else
        {
            TicksDue = (ulong)Math.Floor(elapsed / DurationSeconds);
            _elapsed = Math.Max(0, elapsed - TicksDue * DurationSeconds);
        }
    }
}
