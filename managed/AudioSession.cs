namespace GameAuthoringLab;
internal enum AudioGroup : uint { Master,Music,Sfx }

/// <summary>One explicit main-thread session per engine. Offline mixing is actual PCM, not a device simulation.</summary>
internal sealed unsafe class AudioSession : IDisposable
{
    private readonly EngineHost _engine;
    private bool _closed;
    public bool Offline { get; }
    internal nint Context { get { _engine.AssertAlive(); ObjectDisposedException.ThrowIf(_closed,this); return _engine.NativeContext; } }
    internal AudioSession(EngineHost engine,bool offline)
    {
        _engine=engine;Offline=offline;
        var config=new AudioConfig {Size=(uint)sizeof(AudioConfig),Version=1,Flags=offline?1u:0};
        Native.Check(AudioNative.Open(engine.NativeContext,&config),"open audio mixer");
    }
    public AudioClip LoadClip(AssetRoot assets,string logicalPath)
    {
        nint context=Context;string path=ResolveAudio(assets,logicalPath);ulong id=0;
        if(AudioNative.LoadClip(context,path,&id)!=0)throw new AssetException("ASSET_AUDIO",assets.DirectoryPath,logicalPath,Native.Error());
        return new AudioClip(this,id);
    }
    public AudioVoice CreateVoice(AudioClip clip,AudioGroup group=AudioGroup.Sfx)
    {
        nint context=Context;ArgumentNullException.ThrowIfNull(clip);ValidateGroup(group,false);
        if(!ReferenceEquals(clip.Session,this))throw new InvalidOperationException("Audio clip belongs to another session.");
        ulong id=0;Native.Check(AudioNative.CreateVoice(context,clip.Handle,group,&id),"create audio voice");return new AudioVoice(this,id);
    }
    public AudioVoice OpenStream(AssetRoot assets,string logicalPath,AudioGroup group=AudioGroup.Music)
    {
        nint context=Context;ValidateGroup(group,false);string path=ResolveAudio(assets,logicalPath);ulong id=0;
        if(AudioNative.OpenStream(context,path,group,&id)!=0)throw new AssetException("ASSET_AUDIO",assets.DirectoryPath,logicalPath,Native.Error());
        return new AudioVoice(this,id);
    }
    public AudioState State { get { var state=new AudioState {Size=(uint)sizeof(AudioState)};Native.Check(AudioNative.GetState(Context,&state),"audio state");return state; } }
    public void SetGain(AudioGroup group,float gain)
    { ValidateGroup(group,true);ValidateGain(gain);Native.Check(AudioNative.GroupGain(Context,group,gain),"audio group gain"); }
    public uint Mix(Span<float> stereoOutput)
    {
        if(!Offline)throw new InvalidOperationException("Device mixers cannot be read as offline PCM.");
        if(stereoOutput.Length is <2 or >32768 || stereoOutput.Length%2!=0)throw new ArgumentOutOfRangeException(nameof(stereoOutput),"Expected 1..16384 interleaved stereo frames.");
        uint mixed=0;fixed(float* output=stereoOutput)Native.Check(AudioNative.Mix(Context,output,(uint)stereoOutput.Length/2,&mixed),"mix offline PCM");return mixed;
    }
    internal void ReleaseClip(ulong id) { _engine.AssertThread();if(!_closed)Native.Check(AudioNative.ReleaseClip(Context,id),"release audio clip"); }
    internal void ReleaseVoice(ulong id) { _engine.AssertThread();if(!_closed)Native.Check(AudioNative.ReleaseVoice(Context,id),"release audio voice"); }
    internal void EngineDestroyed()=>_closed=true;
    public void Dispose()
    {
        _engine.AssertThread();if(_closed)return;
        Native.Check(AudioNative.Close(Context),"close audio mixer");_closed=true;_engine.AudioClosed(this);
    }
    internal static void ValidateGain(float gain)
    { if(!float.IsFinite(gain)||gain<0||gain>1)throw new ArgumentOutOfRangeException(nameof(gain),"Gain must be finite and in [0,1]."); }
    internal static void ValidateGroup(AudioGroup group,bool master)
    { if(group>AudioGroup.Sfx||(!master&&group==AudioGroup.Master))throw new ArgumentOutOfRangeException(nameof(group)); }
    internal static string ResolveAudio(AssetRoot assets,string logicalPath)
    {
        string path=assets.Resolve(logicalPath);
        if(!logicalPath.EndsWith(".wav",StringComparison.OrdinalIgnoreCase)&&!logicalPath.EndsWith(".ogg",StringComparison.OrdinalIgnoreCase))
            throw new AssetException("ASSET_AUDIO",assets.DirectoryPath,logicalPath,"Only WAV and Ogg/Vorbis are enabled.");
        return path;
    }
}
internal sealed class AudioClip(AudioSession session,ulong id) : IDisposable
{
    private bool _disposed;
    internal AudioSession Session=>session;
    internal ulong Handle { get { _=session.Context;ObjectDisposedException.ThrowIf(_disposed,this);return id; } }
    public void Dispose(){if(_disposed)return;session.ReleaseClip(id);_disposed=true;}
}
internal sealed unsafe class AudioVoice(AudioSession session,ulong id) : IDisposable
{
    private bool _disposed;
    private nint Context { get { ObjectDisposedException.ThrowIf(_disposed,this);return session.Context; } }
    public VoiceState State { get { var state=new VoiceState {Size=(uint)sizeof(VoiceState)};Native.Check(AudioNative.GetVoice(Context,id,&state),"audio voice state");return state; } }
    public void Play(int loops=0)
    { if(loops is <-1 or >1000000)throw new ArgumentOutOfRangeException(nameof(loops));Native.Check(AudioNative.Command(Context,id,1,loops),"play/restart audio voice"); }
    public void Pause()=>Native.Check(AudioNative.Command(Context,id,2,0),"pause audio voice");
    public void Resume()=>Native.Check(AudioNative.Command(Context,id,3,0),"resume audio voice");
    public void Stop()=>Native.Check(AudioNative.Command(Context,id,4,0),"stop audio voice");
    public void SetGain(float gain){AudioSession.ValidateGain(gain);Native.Check(AudioNative.VoiceGain(Context,id,gain),"audio voice gain");}
    public void Dispose(){if(_disposed)return;session.ReleaseVoice(id);_disposed=true;}
}

