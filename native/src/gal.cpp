#include "gal.h"
#include "gal_ui.h"
#include "backend.h"
#include <array>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <atomic>
#include <new>
#include <thread>
#include <vector>
#include <string>
struct gal_context {
 gal_config config{}; gal_camera camera{}; gal_stats stats{sizeof(gal_stats),0,0,0,0};
 std::thread::id owner; bool frame=false; uint32_t pending=0; Backend* backend=nullptr;
 std::vector<Vertex> vertices; std::vector<DrawRun> runs; std::vector<uint64_t> textures;
};
static gal_context* live=nullptr;
static uint64_t next_texture=1;
static std::atomic_flag gate=ATOMIC_FLAG_INIT;
struct Guard { Guard() noexcept { while(gate.test_and_set(std::memory_order_acquire)) std::this_thread::yield(); } ~Guard() noexcept { gate.clear(std::memory_order_release); } };
static thread_local char error[512]{};
static int fail(const char* msg) noexcept { std::snprintf(error,sizeof(error),"%s",msg); return -1; }
static bool valid(gal_context* c) { return c && c==live && c->owner==std::this_thread::get_id(); }
static bool finite(float v) { return std::isfinite(v); }
#define ENTRY Guard lock; error[0]=0
#define CHECK if(!valid(c)) return fail("invalid context or wrong thread")
extern "C" {
uint32_t GAL_CALL gal_abi_version() { return 1; }
const char* GAL_CALL gal_last_error() { return error; }
int GAL_CALL gal_create(const gal_config* cfg, gal_context** out) {
 ENTRY;
 if(out) *out=nullptr;
 if(!out || !cfg || cfg->size!=sizeof(gal_config) || cfg->abi_version!=1 || cfg->width<1 || cfg->height<1 || cfg->width>16384 || cfg->height>16384 || cfg->max_sprites<1 || cfg->max_sprites>65536 || (cfg->flags&~3u)) return fail("invalid config or ABI version");
 if(live) return fail("only one context may be live");
 gal_context* c=nullptr;
 try {
  c=new gal_context; c->config=*cfg; c->owner=std::this_thread::get_id(); c->vertices.reserve(size_t(cfg->max_sprites)*6); c->runs.reserve(cfg->max_sprites); c->textures.reserve(256);
  if(!(cfg->flags&GAL_HEADLESS)) { std::string why; c->backend=backend_create(*cfg,why); if(!c->backend) { delete c; return fail(why.c_str()); } }
  live=c; *out=c; return 0;
 } catch(...) { if(c) { backend_destroy(c->backend); delete c; } return fail("allocation or backend exception"); }
}
int GAL_CALL gal_destroy(gal_context* c) { ENTRY; CHECK; backend_destroy(c->backend); delete c; live=nullptr; return 0; }
const char* GAL_CALL gal_backend(gal_context* c) { ENTRY; if(!valid(c)) { fail("invalid context or wrong thread"); return nullptr; } return c->backend?backend_name(c->backend):"headless-validation"; }
int GAL_CALL gal_poll(gal_context* c,gal_input* input) {
 ENTRY; CHECK; if(c->frame) return fail("poll must occur outside an active frame"); if(!input || input->size!=sizeof(gal_input)) return fail("invalid input size");
 *input={sizeof(gal_input),0,0,0,0,0,c->config.width,c->config.height};
 if(c->backend) { try { std::string why; if(!backend_poll(c->backend,*input,why)) return fail(why.c_str()); c->config.width=input->width; c->config.height=input->height; } catch(...) { return fail("input backend exception"); } }
 return 0;
}
int GAL_CALL gal_begin(gal_context* c,const gal_camera* camera) {
 ENTRY; CHECK; if(c->frame) return fail("frame already begun");
 if(!camera || !finite(camera->x)||!finite(camera->y)||!finite(camera->zoom)||camera->zoom<0.01f||camera->zoom>100.f) return fail("invalid camera");
 c->camera=*camera; c->frame=true; c->pending=0; c->vertices.clear(); c->runs.clear(); return 0;
}
int GAL_CALL gal_submit(gal_context* c,const gal_sprite* sprites,uint32_t count) {
 ENTRY; CHECK; if(!c->frame) return fail("begin required");
 if(count>c->config.max_sprites-c->pending || (count&&!sprites)) return fail("invalid sprites or batch capacity exceeded");
 for(uint32_t i=0;i<count;i++) { const auto&s=sprites[i];
  if(!finite(s.x)||!finite(s.y)||!finite(s.w)||!finite(s.h)||!finite(s.r)||!finite(s.g)||!finite(s.b)||!finite(s.a)||s.w<0||s.h<0||s.r<0||s.r>1||s.g<0||s.g>1||s.b<0||s.b>1||s.a<0||s.a>1) return fail("invalid sprite");
  float x=(s.x-c->camera.x)*c->camera.zoom*2/c->config.width-1, y=1-(s.y-c->camera.y)*c->camera.zoom*2/c->config.height;
  float w=s.w*c->camera.zoom*2/c->config.width,h=s.h*c->camera.zoom*2/c->config.height;
  if(!finite(x)||!finite(y)||!finite(w)||!finite(h)||!finite(x+w)||!finite(y-h)) return fail("sprite transform overflow");
 }
 for(uint32_t i=0;i<count;i++) { const auto&s=sprites[i];
  float x=(s.x-c->camera.x)*c->camera.zoom*2/c->config.width-1, y=1-(s.y-c->camera.y)*c->camera.zoom*2/c->config.height;
  float w=s.w*c->camera.zoom*2/c->config.width,h=s.h*c->camera.zoom*2/c->config.height;
  Vertex a{x,y,0,0,s.r,s.g,s.b,s.a}, b{x+w,y,1,0,s.r,s.g,s.b,s.a}, d{x,y-h,0,1,s.r,s.g,s.b,s.a}, e{x+w,y-h,1,1,s.r,s.g,s.b,s.a};
  for(auto v:{a,b,d,b,e,d}) c->vertices.push_back(v);
 }
 if(count){ if(!c->runs.empty()&&c->runs.back().texture==0)c->runs.back().count+=count*6;else c->runs.push_back({0,c->pending*6,count*6}); }
 c->pending+=count; return 0;
}
int GAL_CALL gal_texture_load_bmp(gal_context* c,const char* path,uint64_t* out) {
 ENTRY; CHECK; if(out)*out=0;
 if(!out||!path||!path[0]||c->frame) return fail("invalid texture request or active frame");
 if(!c->backend)return fail("texture upload unavailable in headless validation");
 if(c->textures.size()>=256||next_texture==0)return fail("texture capacity exhausted");
 uint64_t id=next_texture++;
 try{std::string why;if(!backend_texture_load(c->backend,path,id,why))return fail(why.c_str());c->textures.push_back(id);*out=id;return 0;}catch(...){backend_texture_release(c->backend,id);return fail("texture allocation or backend exception");}
}
int GAL_CALL gal_texture_release(gal_context* c,uint64_t id) {
 ENTRY; CHECK; if(c->frame)return fail("cannot release texture during a frame");
 auto it=std::find(c->textures.begin(),c->textures.end(),id);if(it==c->textures.end())return fail("stale or foreign texture handle");
 backend_texture_release(c->backend,id);c->textures.erase(it);return 0;
}
int GAL_CALL gal_texture_count(gal_context* c,uint32_t* count){ENTRY;CHECK;if(!count)return fail("null texture count");*count=uint32_t(c->textures.size());return 0;}
int GAL_CALL gal_submit_draws(gal_context* c,const gal_draw* draws,uint32_t count){
 ENTRY;CHECK;if(!c->frame)return fail("begin required");
 if(count>c->config.max_sprites-c->pending||(count&&!draws))return fail("invalid draws or batch capacity exceeded");
 for(uint32_t i=0;i<count;i++){
  const auto&d=draws[i];const float values[]={d.m11,d.m12,d.m21,d.m22,d.tx,d.ty,d.w,d.h,d.r,d.g,d.b,d.a};for(float v:values)if(!finite(v))return fail("nonfinite affine draw");
  if(d.w<0||d.h<0||d.r<0||d.r>1||d.g<0||d.g>1||d.b<0||d.b>1||d.a<0||d.a>1)return fail("invalid draw extents or color");
  if(d.texture&&std::find(c->textures.begin(),c->textures.end(),d.texture)==c->textures.end())return fail("stale or foreign texture handle");
  for(int corner=0;corner<4;corner++){double px=(corner&1)?d.w:0,py=(corner&2)?d.h:0;double x=((double(d.m11)*px+double(d.m21)*py+d.tx-c->camera.x)*c->camera.zoom)*2/c->config.width-1;double y=1-((double(d.m12)*px+double(d.m22)*py+d.ty-c->camera.y)*c->camera.zoom)*2/c->config.height;if(!finite(float(x))||!finite(float(y)))return fail("affine draw overflow");}
 }
 for(uint32_t i=0;i<count;i++){
  const auto&d=draws[i];uint32_t first=uint32_t(c->vertices.size());Vertex corners[4];
  for(int j=0;j<4;j++){float u=(j&1)?1.f:0.f,v=(j&2)?1.f:0.f;double px=u*d.w,py=v*d.h;float x=float(((double(d.m11)*px+double(d.m21)*py+d.tx-c->camera.x)*c->camera.zoom)*2/c->config.width-1);float y=float(1-((double(d.m12)*px+double(d.m22)*py+d.ty-c->camera.y)*c->camera.zoom)*2/c->config.height);corners[j]={x,y,u,v,d.r,d.g,d.b,d.a};}
  for(int j:{0,1,2,1,3,2})c->vertices.push_back(corners[j]);
  if(!c->runs.empty()&&c->runs.back().texture==d.texture)c->runs.back().count+=6;else c->runs.push_back({d.texture,first,6});
 }
 c->pending+=count;return 0;
}
int GAL_CALL gal_end(gal_context* c) {
 ENTRY; CHECK; if(!c->frame) return fail("begin required"); c->frame=false;
 if(c->backend) { uint32_t drawn=0; try { std::string why; if(!backend_draw(c->backend,c->vertices.data(),uint32_t(c->vertices.size()),c->runs.data(),uint32_t(c->runs.size()),drawn,why)) return fail(why.c_str()); } catch(...) { return fail("draw backend exception"); } c->stats.draw_calls+=drawn; }
 c->stats.frames++; c->stats.sprites+=c->pending; return 0;
}
int GAL_CALL gal_abort(gal_context* c) { ENTRY; CHECK; if(!c->frame) return fail("begin required"); c->frame=false; c->pending=0; c->vertices.clear(); c->runs.clear(); return 0; }
int GAL_CALL gal_play_tone(gal_context* c) { ENTRY; CHECK; if(!c->backend || !(c->config.flags&GAL_AUDIO)) return fail("audio unavailable or disabled"); try {std::string why; if(!backend_tone(c->backend,why)) return fail(why.c_str());}catch(...){return fail("audio backend exception");} c->stats.audio_plays++; return 0; }
int GAL_CALL gal_get_stats(gal_context* c,gal_stats* stats) { ENTRY; CHECK; if(!stats||stats->size!=sizeof(gal_stats)) return fail("invalid stats size"); *stats=c->stats; return 0; }
}

