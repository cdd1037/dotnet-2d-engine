#pragma once
#include "gal_audio.h"
#include <string>
struct AudioBackend;
enum class AudioOp { Open,Close,LoadClip,ReleaseClip,CreateVoice,OpenStream,ReleaseVoice,Command,VoiceGain,GroupGain,VoiceState,State,Mix };
struct AudioFileRequest { const char* path; uint32_t group; };
struct AudioVoiceRequest { uint64_t id; uint32_t group; };
struct AudioCommand { uint64_t id; uint32_t command; int32_t loops; };
struct AudioGain { uint64_t id; float gain; };
struct AudioMix { float* output; uint32_t frames; };
bool audio_dispatch(AudioBackend*&,AudioOp,const void*,void*,std::string&);
void audio_destroy(AudioBackend*);
