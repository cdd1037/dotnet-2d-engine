using System.Runtime.InteropServices;
namespace GameAuthoringLab;
internal static unsafe class AudioTests
{
    public static int RunContracts()
    {
        int n=0;void Check(bool ok,string label){if(!ok)throw new Exception("AUDIO: "+label);n++;}
        void Reject<T>(Action action) where T:Exception {try{action();}catch(T){n++;return;}throw new Exception("AUDIO expected rejection");}
        Check(sizeof(AudioConfig)==16&&sizeof(VoiceState)==24&&sizeof(AudioState)==32&&Marshal.OffsetOf<AudioState>(nameof(AudioState.DecodedBytes)).ToInt32()==16,"C ABI layouts");
        foreach(float bad in new[]{float.NaN,float.PositiveInfinity,-.01f,1.01f})Reject<ArgumentOutOfRangeException>(()=>AudioSession.ValidateGain(bad));
        Reject<ArgumentOutOfRangeException>(()=>AudioSession.ValidateGroup((AudioGroup)3,true));
        Reject<ArgumentOutOfRangeException>(()=>AudioSession.ValidateGroup(AudioGroup.Master,false));
        var assets=new AssetRoot();Check(File.Exists(AudioSession.ResolveAudio(assets,"audio/pcm.wav")),"asset-root WAV path");
        Reject<AssetException>(()=>AudioSession.ResolveAudio(assets,"../secret.wav"));
        Reject<AssetException>(()=>AudioSession.ResolveAudio(assets,"regions.bmp"));
        Console.WriteLine($"AUDIO CONTRACT PASS assertions={n}; CPU validation only");return n;
    }
    public static int RunOffline()
    {
        int n=0;void Check(bool ok,string label){if(!ok)throw new Exception("AUDIO PCM: "+label);n++;}
        void Reject<T>(Action action,string label) where T:Exception {try{action();}catch(T){n++;return;}throw new Exception("AUDIO accepted: "+label);}
        var root=new AssetRoot();var buffer=new float[512];
        using var engine=new EngineHost(true,16);using var audio=engine.OpenAudio(true);
        Check(audio.State is {Offline:true,SampleRate:48000,Channels:2,Clips:0,Voices:0},"real offline mixer format");
        var clip=audio.LoadClip(root,"audio/pcm.wav");ulong staleClip=clip.Handle;
        using var voice=audio.CreateVoice(clip);Check(!voice.State.Playing,"new voice starts stopped");
        Check(audio.State is {Clips:1,Voices:1,DecodedBytes:16384},"bounded retained PCM accounting");
        bool Samples(float left,float right)=>buffer.Where((v,i)=>Math.Abs(v-(i%2==0?left:right))>1e-5f).Any()==false;
        Array.Fill(buffer,float.NaN);Check(audio.Mix(buffer)==0&&Samples(0,0),"idle fills all output with silence");
        voice.Play();Check(audio.Mix(buffer)==256&&Samples(.25f,-.25f),"decoded WAV samples mixed exactly");
        long position=voice.State.Position;voice.Pause();Check(voice.State.Paused,"pause state");
        Check(audio.Mix(buffer)==0&&Samples(0,0)&&voice.State.Position==position,"pause is silence without advancing");
        voice.Resume();Check(audio.Mix(buffer)==256&&Samples(.25f,-.25f)&&voice.State.Position>position,"resume continues position");
        voice.SetGain(.5f);audio.SetGain(AudioGroup.Sfx,.5f);audio.SetGain(AudioGroup.Master,.5f);
        voice.Play(-1);audio.Mix(buffer);Check(Samples(.03125f,-.03125f),"voice x group x master gain");
        audio.SetGain(AudioGroup.Music,0);audio.Mix(buffer);Check(Samples(.03125f,-.03125f),"music gain does not mute SFX");
        audio.SetGain(AudioGroup.Sfx,0);audio.Mix(buffer);Check(Samples(0,0)&&voice.State.Playing,"mute does not stop voice");
        voice.Stop();Check(!voice.State.Playing&&!voice.State.Paused,"stop clears playing/paused");
        audio.SetGain(AudioGroup.Sfx,1);audio.SetGain(AudioGroup.Master,1);voice.SetGain(1);
        voice.Play(1);var full=new float[2048*2*3];Check(audio.Mix(full)==4096,"one extra loop produces two complete clips");
        Check(full.Take(8192).Where((v,i)=>Math.Abs(v-(i%2==0?.25f:-.25f))>1e-5f).Any()==false&&full.Skip(8192).All(v=>v==0),"finite loop tail padded with silence");
        Check(!voice.State.Playing,"finite loops reach stopped state");
        voice.Play(-1);Check(audio.Mix(full)==6144&&voice.State.Playing,"infinite loop advances across multiple clip durations");voice.Stop();
        clip.Dispose();Check(audio.State.Clips==1,"disposed owner keeps attached clip resident");
        voice.Play();audio.Mix(buffer);Check(Samples(.25f,-.25f),"attached voice can replay after clip-owner disposal");voice.Stop();
        Reject<ObjectDisposedException>(()=>audio.CreateVoice(clip),"disposed clip cannot create voice");
        voice.Dispose();Check(audio.State is {Clips:0,Voices:0,DecodedBytes:0},"final voice releases retained clip PCM");
        using(var scope=new AudioScope(audio))
        {
            var ogg=scope.LoadClip(root,"audio/music.ogg");var decoded=scope.CreateVoice(ogg,AudioGroup.Music);
            audio.SetGain(AudioGroup.Music,1);decoded.Play();audio.Mix(full);
            Check(full.All(float.IsFinite)&&full.Any(v=>Math.Abs(v)>.01f),"bundled Vorbis decoded to actual PCM");decoded.Stop();
            var stream=scope.OpenStream(root,"audio/music.ogg");stream.Play(-1);audio.Mix(full);
            Check(stream.State.Streaming&&stream.State.Position>0&&full.Any(v=>Math.Abs(v)>.01f),"seekable Ogg file stream actually mixed");
            stream.Pause();long at=stream.State.Position;audio.Mix(buffer);Check(Samples(0,0)&&stream.State.Position==at,"stream pause preserves cursor");
            stream.Resume();audio.Mix(buffer);Check(stream.State.Position>at,"stream resumes");stream.Stop();stream.Play();audio.Mix(buffer);Check(stream.State.Position<at,"stream replay seeks to start");
        }
        Check(audio.State is {Clips:0,Voices:0,DecodedBytes:0},"scene scope stops voices and closes streams before releasing clips");
        using(var persistentClip=audio.LoadClip(root,"audio/pcm.wav"))using(var persistentVoice=audio.CreateVoice(persistentClip))
        {
            persistentVoice.Play(-1);var world=new World();
            for(int i=0;i<3;i++)
            {
                var scene=world.CreateScene("audio scene");var owner=world.Create("audio owner",scene);var scope=new AudioScope(audio);
                world.AttachBehavior(owner,new AudioOwnerBehavior(),lifetime=>lifetime.OnDetach(scope.Dispose));
                var cue=scope.CreateVoice(scope.LoadClip(root,"audio/cue.wav"));cue.Play(-1);
                world.UnloadScene(scene);
                Check(audio.State is {Clips:1,Voices:1}&&persistentVoice.State.Playing,"scene unload releases only its audio scope, preserving independent voice");
                audio.Mix(buffer);Check(Samples(.25f,-.25f),"scene unload leaves no stale audible voice");
            }
        }
        Reject<InvalidOperationException>(()=>engine.OpenAudio(true),"one session per context");
        Reject<AssetException>(()=>audio.LoadClip(root,"audio/missing.wav"),"missing source");
        Reject<ArgumentOutOfRangeException>(()=>audio.Mix(new float[3]),"partial stereo frame");
        Reject<ArgumentOutOfRangeException>(()=>audio.Mix(new float[32770]),"oversized offline batch");
        using(var c=audio.LoadClip(root,"audio/pcm.wav"))
        {
            var voices=new List<AudioVoice>();for(int i=0;i<32;i++)voices.Add(audio.CreateVoice(c));
            Reject<InvalidOperationException>(()=>audio.CreateVoice(c),"bounded 32 voices");
            foreach(var v in voices)v.Dispose();Check(audio.State.Voices==0,"voice slots reusable after capacity rejection");
        }
        var clips=new List<AudioClip>();for(int i=0;i<64;i++)clips.Add(audio.LoadClip(root,"audio/pcm.wav"));
        Reject<AssetException>(()=>audio.LoadClip(root,"audio/pcm.wav"),"bounded 64 resident clips");foreach(var c in clips)c.Dispose();
        Check(audio.State.Clips==0,"clip slots reusable after capacity rejection");
        string temporary=Path.Combine(Path.GetTempPath(),"gal-audio-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
        try
        {
            var local=new AssetRoot(temporary);File.WriteAllText(Path.Combine(temporary,"bad.wav"),"malformed audio input");
            Reject<AssetException>(()=>audio.LoadClip(local,"bad.wav"),"malformed decode preserves mixer");
            Reject<AssetException>(()=>audio.OpenStream(local,"bad.wav"),"malformed stream preserves mixer");
            using(var file=File.Create(Path.Combine(temporary,"too-large.wav")))file.SetLength(64*1024*1024+1);
            Reject<AssetException>(()=>audio.LoadClip(local,"too-large.wav"),"encoded byte limit");
            // Valid PCM WAV whose float-stereo decode exceeds the 16 MiB clip cap.
            using(var file=File.Create(Path.Combine(temporary,"decoded-limit.wav")))
            using(var writer=new BinaryWriter(file))
            {
                const int bytes=9*1024*1024;
                writer.Write("RIFF"u8);writer.Write(36+bytes);writer.Write("WAVEfmt "u8);writer.Write(16);
                writer.Write((short)1);writer.Write((short)2);writer.Write(48000);writer.Write(192000);writer.Write((short)4);writer.Write((short)16);
                writer.Write("data"u8);writer.Write(bytes);file.SetLength(44L+bytes);
            }
            Reject<AssetException>(()=>audio.LoadClip(local,"decoded-limit.wav"),"bounded incremental PCM decode");
            using(var c=audio.LoadClip(root,"audio/pcm.wav"))using(var v=audio.CreateVoice(c)){v.Play();audio.Mix(buffer);Check(Samples(.25f,-.25f),"failure leaves prior mixer usable");}
            if(OperatingSystem.IsLinux())
            {
                File.CreateSymbolicLink(Path.Combine(temporary,"linked.wav"),root.Resolve("audio/pcm.wav"));
                Reject<AssetException>(()=>audio.OpenStream(local,"linked.wav"),"stream resolver rejects descendant links");
            }
            // An open stream owns its file descriptor, not a future path lookup.
            File.Copy(root.Resolve("audio/pcm.wav"),Path.Combine(temporary,"retained.wav"));
            using(var v=audio.OpenStream(local,"retained.wav",AudioGroup.Sfx))
            {if(OperatingSystem.IsLinux())File.Move(Path.Combine(temporary,"retained.wav"),Path.Combine(temporary,"renamed.wav"));v.Play();audio.Mix(buffer);Check(Samples(.25f,-.25f),"open stream retains independent cursor (rename tested on Linux)");}
        }
        finally{Directory.Delete(temporary,true);}
        var raw=new AudioConfig {Size=16,Version=99,Flags=1};Check(AudioNative.Open(engine.NativeContext,&raw)!=0,"native bad version rejected");
        ulong invalid=999;Check(AudioNative.CreateVoice(engine.NativeContext,staleClip,AudioGroup.Sfx,&invalid)!=0&&invalid==0,"stale clip rejected and output reset");
        using(var nativeClip=audio.LoadClip(root,"audio/pcm.wav"))
        {
            ulong nativeVoice=0;Native.Check(AudioNative.CreateVoice(engine.NativeContext,nativeClip.Handle,AudioGroup.Sfx,&nativeVoice),"raw test voice");
            Check(AudioNative.Command(engine.NativeContext,nativeVoice,1,-2)!=0&&AudioNative.Command(engine.NativeContext,nativeVoice,2,1)!=0,"native loop/command rejection");
            Check(AudioNative.VoiceGain(engine.NativeContext,nativeVoice,float.PositiveInfinity)!=0,"native nonfinite gain rejection");
            var malformed=new VoiceState{Size=24,Reserved=1};Check(AudioNative.GetVoice(engine.NativeContext,nativeVoice,&malformed)!=0,"native voice state reserved rejection");
            Native.Check(AudioNative.ReleaseVoice(engine.NativeContext,nativeVoice),"raw voice cleanup");
            Check(AudioNative.Command(engine.NativeContext,nativeVoice,1,0)!=0&&AudioNative.ReleaseVoice(engine.NativeContext,nativeVoice)!=0,"stale voice cannot play/release");
        }
        Check(AudioNative.GroupGain(engine.NativeContext,(AudioGroup)3,1)!=0&&AudioNative.GroupGain(engine.NativeContext,AudioGroup.Master,float.NaN)!=0,"native gain validation");
        var cam=new Camera{Zoom=1};Native.Check(Native.Begin(engine.NativeContext,&cam),"begin audio guard");
        Check(AudioNative.Close(engine.NativeContext)!=0,"audio cannot close during sprite frame");Native.Check(Native.Abort(engine.NativeContext),"abort audio guard");
        Exception? wrongThread=null;var thread=new Thread(()=>{try{audio.SetGain(AudioGroup.Master,1);}catch(Exception e){wrongThread=e;}});thread.Start();thread.Join();Check(wrongThread is InvalidOperationException,"audio main-thread ownership");
        using(var c=audio.LoadClip(root,"audio/pcm.wav"))using(var v=audio.CreateVoice(c))
        {
            v.Play(-1);for(int i=0;i<128;i++)audio.Mix(buffer);long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<1000;i++){audio.Mix(buffer);_ =v.State;}
            Check(GC.GetAllocatedBytesForCurrentThread()==before,"warmed offline mix/state managed allocations zero");
        }
        var lateClip=audio.LoadClip(root,"audio/pcm.wav");var lateVoice=audio.CreateVoice(lateClip);lateVoice.Play(-1);
        audio.Dispose();Reject<ObjectDisposedException>(()=>lateVoice.Play(),"session teardown invalidates outstanding voices");lateClip.Dispose();lateVoice.Dispose();
        using(var reopened=engine.OpenAudio(true))
        {
            Check(reopened.State.Clips==0&&reopened.State.Voices==0,"same-context reopen has no stale resources");
            var finalClip=reopened.LoadClip(root,"audio/pcm.wav");var finalVoice=reopened.CreateVoice(finalClip);finalVoice.Play(-1);
            engine.Dispose();finalVoice.Dispose();finalClip.Dispose();Check(true,"engine destruction releases mixer before late managed disposal");
        }
        using(var nextEngine=new EngineHost(true,1))using(var nextAudio=nextEngine.OpenAudio(true))
        {
            using var nextClip=nextAudio.LoadClip(root,"audio/pcm.wav");ulong foreign=0;
            Check(nextClip.Handle!=staleClip&&AudioNative.CreateVoice(nextEngine.NativeContext,staleClip,AudioGroup.Sfx,&foreign)!=0,"clip handles cannot cross destroyed contexts");
        }
        Console.WriteLine($"AUDIO OFFLINE PASS assertions={n}; actual SDL_mixer PCM, no physical output or latency measurement");return n;
    }
    private sealed class AudioOwnerBehavior : IBehavior { public void Update(Entity entity,float deltaSeconds){} }
    public static int RunDevice()
    {
        using var engine=new EngineHost(true,1);using var audio=engine.OpenAudio();using var scope=new AudioScope(audio);
        var clip=scope.LoadClip(new AssetRoot(),"audio/cue.wav");var voice=scope.CreateVoice(clip);voice.Play(-1);
        for(int i=0;i<100&&voice.State.Position==0;i++)Thread.Sleep(10);
        if(audio.State.Offline||!voice.State.Playing||voice.State.Position<=0)throw new Exception("Audio device mixer did not advance");
        voice.Pause();voice.Resume();voice.Stop();scope.Dispose();if(audio.State.Clips!=0||audio.State.Voices!=0)throw new Exception("Audio device cleanup failed");
        Console.WriteLine($"AUDIO DEVICE PASS driver={Environment.GetEnvironmentVariable("SDL_AUDIODRIVER")??"default"}; asynchronous track advanced, playback controls/cleanup passed; audibility not verified");return 0;
    }
}
