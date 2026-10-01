#pragma once
#include "gal.h"
#include <string>
struct Vertex { float x,y,u,v,r,g,b,a; };
struct DrawRun { uint64_t texture; uint32_t first,count; };
struct Backend;
Backend* backend_create(gal_config&,std::string&);
void backend_destroy(Backend*);
const char* backend_name(Backend*);
bool backend_poll(Backend*,gal_input&,std::string&);
bool backend_poll_v2(Backend*,gal_input_v2&,std::string&);
bool backend_draw(Backend*,const Vertex*,uint32_t,const DrawRun*,uint32_t,uint32_t&,std::string&);
bool backend_tone(Backend*,std::string&);

bool backend_texture_load(Backend*,const char*,uint64_t,std::string&);
void backend_texture_release(Backend*,uint64_t);

#ifdef GAL_ENABLE_RMLUI
bool backend_ui(Backend*,int,const void*,void*,std::string&);
#endif
