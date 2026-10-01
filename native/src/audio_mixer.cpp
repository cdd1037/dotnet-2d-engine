#include "audio_backend.h"
#include <SDL3/SDL.h>
#include <SDL3_mixer/SDL_mixer.h>
#include <array>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <memory>
#include <vector>
namespace {
constexpr size_t max_clips=64,max_voices=32,clip_limit=16*1024*1024,total_limit=64*1024*1024;
constexpr Sint64 file_limit=64*1024*1024;
uint64_t next_audio_id=1;
struct Clip { uint64_t id=0;MIX_Audio*audio=nullptr;size_t bytes=0;uint32_t voices=0;bool owned=false; };
struct Voice { uint64_t id=0,clip=0;MIX_Track*track=nullptr;SDL_IOStream*io=nullptr;uint32_t group=0;float gain=1; };
struct Properties { SDL_PropertiesID id=SDL_CreateProperties();~Properties(){SDL_DestroyProperties(id);} };
using Io=std::unique_ptr<SDL_IOStream,decltype(&SDL_CloseIO)>;
using Decoder=std::unique_ptr<MIX_AudioDecoder,decltype(&MIX_DestroyAudioDecoder)>;
using Track=std::unique_ptr<MIX_Track,decltype(&MIX_DestroyTrack)>;
bool error(std::string&e,const char*message=nullptr){e=message?message:SDL_GetError();return false;}
bool valid_gain(float gain){return std::isfinite(gain)&&gain>=0&&gain<=1;}
Io open_file(const char*path,std::string&e){
 Io io(nullptr,SDL_CloseIO);
 if(!path||!path[0]||std::strlen(path)>4096){error(e,"invalid audio path");return io;}
 io.reset(SDL_IOFromFile(path,"rb"));if(!io){error(e);return io;}
 const Sint64 bytes=SDL_GetIOSize(io.get());unsigned char header[12]{};
 if(bytes<12||bytes>file_limit||SDL_ReadIO(io.get(),header,sizeof(header))!=sizeof(header)
    ||(std::memcmp(header,"OggS",4)!=0&&(std::memcmp(header,"RIFF",4)!=0||std::memcmp(header+8,"WAVE",4)!=0))){error(e,"audio requires a WAV/Ogg file of 12 bytes..64 MiB");io.reset();return io;}
 if(SDL_SeekIO(io.get(),0,SDL_IO_SEEK_SET)<0){error(e);io.reset();}return io;
}
}
struct AudioBackend {
 MIX_Mixer*mixer=nullptr;bool initialized=false,audio_initialized=false,offline=false;
 SDL_AudioSpec spec{SDL_AUDIO_F32,2,48000};float groups[3]={1,1,1};
 std::array<Clip,max_clips>clips{};std::array<Voice,max_voices>voices{};size_t decoded=0;
 Clip*find_clip(uint64_t id){for(auto&c:clips)if(c.id==id&&id)return &c;return nullptr;}
 Voice*find_voice(uint64_t id){for(auto&v:voices)if(v.id==id&&id)return &v;return nullptr;}
 void collect(Clip&c){if(!c.owned&&!c.voices){decoded-=c.bytes;MIX_DestroyAudio(c.audio);c={};}}
 void release(Voice&v){MIX_DestroyTrack(v.track);if(v.io)SDL_CloseIO(v.io);if(auto*c=find_clip(v.clip)){c->voices--;collect(*c);}v={};}
};
void audio_destroy(AudioBackend*a){
 if(!a)return;
 for(auto&v:a->voices)if(v.id)a->release(v);
 for(auto&c:a->clips)if(c.id)MIX_DestroyAudio(c.audio);
 MIX_DestroyMixer(a->mixer);if(a->initialized)MIX_Quit();if(a->audio_initialized)SDL_QuitSubSystem(SDL_INIT_AUDIO);delete a;
}
static bool load_clip(AudioBackend&a,const char*path,uint64_t*out,std::string&e){
 if(!out)return error(e,"null clip output");
 *out=0;
 auto slot=std::find_if(a.clips.begin(),a.clips.end(),[](const Clip&c){return !c.id;});
 if(slot==a.clips.end()||!next_audio_id)return error(e,"audio clip capacity exhausted (64)");
 auto io=open_file(path,e);if(!io)return false;
 Properties props;if(!props.id||!SDL_SetBooleanProperty(props.id,MIX_PROP_AUDIO_LOAD_IGNORE_LOOPS_BOOLEAN,true))return error(e);
 Decoder decoder(MIX_CreateAudioDecoder_IO(io.get(),false,props.id),MIX_DestroyAudioDecoder);if(!decoder)return error(e);
 SDL_AudioSpec input{};if(!MIX_GetAudioDecoderFormat(decoder.get(),&input))return error(e);
 if(input.channels<1||input.channels>2||input.freq<8000||input.freq>192000)return error(e,"audio requires mono/stereo input at 8000..192000 Hz");
 std::vector<float>samples;std::array<float,4096>chunk{};SDL_AudioSpec decoded{SDL_AUDIO_F32,2,48000};
 for(;;){int n=MIX_DecodeAudio(decoder.get(),chunk.data(),sizeof(chunk),&decoded);if(n<0)return error(e);if(n==0)break;
  if(n%8||samples.size()*4+size_t(n)>clip_limit||a.decoded+samples.size()*4+size_t(n)>total_limit)return error(e,"decoded audio exceeds 16 MiB per clip or 64 MiB retained clip budget");
  const size_t floats=size_t(n)/4;for(size_t i=0;i<floats;i++)if(!std::isfinite(chunk[i]))return error(e,"nonfinite decoded audio");
  samples.insert(samples.end(),chunk.begin(),chunk.begin()+floats);
 }
 if(samples.empty())return error(e,"empty decoded audio");
 MIX_Audio*audio=MIX_LoadRawAudio(a.mixer,samples.data(),samples.size()*4,&decoded);if(!audio)return error(e);
 *slot={next_audio_id++,audio,samples.size()*4,0,true};a.decoded+=slot->bytes;*out=slot->id;return true;
}
static bool create_voice(AudioBackend&a,uint64_t clip,const char*path,uint32_t group,uint64_t*out,std::string&e){
 if(!out)return error(e,"null voice output");
 *out=0;
 if(group!=GAL_AUDIO_MUSIC&&group!=GAL_AUDIO_SFX)return error(e,"voice group must be music or SFX");
 auto slot=std::find_if(a.voices.begin(),a.voices.end(),[](const Voice&v){return !v.id;});
 if(slot==a.voices.end()||!next_audio_id)return error(e,"audio voice capacity exhausted (32)");
 Clip*c=nullptr;Io io(nullptr,SDL_CloseIO);
 if(path){
  io=open_file(path,e);if(!io)return false;
  // Probe the same open stream, then rewind it. No shared FILE/IO cursor across voices.
  {
   Decoder probe(MIX_CreateAudioDecoder_IO(io.get(),false,0),MIX_DestroyAudioDecoder);if(!probe)return error(e);
   SDL_AudioSpec format{};if(!MIX_GetAudioDecoderFormat(probe.get(),&format))return error(e);
   if(format.channels<1||format.channels>2||format.freq<8000||format.freq>192000)return error(e,"audio requires mono/stereo input at 8000..192000 Hz");
   auto metadata=MIX_GetAudioDecoderProperties(probe.get());
   if(SDL_GetBooleanProperty(metadata,MIX_PROP_METADATA_DURATION_INFINITE_BOOLEAN,false))return error(e,"stream metadata cannot request infinite duration; use explicit voice looping");
  }
  if(SDL_SeekIO(io.get(),0,SDL_IO_SEEK_SET)<0)return error(e);
 }
 else if(!(c=a.find_clip(clip))||!c->owned)return error(e,"stale or foreign clip handle");
 Track track(MIX_CreateTrack(a.mixer),MIX_DestroyTrack);if(!track)return error(e);
 if(!(io?MIX_SetTrackIOStream(track.get(),io.get(),false):MIX_SetTrackAudio(track.get(),c->audio)))return error(e);
 if(!MIX_SetTrackGain(track.get(),a.groups[group]))return error(e);
 *slot={next_audio_id++,clip,track.release(),io.release(),group,1};if(c)c->voices++;*out=slot->id;return true;
}
bool audio_dispatch(AudioBackend*&a,AudioOp op,const void*in,void*out,std::string&e){
 if(op==AudioOp::Open){
  const auto*cfg=static_cast<const gal_audio_config*>(in);
  if(!cfg||cfg->size!=sizeof(*cfg)||cfg->version!=GAL_AUDIO_VERSION||cfg->reserved||(cfg->flags&~GAL_AUDIO_OFFLINE))return error(e,"invalid audio config size/version/flags/reserved");
  if(a)return error(e,"audio mixer already open");
  if(MIX_Version()<3002004)return error(e,"SDL_mixer 3.2.4 or newer required");
  std::unique_ptr<AudioBackend,decltype(&audio_destroy)>candidate(new AudioBackend,audio_destroy);
  candidate->offline=(cfg->flags&GAL_AUDIO_OFFLINE)!=0;
  if(!candidate->offline){if(!SDL_InitSubSystem(SDL_INIT_AUDIO))return error(e);candidate->audio_initialized=true;}
  if(!MIX_Init())return error(e);
  candidate->initialized=true;
  candidate->mixer=candidate->offline?MIX_CreateMixer(&candidate->spec):MIX_CreateMixerDevice(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK,&candidate->spec);
  if(!candidate->mixer||!MIX_GetMixerFormat(candidate->mixer,&candidate->spec))return error(e);
  a=candidate.release();return true;
 }
 if(!a)return error(e,"audio mixer is not open");
 if(op==AudioOp::Close){audio_destroy(a);a=nullptr;return true;}
 if(op==AudioOp::LoadClip)return load_clip(*a,static_cast<const AudioFileRequest*>(in)->path,static_cast<uint64_t*>(out),e);
 if(op==AudioOp::CreateVoice){const auto&r=*static_cast<const AudioVoiceRequest*>(in);return create_voice(*a,r.id,nullptr,r.group,static_cast<uint64_t*>(out),e);}
 if(op==AudioOp::OpenStream){const auto&r=*static_cast<const AudioFileRequest*>(in);if(!r.path)return error(e,"null stream path");return create_voice(*a,0,r.path,r.group,static_cast<uint64_t*>(out),e);}
 if(op==AudioOp::ReleaseClip){auto*c=a->find_clip(*static_cast<const uint64_t*>(in));if(!c||!c->owned)return error(e,"stale or foreign clip handle");c->owned=false;a->collect(*c);return true;}
 if(op==AudioOp::State){auto*s=static_cast<gal_audio_state*>(out);if(!s||s->size!=sizeof(*s))return error(e,"invalid audio state size");
  *s={sizeof(*s),a->offline?uint32_t(GAL_AUDIO_OFFLINE):0,0,0,a->decoded,a->spec.freq,a->spec.channels};for(const auto&c:a->clips)if(c.id)s->clips++;for(const auto&v:a->voices)if(v.id)s->voices++;return true;}
 if(op==AudioOp::Mix){const auto&r=*static_cast<const AudioMix*>(in);auto*mixed=static_cast<uint32_t*>(out);
  if(!a->offline||!mixed||!r.output||r.frames<1||r.frames>16384)return error(e,"offline mix requires output for 1..16384 stereo frames and a frame-count pointer");
  const int bytes=MIX_Generate(a->mixer,r.output,int(r.frames*8));if(bytes<0)return error(e);*mixed=uint32_t(bytes/8);return true;}
 if(op==AudioOp::GroupGain){const auto&r=*static_cast<const AudioGain*>(in);if(r.id>2||!valid_gain(r.gain))return error(e,"invalid audio group or gain (0..1)");
  MIX_LockMixer(a->mixer);
  bool ok=true;if(r.id==0)ok=MIX_SetMixerGain(a->mixer,r.gain);else for(auto&v:a->voices)if(v.id&&v.group==r.id)ok=MIX_SetTrackGain(v.track,v.gain*r.gain)&&ok;
  if(ok)a->groups[r.id]=r.gain;
  MIX_UnlockMixer(a->mixer);return ok||error(e);}
 uint64_t id=0;if(op==AudioOp::Command)id=static_cast<const AudioCommand*>(in)->id;else if(op==AudioOp::VoiceGain)id=static_cast<const AudioGain*>(in)->id;else id=*static_cast<const uint64_t*>(in);
 auto*v=a->find_voice(id);if(!v)return error(e,"stale or foreign voice handle");
 if(op==AudioOp::ReleaseVoice){a->release(*v);return true;}
 if(op==AudioOp::VoiceState){auto*s=static_cast<gal_voice_state*>(out);if(!s||s->size!=sizeof(*s)||s->reserved)return error(e,"invalid voice state size/reserved");
  MIX_LockMixer(a->mixer);*s={sizeof(*s),(MIX_TrackPlaying(v->track)?uint32_t(GAL_VOICE_PLAYING):0)|(MIX_TrackPaused(v->track)?uint32_t(GAL_VOICE_PAUSED):0)|(v->io?uint32_t(GAL_VOICE_STREAM):0),MIX_GetTrackPlaybackPosition(v->track),v->group,0};MIX_UnlockMixer(a->mixer);return true;}
 if(op==AudioOp::VoiceGain){const auto&r=*static_cast<const AudioGain*>(in);if(!valid_gain(r.gain))return error(e,"voice gain must be finite 0..1");if(!MIX_SetTrackGain(v->track,r.gain*a->groups[v->group]))return error(e);v->gain=r.gain;return true;}
 if(op==AudioOp::Command){const auto&r=*static_cast<const AudioCommand*>(in);if(r.command<GAL_VOICE_PLAY||r.command>GAL_VOICE_STOP||r.loops<-1||r.loops>1000000||(r.command!=GAL_VOICE_PLAY&&r.loops))return error(e,"invalid voice command or loop count");
  if(r.command==GAL_VOICE_PLAY){Properties props;if(!props.id||!SDL_SetNumberProperty(props.id,MIX_PROP_PLAY_LOOPS_NUMBER,r.loops))return error(e);return MIX_PlayTrack(v->track,props.id)||error(e);}
  if(r.command==GAL_VOICE_PAUSE)return MIX_PauseTrack(v->track)||error(e);
  if(r.command==GAL_VOICE_RESUME)return MIX_ResumeTrack(v->track)||error(e);
  return MIX_StopTrack(v->track,0)||error(e);
 }
 return error(e,"unsupported audio operation");
}
