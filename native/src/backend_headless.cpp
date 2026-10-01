#include "backend.h"
Backend* backend_create(const gal_config&,std::string& e) { e="built headless-only; rebuild with SDL3 GPU backend"; return nullptr; }
void backend_destroy(Backend*) {}
const char* backend_name(Backend*) { return "unavailable"; }
bool backend_poll(Backend*,gal_input&,std::string&) { return false; }
bool backend_draw(Backend*,const Vertex*,uint32_t,const DrawRun*,uint32_t,uint32_t&,std::string&) { return false; }
bool backend_tone(Backend*,std::string&) { return false; }

bool backend_texture_load(Backend*,const char*,uint64_t,std::string&){return false;}
void backend_texture_release(Backend*,uint64_t){}
