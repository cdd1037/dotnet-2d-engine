#pragma once
#include "gal.h"
#include "gal_ui.h"
#include <string>
struct Vertex { float x,y,u,v,r,g,b,a; };
struct DrawRun { uint64_t texture; uint32_t first,count; gal_clip_rect clip; uint64_t material; float parameters[8]; };
struct Backend;
Backend* backend_create(gal_config&,std::string&);
void backend_destroy(Backend*);
const char* backend_name(Backend*);
bool backend_poll(Backend*,gal_input&,std::string&);
bool backend_poll_v2(Backend*,gal_input_v2&,std::string&);
bool backend_draw(Backend*,const Vertex*,uint32_t,const DrawRun*,uint32_t,uint32_t&,std::string&);
bool backend_tone(Backend*,std::string&);

bool backend_texture_load(Backend*,const char*,uint64_t,int32_t&,int32_t&,std::string&);
void backend_texture_release(Backend*,uint64_t);
bool backend_material_create(Backend*,const uint8_t*,uint32_t,uint64_t,std::string&);
void backend_material_release(Backend*,uint64_t);

#ifdef GAL_ENABLE_RMLUI
bool backend_ui(Backend*,int,const void*,void*,std::string&);
#endif

struct BoundUiOpen { const char* path; const char* font; const gal_bound_ui_target* targets; uint32_t count; };
struct BoundUiApply { const gal_bound_ui_snapshot* snapshot; const gal_bound_ui_value* values; const gal_bound_ui_row* rows; };
struct BoundUiTest { uint32_t command; gal_bound_ui_action* value; };
