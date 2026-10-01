#include "gal.h"
#include "gal_ui.h"
#include "backend.h"
#include "audio_backend.h"
#include <array>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <atomic>
#include <new>
#include <thread>
#include <vector>
#include <string>
struct TextureInfo { uint64_t id; int32_t width,height; bool operator==(uint64_t other)const{return id==other;} };
struct gal_context {
 gal_config config{}; gal_camera camera{}; gal_stats stats{sizeof(gal_stats),0,0,0,0};
 AudioBackend* audio=nullptr;
 std::thread::id owner; bool frame=false; uint32_t pending=0; Backend* backend=nullptr;
 std::vector<Vertex> vertices; std::vector<DrawRun> runs; std::vector<TextureInfo> textures;
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
  if(!(cfg->flags&GAL_HEADLESS)) { std::string why; c->backend=backend_create(c->config,why); if(!c->backend) { delete c; return fail(why.c_str()); } }
  live=c; *out=c; return 0;
 } catch(...) { if(c) { backend_destroy(c->backend); delete c; } return fail("allocation or backend exception"); }
}
int GAL_CALL gal_destroy(gal_context* c) { ENTRY; CHECK; audio_destroy(c->audio); backend_destroy(c->backend); delete c; live=nullptr; return 0; }
const char* GAL_CALL gal_backend(gal_context* c) { ENTRY; if(!valid(c)) { fail("invalid context or wrong thread"); return nullptr; } return c->backend?backend_name(c->backend):"headless-validation"; }
int GAL_CALL gal_poll(gal_context* c,gal_input* input) {
 ENTRY; CHECK; if(c->frame) return fail("poll must occur outside an active frame"); if(!input || input->size!=sizeof(gal_input)) return fail("invalid input size");
 *input={sizeof(gal_input),0,0,0,0,0,c->config.width,c->config.height};
 if(c->backend) { try { std::string why; if(!backend_poll(c->backend,*input,why)) return fail(why.c_str()); c->config.width=input->width; c->config.height=input->height; } catch(...) { return fail("input backend exception"); } }
 return 0;
}
int GAL_CALL gal_poll_v2(gal_context* c,gal_input_v2* input) {
 ENTRY; CHECK; if(c->frame) return fail("poll must occur outside an active frame");
 if(!input||input->size!=sizeof(gal_input_v2)||input->version!=GAL_INPUT_VERSION||input->reserved) return fail("invalid input v2 size/version/reserved");
 gal_input_v2 candidate{};candidate.size=sizeof(candidate);candidate.version=GAL_INPUT_VERSION;
 candidate.flags=GAL_INPUT_FOCUSED|GAL_INPUT_DRAWABLE;
 candidate.window_width=candidate.pixel_width=c->config.width;candidate.window_height=candidate.pixel_height=c->config.height;
 if(c->backend){try{std::string why;if(!backend_poll_v2(c->backend,candidate,why))return fail(why.c_str());}catch(...){return fail("input backend exception");}}
 if((candidate.flags&GAL_INPUT_DRAWABLE)&&candidate.pixel_width>0&&candidate.pixel_height>0&&candidate.pixel_width<=16384&&candidate.pixel_height<=16384&&candidate.window_width>0&&candidate.window_height>0&&candidate.window_width<=16384&&candidate.window_height<=16384){c->config.width=candidate.pixel_width;c->config.height=candidate.pixel_height;}
 else candidate.flags&=~GAL_INPUT_DRAWABLE;
 *input=candidate;return 0;
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
 try{std::string why;int32_t width=0,height=0;if(!backend_texture_load(c->backend,path,id,width,height,why))return fail(why.c_str());if(width<1||height<1||width>4096||height>4096){backend_texture_release(c->backend,id);return fail("invalid decoded texture dimensions");}c->textures.push_back({id,width,height});*out=id;return 0;}catch(...){backend_texture_release(c->backend,id);return fail("texture allocation or backend exception");}
}
int GAL_CALL gal_texture_release(gal_context* c,uint64_t id) {
 ENTRY; CHECK; if(c->frame)return fail("cannot release texture during a frame");
 auto it=std::find(c->textures.begin(),c->textures.end(),id);if(it==c->textures.end())return fail("stale or foreign texture handle");
 backend_texture_release(c->backend,id);c->textures.erase(it);return 0;
}
int GAL_CALL gal_texture_count(gal_context* c,uint32_t* count){ENTRY;CHECK;if(!count)return fail("null texture count");*count=uint32_t(c->textures.size());return 0;}
int GAL_CALL gal_texture_get_info(gal_context*c,uint64_t id,gal_texture_info*info){
 ENTRY;CHECK;if(!info||info->size!=sizeof(*info)||info->reserved)return fail("invalid texture info size/reserved");
 auto it=std::find(c->textures.begin(),c->textures.end(),id);if(it==c->textures.end())return fail("stale or foreign texture handle");
 *info={sizeof(*info),it->width,it->height,0};return 0;
}
static bool valid_draw(gal_context*c,const gal_draw&d){
 const float values[]={d.m11,d.m12,d.m21,d.m22,d.tx,d.ty,d.w,d.h,d.r,d.g,d.b,d.a};for(float v:values)if(!finite(v)){fail("nonfinite affine draw");return false;}
 if(d.w<0||d.h<0||d.r<0||d.r>1||d.g<0||d.g>1||d.b<0||d.b>1||d.a<0||d.a>1){fail("invalid draw extents or color");return false;}
 if(d.texture&&std::find(c->textures.begin(),c->textures.end(),d.texture)==c->textures.end()){fail("stale or foreign texture handle");return false;}
 for(int corner=0;corner<4;corner++){double px=(corner&1)?d.w:0,py=(corner&2)?d.h:0;double x=((double(d.m11)*px+double(d.m21)*py+d.tx-c->camera.x)*c->camera.zoom)*2/c->config.width-1;double y=1-((double(d.m12)*px+double(d.m22)*py+d.ty-c->camera.y)*c->camera.zoom)*2/c->config.height;if(!finite(float(x))||!finite(float(y))){fail("affine draw overflow");return false;}}
 return true;
}
static void append_draw(gal_context*c,const gal_draw&d,float u0,float v0,float u1,float v1){
 uint32_t first=uint32_t(c->vertices.size());Vertex corners[4];
 for(int j=0;j<4;j++){double px=(j&1)?d.w:0,py=(j&2)?d.h:0;float x=float(((double(d.m11)*px+double(d.m21)*py+d.tx-c->camera.x)*c->camera.zoom)*2/c->config.width-1);float y=float(1-((double(d.m12)*px+double(d.m22)*py+d.ty-c->camera.y)*c->camera.zoom)*2/c->config.height);corners[j]={x,y,(j&1)?u1:u0,(j&2)?v1:v0,d.r,d.g,d.b,d.a};}
 for(int j:{0,1,2,1,3,2})c->vertices.push_back(corners[j]);
 if(!c->runs.empty()&&c->runs.back().texture==d.texture)c->runs.back().count+=6;else c->runs.push_back({d.texture,first,6});
}
int GAL_CALL gal_submit_draws(gal_context*c,const gal_draw*draws,uint32_t count){
 ENTRY;CHECK;if(!c->frame)return fail("begin required");
 if(count>c->config.max_sprites-c->pending||(count&&!draws))return fail("invalid draws or batch capacity exceeded");
 for(uint32_t i=0;i<count;i++)if(!valid_draw(c,draws[i]))return -1;
 for(uint32_t i=0;i<count;i++)append_draw(c,draws[i],0,0,1,1);
 c->pending+=count;return 0;
}
int GAL_CALL gal_submit_draws_v2(gal_context*c,const gal_draw_v2*draws,uint32_t count){
 ENTRY;CHECK;if(!c->frame)return fail("begin required");
 if(count>c->config.max_sprites-c->pending||(count&&!draws))return fail("invalid draws or batch capacity exceeded");
 for(uint32_t i=0;i<count;i++){
  const auto&d=draws[i];if(d.size!=sizeof(d)||d.version!=GAL_DRAW_VERSION||d.reserved||(d.flags&~3u))return fail("invalid draw v2 size/version/flags/reserved");
  if(!valid_draw(c,d.draw))return -1;
  if(d.source_x||d.source_y||d.source_w||d.source_h){
   auto it=std::find(c->textures.begin(),c->textures.end(),d.draw.texture);
   if(it==c->textures.end()||d.source_x<0||d.source_y<0||d.source_w<1||d.source_h<1||int64_t(d.source_x)+d.source_w>it->width||int64_t(d.source_y)+d.source_h>it->height)return fail("source rectangle outside texture");
  }
 }
 for(uint32_t i=0;i<count;i++){
  const auto&d=draws[i];float u0=0,v0=0,u1=1,v1=1;
  if(d.source_w){auto it=std::find(c->textures.begin(),c->textures.end(),d.draw.texture);u0=(d.source_x+.5f)/it->width;v0=(d.source_y+.5f)/it->height;u1=(d.source_x+d.source_w-.5f)/it->width;v1=(d.source_y+d.source_h-.5f)/it->height;}
  if(d.flags&GAL_FLIP_X)std::swap(u0,u1);
  if(d.flags&GAL_FLIP_Y)std::swap(v0,v1);
  append_draw(c,d.draw,u0,v0,u1,v1);
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

static int audio_call(gal_context*c,AudioOp op,const void*in,void*out){
 ENTRY;CHECK;if(c->frame)return fail("audio operations require no active sprite frame");
 try{std::string why;if(!audio_dispatch(c->audio,op,in,out,why))return fail(why.c_str());return 0;}
 catch(const std::exception&e){return fail(e.what());}catch(...){return fail("audio allocation or backend exception");}
}
extern "C" {
int GAL_CALL gal_audio_open(gal_context*c,const gal_audio_config*cfg){return audio_call(c,AudioOp::Open,cfg,nullptr);}
int GAL_CALL gal_audio_close(gal_context*c){return audio_call(c,AudioOp::Close,nullptr,nullptr);}
int GAL_CALL gal_audio_load_clip(gal_context*c,const char*path,uint64_t*out){AudioFileRequest r{path,0};return audio_call(c,AudioOp::LoadClip,&r,out);}
int GAL_CALL gal_audio_release_clip(gal_context*c,uint64_t id){return audio_call(c,AudioOp::ReleaseClip,&id,nullptr);}
int GAL_CALL gal_audio_create_voice(gal_context*c,uint64_t id,uint32_t group,uint64_t*out){AudioVoiceRequest r{id,group};return audio_call(c,AudioOp::CreateVoice,&r,out);}
int GAL_CALL gal_audio_open_stream(gal_context*c,const char*path,uint32_t group,uint64_t*out){AudioFileRequest r{path,group};return audio_call(c,AudioOp::OpenStream,&r,out);}
int GAL_CALL gal_audio_release_voice(gal_context*c,uint64_t id){return audio_call(c,AudioOp::ReleaseVoice,&id,nullptr);}
int GAL_CALL gal_audio_voice_command(gal_context*c,uint64_t id,uint32_t command,int32_t loops){AudioCommand r{id,command,loops};return audio_call(c,AudioOp::Command,&r,nullptr);}
int GAL_CALL gal_audio_voice_gain(gal_context*c,uint64_t id,float gain){AudioGain r{id,gain};return audio_call(c,AudioOp::VoiceGain,&r,nullptr);}
int GAL_CALL gal_audio_group_gain(gal_context*c,uint32_t group,float gain){AudioGain r{group,gain};return audio_call(c,AudioOp::GroupGain,&r,nullptr);}
int GAL_CALL gal_audio_get_voice(gal_context*c,uint64_t id,gal_voice_state*out){return audio_call(c,AudioOp::VoiceState,&id,out);}
int GAL_CALL gal_audio_get_state(gal_context*c,gal_audio_state*out){return audio_call(c,AudioOp::State,nullptr,out);}
int GAL_CALL gal_audio_mix(gal_context*c,float*output,uint32_t frames,uint32_t*mixed){AudioMix r{output,frames};return audio_call(c,AudioOp::Mix,&r,mixed);}
}