static int ui_call(gal_context* c,int op,const void* input,void* output) {
 ENTRY; CHECK;
 if(c->frame)return fail("UI operations require no active sprite frame");
#ifdef GAL_ENABLE_RMLUI
 if(!c->backend)return fail("UI requires a graphics backend");
 try {std::string why;if(!backend_ui(c->backend,op,input,output,why))return fail(why.c_str());return 0;}
 catch(const std::exception& e){return fail(e.what());}catch(...){return fail("UI backend exception");}
#else
 (void)op;(void)input;(void)output;return fail("RmlUi experimental module not enabled in this build");
#endif
}
extern "C" {
int GAL_CALL gal_ui_open(gal_context*c,const char*path,const char*font){const char*paths[]={path,font};return ui_call(c,1,paths,nullptr);}
int GAL_CALL gal_ui_close(gal_context*c){return ui_call(c,2,nullptr,nullptr);}
int GAL_CALL gal_ui_set_model(gal_context*c,const gal_ui_model*m){return ui_call(c,3,m,nullptr);}
int GAL_CALL gal_ui_poll_action(gal_context*c,gal_ui_action*a){return ui_call(c,4,nullptr,a);}
int GAL_CALL gal_ui_get_state(gal_context*c,gal_ui_state*s){return ui_call(c,5,nullptr,s);}
int GAL_CALL gal_ui_test_command(gal_context*c,uint32_t generation,uint32_t command){uint32_t args[]={generation,command};return ui_call(c,6,args,nullptr);}
int GAL_CALL gal_game_ui_open(gal_context*c,const char*path,const char*font){const char*paths[]={path,font};return ui_call(c,10,paths,nullptr);}
int GAL_CALL gal_game_ui_set_model(gal_context*c,const gal_game_ui_model*m){return ui_call(c,11,m,nullptr);}
int GAL_CALL gal_game_ui_poll_action(gal_context*c,gal_game_ui_action*a){return ui_call(c,12,nullptr,a);}
int GAL_CALL gal_game_ui_test_command(gal_context*c,uint32_t generation,uint32_t command){uint32_t args[]={generation,command};return ui_call(c,13,args,nullptr);}
int GAL_CALL gal_capture_next(gal_context*c,const char*path){return ui_call(c,7,path,nullptr);}
}
