#ifndef GAL_AUDIO_H
#define GAL_AUDIO_H
#include "gal.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Optional SDL_mixer module. One lazily opened mixer per gal_context, main-thread
   calls outside sprite frames. No managed audio callbacks. Audio destruction
   stops voices before closing streams/clips/device. Handles never cross contexts.
   Offline output is interleaved float32 stereo at 48000 Hz, explicitly generated.
   Device mode runs asynchronously; offline mixing cannot read a device mixer. */
enum { GAL_AUDIO_VERSION=1, GAL_AUDIO_OFFLINE=1 };
enum { GAL_AUDIO_MASTER=0, GAL_AUDIO_MUSIC=1, GAL_AUDIO_SFX=2 };
enum { GAL_VOICE_PLAY=1, GAL_VOICE_PAUSE=2, GAL_VOICE_RESUME=3, GAL_VOICE_STOP=4 };
enum { GAL_VOICE_PLAYING=1, GAL_VOICE_PAUSED=2, GAL_VOICE_STREAM=4 };
typedef struct { uint32_t size,version,flags,reserved; } gal_audio_config;
/* Position is source sample frames (clips normalized to 48000 Hz; streams keep
   their source rate), not wall-clock time or output latency. */
typedef struct { uint32_t size,flags; int64_t position; uint32_t group,reserved; } gal_voice_state;
typedef struct { uint32_t size,flags,clips,voices; uint64_t decoded_bytes; int32_t sample_rate,channels; } gal_audio_state;
GAL_API int GAL_CALL gal_audio_open(gal_context*,const gal_audio_config*);
GAL_API int GAL_CALL gal_audio_close(gal_context*);
GAL_API int GAL_CALL gal_audio_load_clip(gal_context*,const char* utf8_path,uint64_t* clip);
/* Releasing a clip prevents new voices, but existing attached voices retain it. */
GAL_API int GAL_CALL gal_audio_release_clip(gal_context*,uint64_t clip);
GAL_API int GAL_CALL gal_audio_create_voice(gal_context*,uint64_t clip,uint32_t group,uint64_t* voice);
GAL_API int GAL_CALL gal_audio_open_stream(gal_context*,const char* utf8_path,uint32_t group,uint64_t* voice);
GAL_API int GAL_CALL gal_audio_release_voice(gal_context*,uint64_t voice);
/* Play always restarts. loops is extra repeats: -1 forever, 0 once, max 1000000.
   For other commands loops must be zero. Gain is finite [0,1]. */
GAL_API int GAL_CALL gal_audio_voice_command(gal_context*,uint64_t voice,uint32_t command,int32_t loops);
GAL_API int GAL_CALL gal_audio_voice_gain(gal_context*,uint64_t voice,float gain);
GAL_API int GAL_CALL gal_audio_group_gain(gal_context*,uint32_t group,float gain);
GAL_API int GAL_CALL gal_audio_get_voice(gal_context*,uint64_t voice,gal_voice_state*);
GAL_API int GAL_CALL gal_audio_get_state(gal_context*,gal_audio_state*);
/* frames 1..16384; caller supplies frames*2 floats. mixed_frames excludes trailing
   EOF silence; success still initializes the entire output. Not device latency. */
GAL_API int GAL_CALL gal_audio_mix(gal_context*,float* output,uint32_t frames,uint32_t* mixed_frames);
#ifdef __cplusplus
}
#endif
#endif
