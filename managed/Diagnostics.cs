using System.Diagnostics;
namespace GameAuthoringLab;

public enum DiagnosticLevel { Off, Trace, Info, Warning, Error }

/// <summary>TimestampTicks is zero unless explicitly requested; its clock is Stopwatch, not UTC.</summary>
public readonly record struct DiagnosticEntry(ulong Sequence, DiagnosticLevel Level, uint Code,
    string Category, string Message, long Frame, double Value, long TimestampTicks);

/// <summary>A single-thread, fixed-capacity FIFO. A full queue drops new entries, preserving unread evidence.</summary>
public sealed class DiagnosticLog
{
    private readonly DiagnosticEntry[] _entries;
    private readonly Func<long>? _clock;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private int _head, _count;
    private DiagnosticLevel _minimum;
    public int Capacity => _entries.Length;
    public int Count => _count;
    public ulong Written { get; private set; }
    public ulong Dropped { get; private set; }
    public bool IncludeTimestamp { get; }
    public DiagnosticLevel MinimumLevel
    {
        get => _minimum;
        set { CheckThread(); if(value is < DiagnosticLevel.Off or > DiagnosticLevel.Error)throw new ArgumentOutOfRangeException(nameof(value)); _minimum=value; }
    }
    public DiagnosticLog(int capacity=128, bool includeTimestamp=false) : this(capacity,includeTimestamp,null) { }
    internal DiagnosticLog(int capacity,bool includeTimestamp,Func<long>? clock)
    {
        if(capacity is < 1 or > 65536)throw new ArgumentOutOfRangeException(nameof(capacity));
        _entries=new DiagnosticEntry[capacity]; IncludeTimestamp=includeTimestamp; _clock=clock;
    }
    public bool IsEnabled(DiagnosticLevel level) => _minimum!=DiagnosticLevel.Off && level>=_minimum && level<=DiagnosticLevel.Error;
    /// <summary>Filtered calls skip payload validation. Reuse strings, or call IsEnabled before formatting.</summary>
    public bool TryWrite(DiagnosticLevel level,uint code,string category,string message,long frame=0,double value=0)
    {
        CheckThread();
        if(level is < DiagnosticLevel.Trace or > DiagnosticLevel.Error)throw new ArgumentOutOfRangeException(nameof(level));
        if(!IsEnabled(level))return false;
        ArgumentNullException.ThrowIfNull(category); ArgumentNullException.ThrowIfNull(message);
        if(category.Length is < 1 or > 48)throw new ArgumentException("Log category must contain 1..48 ASCII identifier characters.",nameof(category));
        foreach(char c in category)if(!(char.IsAsciiLetterOrDigit(c)||c is '.' or '_' or '-'))throw new ArgumentException("Invalid log category character.",nameof(category));
        if(message.Length>1024)throw new ArgumentException("Log message exceeds 1024 UTF-16 code units.",nameof(message));
        if(code==0)throw new ArgumentOutOfRangeException(nameof(code));
        if(frame<0)throw new ArgumentOutOfRangeException(nameof(frame));
        if(!double.IsFinite(value))throw new ArgumentOutOfRangeException(nameof(value));
        if(_count==Capacity){Dropped=Increment(Dropped);return false;}
        long timestamp=IncludeTimestamp?(_clock?.Invoke()??Stopwatch.GetTimestamp()):0;
        Written=Increment(Written);
        _entries[(_head+_count)%Capacity]=new(Written,level,code,category,message,frame,value,timestamp); _count++;
        return true;
    }
    public bool TryRead(out DiagnosticEntry entry)
    {
        CheckThread();
        if(_count==0){entry=default;return false;}
        entry=_entries[_head];_entries[_head]=default;_head=(_head+1)%Capacity;_count--;return true;
    }
    /// <summary>Release queued string references. Lifetime counters are preserved.</summary>
    public void Clear(){CheckThread();Array.Clear(_entries);_head=0;_count=0;}
    private void CheckThread(){if(Environment.CurrentManagedThreadId!=_thread)throw new InvalidOperationException("DiagnosticLog is single-thread owned.");}
    internal static ulong Increment(ulong value)=>value==ulong.MaxValue?value:value+1;
}

public enum CpuPhase { Input, Update, Extraction, Render, Other }

/// <summary>CPU elapsed wall time includes waits within the measured scope; it is not GPU execution time.</summary>
public readonly record struct CpuTimingSample(ulong Count,double LastSeconds,double MinimumSeconds,double MaximumSeconds,double TotalSeconds)
{
    public double AverageSeconds => Count==0?0:TotalSeconds/Count;
    internal CpuTimingSample Add(double seconds)=>new(DiagnosticLog.Increment(Count),seconds,
        Count==0?seconds:Math.Min(MinimumSeconds,seconds),Math.Max(MaximumSeconds,seconds),TotalSeconds+seconds);
}