/// <summary>Explicit scene/example ownership. Dispose on unload: voices first, then clip ownership.</summary>
internal sealed class AudioScope(AudioSession session) : IDisposable
{
    private readonly List<AudioVoice> _voices=[];
    private readonly List<AudioClip> _clips=[];
    private bool _disposed;
    public AudioClip LoadClip(AssetRoot assets,string path)
    { ObjectDisposedException.ThrowIf(_disposed,this);var clip=session.LoadClip(assets,path);_clips.Add(clip);return clip; }
    public AudioVoice CreateVoice(AudioClip clip,AudioGroup group=AudioGroup.Sfx)
    { ObjectDisposedException.ThrowIf(_disposed,this);var voice=session.CreateVoice(clip,group);_voices.Add(voice);return voice; }
    public AudioVoice OpenStream(AssetRoot assets,string path,AudioGroup group=AudioGroup.Music)
    { ObjectDisposedException.ThrowIf(_disposed,this);var voice=session.OpenStream(assets,path,group);_voices.Add(voice);return voice; }
    public void Dispose()
    { if(_disposed)return;for(int i=_voices.Count-1;i>=0;i--)_voices[i].Dispose();for(int i=_clips.Count-1;i>=0;i--)_clips[i].Dispose();_voices.Clear();_clips.Clear();_disposed=true; }
}
