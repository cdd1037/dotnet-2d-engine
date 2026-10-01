#include "audio_backend.h"
bool audio_dispatch(AudioBackend*&,AudioOp,const void*,void*,std::string&e){e="audio mixer module not enabled in this build";return false;}
void audio_destroy(AudioBackend*){}
