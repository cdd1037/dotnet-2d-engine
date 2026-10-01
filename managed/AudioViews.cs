namespace GameAuthoringLab;

/// <summary>A copied mixer snapshot, independent of the session's subsequent playback or lifetime.</summary>
public readonly record struct AudioSessionState
{
    public bool Offline { get; }
    /// <summary>Resident clips, including clips retained by voices after caller ownership is released.</summary>
    public uint Clips { get; }
    /// <summary>Resident voices, including stopped and paused voices.</summary>
    public uint Voices { get; }
    public ulong DecodedBytes { get; }
    public int SampleRate { get; }
    public int Channels { get; }

    internal AudioSessionState(AudioState state)
    {
        Offline=state.Offline;Clips=state.Clips;Voices=state.Voices;
        DecodedBytes=state.DecodedBytes;SampleRate=state.SampleRate;Channels=state.Channels;
    }
}

/// <summary>A copied voice snapshot, independent of subsequent playback or disposal.</summary>
public readonly record struct AudioVoiceState
{
    /// <summary>Whether the voice is playing; paused voices are reported separately.</summary>
    public bool Playing { get; }
    public bool Paused { get; }
    public bool Streaming { get; }
    /// <summary>Source sample frames: 48 kHz for decoded clips, or the original sample rate for streams.</summary>
    public long Position { get; }
    public AudioGroup Group { get; }

    internal AudioVoiceState(VoiceState state)
    {
        Playing=state.Playing;Paused=state.Paused;Streaming=state.Streaming;
        Position=state.Position;Group=state.Group;
    }
}
