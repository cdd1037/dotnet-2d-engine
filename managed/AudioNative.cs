using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace GameAuthoringLab;
[StructLayout(LayoutKind.Sequential)]
internal struct AudioConfig { public uint Size,Version,Flags,Reserved; }
[StructLayout(LayoutKind.Sequential)]
internal struct VoiceState
{
    public uint Size,Flags;
    public long Position;
    public AudioGroup Group;
    public uint Reserved;
    public readonly bool Playing => (Flags&1)!=0;
    public readonly bool Paused => (Flags&2)!=0;
    public readonly bool Streaming => (Flags&4)!=0;
}
[StructLayout(LayoutKind.Sequential)]
internal struct AudioState
{
    public uint Size,Flags,Clips,Voices;
    public ulong DecodedBytes;
    public int SampleRate,Channels;
    public readonly bool Offline => (Flags&1)!=0;
}
internal static unsafe partial class AudioNative
{
    [LibraryImport("gal",EntryPoint="gal_audio_open")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    public static partial int Open(nint context,AudioConfig* config);
    [LibraryImport("gal",EntryPoint="gal_audio_close")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    public static partial int Close(nint context);
    [LibraryImport("gal",EntryPoint="gal_audio_load_clip",StringMarshalling=StringMarshalling.Utf8)][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    public static partial int LoadClip(nint context,string path,ulong* clip);
    [LibraryImport("gal",EntryPoint="gal_audio_release_clip")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    public static partial int ReleaseClip(nint context,ulong clip);
    [LibraryImport("gal",EntryPoint="gal_audio_create_voice")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    public static partial int CreateVoice(nint context,ulong clip,AudioGroup group,ulong* voice);
    [LibraryImport("gal",EntryPoint="gal_audio_open_stream",StringMarshalling=StringMarshalling.Utf8)][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    public static partial int OpenStream(nint context,string path,AudioGroup group,ulong* voice);
    [LibraryImport("gal",EntryPoint="gal_audio_release_voice")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    public static partial int ReleaseVoice(nint context,ulong voice);
    [LibraryImport("gal",EntryPoint="gal_audio_voice_command")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    public static partial int Command(nint context,ulong voice,uint command,int loops);
    [LibraryImport("gal",EntryPoint="gal_audio_voice_gain")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    public static partial int VoiceGain(nint context,ulong voice,float gain);
    [LibraryImport("gal",EntryPoint="gal_audio_group_gain")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    public static partial int GroupGain(nint context,AudioGroup group,float gain);
    [LibraryImport("gal",EntryPoint="gal_audio_get_voice")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    public static partial int GetVoice(nint context,ulong voice,VoiceState* state);
    [LibraryImport("gal",EntryPoint="gal_audio_get_state")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    public static partial int GetState(nint context,AudioState* state);
    [LibraryImport("gal",EntryPoint="gal_audio_mix")][UnmanagedCallConv(CallConvs=[typeof(CallConvCdecl)])]
    public static partial int Mix(nint context,float* output,uint frames,uint* mixedFrames);
}