/// <summary>Explicit, non-nested phase scopes. No clocks or allocations on the disabled path.</summary>
public sealed class CpuTimings
{
    private readonly CpuTimingSample[] _phases=new CpuTimingSample[5];
    private readonly Func<long>? _clock;
    private readonly double _frequency;
    private readonly int _thread=Environment.CurrentManagedThreadId;
    private bool _enabled,_active;
    private long _frameStart,_phaseStart;
    private ulong _ticket,_activeTicket;
    private CpuPhase _phase;
    public bool Enabled
    {
        get=>_enabled;
        set{CheckThread();if(_active)throw new InvalidOperationException("Finish or abort the active timing frame before changing Enabled.");_enabled=value;}
    }
    public bool FrameActive=>_active;
    public CpuTimingSample Frames { get; private set; }
    public ulong AbortedFrames { get; private set; }
    public CpuTimings() : this(null,Stopwatch.Frequency) { }
    internal CpuTimings(Func<long>? clock,double frequency)
    {
        if(!double.IsFinite(frequency)||frequency<=0)throw new ArgumentOutOfRangeException(nameof(frequency));
        _clock=clock;_frequency=frequency;
    }
    public CpuTimingSample GetPhase(CpuPhase phase){CheckThread();ValidatePhase(phase);return _phases[(int)phase];}
    public void BeginFrame()
    {
        CheckThread();if(!_enabled)return;
        if(_active)throw new InvalidOperationException("A CPU timing frame is already active.");
        _frameStart=Now();_active=true;
    }
    public CpuPhaseScope Measure(CpuPhase phase)
    {
        CheckThread();if(!_enabled)return default;
        ValidatePhase(phase);
        if(!_active)throw new InvalidOperationException("Begin a timing frame before measuring phases.");
        if(_activeTicket!=0)throw new InvalidOperationException("CPU timing phases cannot overlap or nest.");
        if(_ticket==ulong.MaxValue)throw new InvalidOperationException("CPU timing ticket space exhausted.");
        _phaseStart=Now();_phase=phase;_activeTicket=++_ticket;
        return new(this,_activeTicket);
    }
    internal void FinishPhase(ulong ticket)
    {
        CheckThread();if(ticket!=_activeTicket||ticket==0)return; // copied/stale scopes cannot finish newer work
        double elapsed=Elapsed(_phaseStart,Now());_phases[(int)_phase]=_phases[(int)_phase].Add(elapsed);_activeTicket=0;
    }
    public void EndFrame()
    {
        CheckThread();if(!_enabled)return;
        if(!_active)throw new InvalidOperationException("No CPU timing frame is active.");
        if(_activeTicket!=0)throw new InvalidOperationException("Dispose the phase scope before ending its frame.");
        double elapsed=Elapsed(_frameStart,Now());Frames=Frames.Add(elapsed);_active=false;
    }
    /// <summary>Discard an unfinished frame/phase. Earlier completed phase samples remain visible.</summary>
    public void AbortFrame(){CheckThread();if(!_active)return;_active=false;_activeTicket=0;AbortedFrames=DiagnosticLog.Increment(AbortedFrames);}
    public void Reset()
    {
        CheckThread();if(_active)throw new InvalidOperationException("Cannot reset active CPU timings.");
        Array.Clear(_phases);Frames=default;AbortedFrames=0; // tickets never rewind
    }
    private long Now()=>_clock?.Invoke()??Stopwatch.GetTimestamp();
    private double Elapsed(long start,long end)
    {
        if(end<start)throw new InvalidOperationException("CPU timing clock moved backwards; abort the frame.");
        double seconds=(double)((Int128)end-start)/_frequency;
        if(!double.IsFinite(seconds))throw new InvalidOperationException("CPU timing duration overflowed; abort the frame.");
        return seconds;
    }
    private static void ValidatePhase(CpuPhase phase){if(phase is < CpuPhase.Input or > CpuPhase.Other)throw new ArgumentOutOfRangeException(nameof(phase));}
    private void CheckThread(){if(Environment.CurrentManagedThreadId!=_thread)throw new InvalidOperationException("CpuTimings is single-thread owned.");}
}

/// <summary>Keep this value on the stack; interface boxing would allocate. Repeated Dispose is harmless.</summary>
public readonly struct CpuPhaseScope : IDisposable
{
    private readonly CpuTimings? _owner;
    private readonly ulong _ticket;
    internal CpuPhaseScope(CpuTimings owner,ulong ticket){_owner=owner;_ticket=ticket;}
    public void Dispose()=>_owner?.FinishPhase(_ticket);
}
