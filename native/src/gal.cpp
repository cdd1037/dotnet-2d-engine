#include "gal.h"
#include "gal_ui.h"
#include "backend.h"
#include "audio_backend.h"
#include "physics_backend.h"
#include <array>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <atomic>
#include <new>
#include <thread>
#include <vector>
#include <string>
struct TextureInfo { uint64_t id; int32_t width,height; bool target=false; bool operator==(uint64_t other)const{return id==other;} };
struct Projection { gal_camera camera; int32_t width,height; };
struct gal_context {
 gal_config config{}; gal_camera camera{}; gal_stats stats{sizeof(gal_stats),0,0,0,0};
 AudioBackend* audio=nullptr; PhysicsBackend* physics=nullptr;
 std::thread::id owner; bool frame=false; uint32_t pending=0; Backend* backend=nullptr;
 uint32_t target_count=0; uint64_t target_bytes=0;
 std::vector<Vertex> vertices; std::vector<DrawRun> runs; std::vector<TextureInfo> textures; std::vector<uint64_t> materials;
};
static gal_context* live=nullptr;
static uint64_t next_texture=1;
static uint64_t next_material=1;
static std::atomic_flag gate=ATOMIC_FLAG_INIT;
struct Guard { Guard() noexcept { while(gate.test_and_set(std::memory_order_acquire)) std::this_thread::yield(); } ~Guard() noexcept { gate.clear(std::memory_order_release); } };
static thread_local char error[512]{};
static int fail(const char* msg) noexcept { std::snprintf(error,sizeof(error),"%s",msg); return -1; }
static bool valid(gal_context* c) { return c && c==live && c->owner==std::this_thread::get_id(); }
static bool finite(float v) { return std::isfinite(v); }
static bool valid_camera(const gal_camera&camera) { return finite(camera.x)&&finite(camera.y)&&finite(camera.zoom)&&camera.zoom>=0.01f&&camera.zoom<=100.f; }
static constexpr gal_clip_rect unclipped{sizeof(gal_clip_rect),GAL_CLIP_VERSION,0,0,0,0,0,0};
static constexpr float default_parameters[8]{};
static bool same_clip(const gal_clip_rect&a,const gal_clip_rect&b) {
 return a.flags==b.flags&&a.x==b.x&&a.y==b.y&&a.width==b.width&&a.height==b.height;
}
static void append_run(gal_context*c,uint64_t texture,uint32_t first,uint32_t count,const gal_clip_rect&clip,uint64_t material=0,const float*parameters=default_parameters,bool merge=true) {
 if(merge&&!c->runs.empty()&&c->runs.back().texture==texture&&c->runs.back().material==material&&same_clip(c->runs.back().clip,clip)&&std::memcmp(c->runs.back().parameters,parameters,sizeof(default_parameters))==0)c->runs.back().count+=count;
 else { DrawRun run{texture,first,count,clip,material,{}};std::memcpy(run.parameters,parameters,sizeof(run.parameters));c->runs.push_back(run); }
}
static void discard_material(Backend*backend,uint64_t id) noexcept { if(backend)try{backend_material_release(backend,id);}catch(...){} }
static void discard_target(Backend*backend,uint64_t id) noexcept { if(backend)try{backend_target_release(backend,id);}catch(...){} }
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
  c=new gal_context; c->config=*cfg; c->owner=std::this_thread::get_id(); c->vertices.reserve(size_t(cfg->max_sprites)*6); c->runs.reserve(cfg->max_sprites); c->textures.reserve(256+GAL_TARGET_CAPACITY); c->materials.reserve(GAL_MATERIAL_CAPACITY);
  if(!(cfg->flags&GAL_HEADLESS)) { std::string why; c->backend=backend_create(c->config,why); if(!c->backend) { delete c; return fail(why.c_str()); } }
  live=c; *out=c; return 0;
 } catch(...) { if(c) { backend_destroy(c->backend); delete c; } return fail("allocation or backend exception"); }
}
int GAL_CALL gal_destroy(gal_context* c) { ENTRY; CHECK; physics_destroy(c->physics); audio_destroy(c->audio); backend_destroy(c->backend); delete c; live=nullptr; return 0; }
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
 if(!camera || !valid_camera(*camera)) return fail("invalid camera");
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
 if(count)append_run(c,0,c->pending*6,count*6,unclipped);
 c->pending+=count; return 0;
}
int GAL_CALL gal_texture_load_bmp(gal_context* c,const char* path,uint64_t* out) {
 ENTRY; CHECK; if(out)*out=0;
 if(!out||!path||!path[0]||c->frame) return fail("invalid texture request or active frame");
 if(!c->backend)return fail("texture upload unavailable in headless validation");
 if(c->textures.size()-c->target_count>=256||next_texture==0)return fail("texture capacity exhausted");
 uint64_t id=next_texture++;
 try{std::string why;int32_t width=0,height=0;if(!backend_texture_load(c->backend,path,id,width,height,why))return fail(why.c_str());if(width<1||height<1||width>4096||height>4096){backend_texture_release(c->backend,id);return fail("invalid decoded texture dimensions");}c->textures.push_back({id,width,height});*out=id;return 0;}catch(...){backend_texture_release(c->backend,id);return fail("texture allocation or backend exception");}
}
int GAL_CALL gal_texture_release(gal_context* c,uint64_t id) {
 ENTRY; CHECK; if(c->frame)return fail("cannot release texture during a frame");
 auto it=std::find(c->textures.begin(),c->textures.end(),id);if(it==c->textures.end())return fail("stale or foreign texture handle");
 if(it->target)return fail("target-owned textures require gal_target_release");
 backend_texture_release(c->backend,id);c->textures.erase(it);return 0;
}
int GAL_CALL gal_texture_count(gal_context* c,uint32_t* count){ENTRY;CHECK;if(!count)return fail("null texture count");*count=uint32_t(c->textures.size());return 0;}
int GAL_CALL gal_texture_get_info(gal_context*c,uint64_t id,gal_texture_info*info){
 ENTRY;CHECK;if(!info||info->size!=sizeof(*info)||info->reserved)return fail("invalid texture info size/reserved");
 auto it=std::find(c->textures.begin(),c->textures.end(),id);if(it==c->textures.end())return fail("stale or foreign texture handle");
 *info={sizeof(*info),it->width,it->height,0};return 0;
}
int GAL_CALL gal_target_create_v1(gal_context*c,const gal_target_desc*desc,uint64_t*out){
 ENTRY;if(out)*out=0;CHECK;
 if(c->frame)return fail("cannot create target during a frame");
 if(!out||!desc||desc->size!=sizeof(*desc)||desc->version!=GAL_TARGET_VERSION||desc->width<1||desc->height<1||desc->width>4096||desc->height>4096)return fail("invalid target descriptor or dimensions (1..4096)");
 const uint64_t bytes=uint64_t(desc->width)*uint64_t(desc->height)*8;
 if(c->target_count>=GAL_TARGET_CAPACITY||bytes>64u*1024u*1024u-c->target_bytes||next_texture==0)return fail("target capacity or 64 MiB paired-texture budget exhausted");
 const uint64_t id=next_texture++;
 try{
  if(c->backend){std::string why;if(!backend_target_create(c->backend,id,desc->width,desc->height,why)){discard_target(c->backend,id);return fail(why.c_str());}}
  c->textures.push_back({id,desc->width,desc->height,true});++c->target_count;c->target_bytes+=bytes;*out=id;return 0;
 }catch(...){discard_target(c->backend,id);return fail("target allocation or backend exception");}
}
int GAL_CALL gal_target_release(gal_context*c,uint64_t id){
 ENTRY;CHECK;if(c->frame)return fail("cannot release target during a frame");
 auto it=std::find(c->textures.begin(),c->textures.end(),id);if(it==c->textures.end()||!it->target)return fail("stale, foreign or non-target handle");
 try{if(c->backend)backend_target_release(c->backend,id);c->target_bytes-=uint64_t(it->width)*uint64_t(it->height)*8;--c->target_count;c->textures.erase(it);return 0;}catch(...){return fail("target release backend exception");}
}
int GAL_CALL gal_material_create_v1(gal_context*c,const gal_material_desc*desc,const uint8_t*fragment,uint64_t*out){
 ENTRY;if(out)*out=0;CHECK;
 if(c->frame)return fail("cannot create material during a frame");
 if(!out||!desc||!fragment||desc->size!=sizeof(*desc)||desc->version!=GAL_MATERIAL_VERSION||desc->parameter_bytes!=GAL_MATERIAL_PARAMETER_BYTES)return fail("invalid material descriptor or pointers");
 if(desc->fragment_bytes<20||desc->fragment_bytes>256*1024||(desc->fragment_bytes&3u))return fail("material SPIR-V byte length must be 20..262144 and divisible by four");
 // Read the bounded header without requiring the borrowed byte pointer to be aligned.
 auto word=[&](uint32_t offset){return uint32_t(fragment[offset])|(uint32_t(fragment[offset+1])<<8)|(uint32_t(fragment[offset+2])<<16)|(uint32_t(fragment[offset+3])<<24);};
 if(word(0)!=0x07230203u||word(4)!=0x00010000u||word(12)==0||word(12)>0x003fffffu||word(16)!=0)return fail("invalid SPIR-V 1.0 material header");
 if(c->materials.size()>=GAL_MATERIAL_CAPACITY||next_material==0)return fail("material capacity exhausted");
 const uint64_t id=next_material++;
 try{
  if(c->backend){std::string why;if(!backend_material_create(c->backend,fragment,desc->fragment_bytes,id,why)){discard_material(c->backend,id);return fail(why.c_str());}}
  c->materials.push_back(id);*out=id;return 0;
 }catch(...){discard_material(c->backend,id);return fail("material allocation or backend exception");}
}
int GAL_CALL gal_material_release(gal_context*c,uint64_t id){
 ENTRY;CHECK;if(c->frame)return fail("cannot release material during a frame");
 auto it=std::find(c->materials.begin(),c->materials.end(),id);if(it==c->materials.end())return fail("stale or foreign material handle");
 try{if(c->backend)backend_material_release(c->backend,id);c->materials.erase(it);return 0;}catch(...){return fail("material release backend exception");}
}
int GAL_CALL gal_material_count(gal_context*c,uint32_t*count){ENTRY;CHECK;if(!count)return fail("null material count");*count=uint32_t(c->materials.size());return 0;}
static bool valid_draw(gal_context*c,const gal_draw&d,const Projection*projection=nullptr){
 const auto camera=projection?projection->camera:c->camera;const int32_t width=projection?projection->width:c->config.width,height=projection?projection->height:c->config.height;
 const float values[]={d.m11,d.m12,d.m21,d.m22,d.tx,d.ty,d.w,d.h,d.r,d.g,d.b,d.a};for(float v:values)if(!finite(v)){fail("nonfinite affine draw");return false;}
 if(d.w<0||d.h<0||d.r<0||d.r>1||d.g<0||d.g>1||d.b<0||d.b>1||d.a<0||d.a>1){fail("invalid draw extents or color");return false;}
 if(d.texture&&std::find(c->textures.begin(),c->textures.end(),d.texture)==c->textures.end()){fail("stale or foreign texture handle");return false;}
 for(int corner=0;corner<4;corner++){double px=(corner&1)?d.w:0,py=(corner&2)?d.h:0;double x=((double(d.m11)*px+double(d.m21)*py+d.tx-camera.x)*camera.zoom)*2/width-1;double y=1-((double(d.m12)*px+double(d.m22)*py+d.ty-camera.y)*camera.zoom)*2/height;if(!finite(float(x))||!finite(float(y))){fail("affine draw overflow");return false;}}
 return true;
}
static void append_draw(gal_context*c,const gal_draw&d,float u0,float v0,float u1,float v1,const gal_clip_rect&clip=unclipped,uint64_t material=0,const float*parameters=default_parameters,const Projection*projection=nullptr,bool merge=true){
 const auto camera=projection?projection->camera:c->camera;const int32_t width=projection?projection->width:c->config.width,height=projection?projection->height:c->config.height;
 uint32_t first=uint32_t(c->vertices.size());Vertex corners[4];
 for(int j=0;j<4;j++){double px=(j&1)?d.w:0,py=(j&2)?d.h:0;float x=float(((double(d.m11)*px+double(d.m21)*py+d.tx-camera.x)*camera.zoom)*2/width-1);float y=float(1-((double(d.m12)*px+double(d.m22)*py+d.ty-camera.y)*camera.zoom)*2/height);corners[j]={x,y,(j&1)?u1:u0,(j&2)?v1:v0,d.r,d.g,d.b,d.a};}
 for(int j:{0,1,2,1,3,2})c->vertices.push_back(corners[j]);
 append_run(c,d.texture,first,6,clip,material,parameters,merge);
}
int GAL_CALL gal_submit_draws(gal_context*c,const gal_draw*draws,uint32_t count){
 ENTRY;CHECK;if(!c->frame)return fail("begin required");
 if(count>c->config.max_sprites-c->pending||(count&&!draws))return fail("invalid draws or batch capacity exceeded");
 for(uint32_t i=0;i<count;i++)if(!valid_draw(c,draws[i]))return -1;
 for(uint32_t i=0;i<count;i++)append_draw(c,draws[i],0,0,1,1);
 c->pending+=count;return 0;
}
static int validate_clips(const gal_clip_rect*clips,uint32_t clip_count,uint32_t count){
 if((clip_count&&!clips)||(clip_count>1&&clip_count!=count))return fail("invalid clip array or count");
 for(uint32_t i=0;i<clip_count;i++){
  const auto&clip=clips[i];
  if(clip.size!=sizeof(clip)||clip.version!=GAL_CLIP_VERSION||clip.reserved||(clip.flags&~uint32_t(GAL_CLIP_ENABLED)))return fail("invalid clip size/version/flags/reserved");
  if(clip.width<0||clip.height<0||(!(clip.flags&GAL_CLIP_ENABLED)&&(clip.x||clip.y||clip.width||clip.height)))return fail("invalid clip rectangle");
 }
 return 0;
}
static int validate_draw_v2(gal_context*c,const gal_draw_v2&d,const Projection*projection=nullptr){
 if(d.size!=sizeof(d)||d.version!=GAL_DRAW_VERSION||d.reserved||(d.flags&~3u))return fail("invalid draw v2 size/version/flags/reserved");
 if(!valid_draw(c,d.draw,projection))return -1;
 if(d.source_x||d.source_y||d.source_w||d.source_h){
  auto it=std::find(c->textures.begin(),c->textures.end(),d.draw.texture);
  if(it==c->textures.end()||d.source_x<0||d.source_y<0||d.source_w<1||d.source_h<1||int64_t(d.source_x)+d.source_w>it->width||int64_t(d.source_y)+d.source_h>it->height)return fail("source rectangle outside texture");
 }
 return 0;
}
static void append_draw_v2(gal_context*c,const gal_draw_v2&d,const gal_clip_rect&clip,uint64_t material=0,const float*parameters=default_parameters,const Projection*projection=nullptr,bool merge=true){
 float u0=0,v0=0,u1=1,v1=1;
 if(d.source_w){auto it=std::find(c->textures.begin(),c->textures.end(),d.draw.texture);u0=(d.source_x+.5f)/it->width;v0=(d.source_y+.5f)/it->height;u1=(d.source_x+d.source_w-.5f)/it->width;v1=(d.source_y+d.source_h-.5f)/it->height;}
 if(d.flags&GAL_FLIP_X)std::swap(u0,u1);
 if(d.flags&GAL_FLIP_Y)std::swap(v0,v1);
 append_draw(c,d.draw,u0,v0,u1,v1,clip,material,parameters,projection,merge);
}
static int submit_draws_clipped(gal_context*c,const gal_draw_v2*draws,uint32_t count,const gal_clip_rect*clips,uint32_t clip_count){
 if(!c->frame)return fail("begin required");
 if(count>c->config.max_sprites-c->pending||(count&&!draws))return fail("invalid draws or batch capacity exceeded");
 if(validate_clips(clips,clip_count,count))return -1;
 for(uint32_t i=0;i<count;i++)if(validate_draw_v2(c,draws[i]))return -1;
 for(uint32_t i=0;i<count;i++)append_draw_v2(c,draws[i],clip_count?clips[clip_count==1?0:i]:unclipped);
 c->pending+=count;return 0;
}
int GAL_CALL gal_submit_draws_v2(gal_context*c,const gal_draw_v2*draws,uint32_t count){
 ENTRY;CHECK;return submit_draws_clipped(c,draws,count,nullptr,0);
}
int GAL_CALL gal_submit_draws_clipped_v1(gal_context*c,const gal_draw_v2*draws,uint32_t count,const gal_clip_rect*clips,uint32_t clip_count){
 ENTRY;CHECK;return submit_draws_clipped(c,draws,count,clips,clip_count);
}
static int validate_material_draw(gal_context*c,const gal_material_draw_v1&d,const Projection*projection=nullptr){
 if(d.size!=sizeof(d)||d.version!=GAL_MATERIAL_DRAW_VERSION)return fail("invalid material draw size/version");
 if(validate_draw_v2(c,d.sprite,projection))return -1;
 if(d.material&&std::find(c->materials.begin(),c->materials.end(),d.material)==c->materials.end())return fail("stale or foreign material handle");
 for(float parameter:d.parameters)if(!finite(parameter)||(!d.material&&parameter!=0))return fail("invalid material parameters");
 return 0;
}
int GAL_CALL gal_submit_material_draws_v1(gal_context*c,const gal_material_draw_v1*draws,uint32_t count,const gal_clip_rect*clips,uint32_t clip_count){
 ENTRY;CHECK;if(!c->frame)return fail("begin required");
 if(count>c->config.max_sprites-c->pending||(count&&!draws))return fail("invalid material draws or batch capacity exceeded");
 if(validate_clips(clips,clip_count,count))return -1;
 for(uint32_t i=0;i<count;i++)if(validate_material_draw(c,draws[i]))return -1;
 // Capacity was reserved at context creation. Each sprite contributes at most one
 // run, so validated appends cannot allocate or fail partway through this batch.
 for(uint32_t i=0;i<count;i++){
  const auto&d=draws[i];append_draw_v2(c,d.sprite,clip_count?clips[clip_count==1?0:i]:unclipped,d.material,d.material?d.parameters:default_parameters);
 }
 c->pending+=count;return 0;
}
int GAL_CALL gal_render_frame_v1(gal_context*c,const gal_render_pass_v1*passes,uint32_t pass_count,const gal_material_draw_v1*draws,uint32_t draw_count,const gal_clip_rect*clips,uint32_t clip_count){
 ENTRY;CHECK;if(c->frame)return fail("cannot render passes during an active legacy frame");
 if(!passes||pass_count<1||pass_count>GAL_RENDER_PASS_CAPACITY)return fail("render frame requires 1..16 passes");
 if(draw_count>c->config.max_sprites||(draw_count&&!draws))return fail("invalid material draws or frame capacity exceeded");
 if(validate_clips(clips,clip_count,draw_count))return -1;
 std::array<RenderPass,GAL_RENDER_PASS_CAPACITY> backend_passes{};
 uint32_t next_draw=0;
 // Validate every range and resource before writing vertices or executing GPU work.
 for(uint32_t p=0;p<pass_count;p++){
  const auto&pass=passes[p];
  if(pass.size!=sizeof(pass)||pass.version!=GAL_RENDER_PASS_VERSION||pass.reserved)return fail("invalid render pass size/version/reserved");
  if(!valid_camera(pass.camera))return fail("invalid render pass camera");
  for(float channel:pass.clear)if(!finite(channel)||channel<0||channel>1)return fail("invalid render pass clear color");
  if(pass.first_draw!=next_draw||pass.draw_count>draw_count-next_draw)return fail("render pass ranges must partition the draw array");
  int32_t width=c->config.width,height=c->config.height;
  if(p+1==pass_count){if(pass.target)return fail("last render pass must target the window");}
  else{
   auto it=std::find(c->textures.begin(),c->textures.end(),pass.target);
   if(it==c->textures.end()||!it->target)return fail("offscreen render pass requires a live owned target");
   width=it->width;height=it->height;
  }
  auto&prepared=backend_passes[p];prepared.target=pass.target;prepared.width=width;prepared.height=height;std::copy(std::begin(pass.clear),std::end(pass.clear),prepared.clear);
  const Projection projection{pass.camera,width,height};
  for(uint32_t d=next_draw;d<next_draw+pass.draw_count;d++){
   if(validate_material_draw(c,draws[d],&projection))return -1;
   if(pass.target&&draws[d].sprite.draw.texture==pass.target)return fail("cannot sample the current render target");
  }
  next_draw+=pass.draw_count;
 }
 if(next_draw!=draw_count)return fail("render pass ranges must cover every draw");
 c->vertices.clear();c->runs.clear();c->pending=0;
 // Storage is reserved at create; one user draw contributes at most one run.
 for(uint32_t p=0;p<pass_count;p++){
  const auto&pass=passes[p];auto&prepared=backend_passes[p];prepared.first_run=uint32_t(c->runs.size());
  const Projection projection{pass.camera,prepared.width,prepared.height};
  for(uint32_t i=0;i<pass.draw_count;i++){
   const auto index=pass.first_draw+i;const auto&d=draws[index];
   append_draw_v2(c,d.sprite,clip_count?clips[clip_count==1?0:index]:unclipped,d.material,d.material?d.parameters:default_parameters,&projection,i!=0);
  }
  prepared.run_count=uint32_t(c->runs.size())-prepared.first_run;
 }
 uint32_t drawn=0;
 if(c->backend){
  try{std::string why;if(!backend_render_frame(c->backend,c->vertices.data(),uint32_t(c->vertices.size()),c->runs.data(),uint32_t(c->runs.size()),backend_passes.data(),pass_count,drawn,why))return fail(why.c_str());}
  catch(...){return fail("render frame backend exception");}
 }
 c->stats.frames++;c->stats.sprites+=draw_count;c->stats.draw_calls+=drawn;return 0;
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
int GAL_CALL gal_ui_get_text_state(gal_context*c,gal_ui_text_state*s){return ui_call(c,8,nullptr,s);}
int GAL_CALL gal_ui_test_command(gal_context*c,uint32_t generation,uint32_t command){uint32_t args[]={generation,command};return ui_call(c,6,args,nullptr);}
int GAL_CALL gal_game_ui_open(gal_context*c,const char*path,const char*font){const char*paths[]={path,font};return ui_call(c,10,paths,nullptr);}
int GAL_CALL gal_game_ui_set_model(gal_context*c,const gal_game_ui_model*m){return ui_call(c,11,m,nullptr);}
int GAL_CALL gal_game_ui_poll_action(gal_context*c,gal_game_ui_action*a){return ui_call(c,12,nullptr,a);}
int GAL_CALL gal_game_ui_test_command(gal_context*c,uint32_t generation,uint32_t command){uint32_t args[]={generation,command};return ui_call(c,13,args,nullptr);}
int GAL_CALL gal_bound_ui_open(gal_context*c,const char*p,const char*f,const gal_bound_ui_target*t,uint32_t n){BoundUiOpen r{p,f,t,n};return ui_call(c,20,&r,nullptr);}
int GAL_CALL gal_bound_ui_open_images(gal_context*c,const char*p,const char*f,const gal_bound_ui_target*t,uint32_t n,const char*const*images,uint32_t image_count){BoundUiOpen r{p,f,t,n,images,image_count};return ui_call(c,20,&r,nullptr);}
int GAL_CALL gal_bound_ui_apply(gal_context*c,const gal_bound_ui_snapshot*s,const gal_bound_ui_value*v,const gal_bound_ui_row*r){BoundUiApply a{s,v,r};return ui_call(c,21,&a,nullptr);}
int GAL_CALL gal_bound_ui_poll(gal_context*c,gal_bound_ui_action*a){return ui_call(c,22,nullptr,a);}
int GAL_CALL gal_bound_ui_test_command(gal_context*c,uint32_t command,gal_bound_ui_action*a){BoundUiTest r{command,a};return ui_call(c,23,&r,nullptr);}
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

static int physics_call(gal_context*c,PhysicsOp op,const void*in,void*out){
 ENTRY;CHECK;if(c->frame)return fail("physics operations require no active sprite frame");
 try{std::string why;if(!physics_dispatch(c->physics,op,in,out,why))return fail(why.c_str());return 0;}
 catch(const std::exception&e){return fail(e.what());}catch(...){return fail("physics allocation or backend exception");}
}
extern "C" {
int GAL_CALL gal_physics_open(gal_context*c,const gal_physics_config*cfg){return physics_call(c,PhysicsOp::Open,cfg,nullptr);}
int GAL_CALL gal_physics_close(gal_context*c){return physics_call(c,PhysicsOp::Close,nullptr,nullptr);}
int GAL_CALL gal_physics_create_body(gal_context*c,const gal_body_def*def,uint64_t*out){return physics_call(c,PhysicsOp::CreateBody,def,out);}
int GAL_CALL gal_physics_release_body(gal_context*c,uint64_t id){return physics_call(c,PhysicsOp::ReleaseBody,&id,nullptr);}
int GAL_CALL gal_physics_create_shape(gal_context*c,uint64_t body,const gal_shape_def*def,uint64_t*out){PhysicsShapeRequest r{body,def};return physics_call(c,PhysicsOp::CreateShape,&r,out);}
int GAL_CALL gal_physics_create_capsule_v1(gal_context*c,uint64_t body,const gal_capsule_def_v1*def,uint64_t*out){PhysicsCapsuleRequest r{body,def};return physics_call(c,PhysicsOp::CreateCapsule,&r,out);}
int GAL_CALL gal_physics_release_shape(gal_context*c,uint64_t id){return physics_call(c,PhysicsOp::ReleaseShape,&id,nullptr);}
int GAL_CALL gal_physics_body_command(gal_context*c,uint64_t body,uint32_t command,float x,float y,float z){PhysicsCommand r{body,command,x,y,z};return physics_call(c,PhysicsOp::BodyCommand,&r,nullptr);}
int GAL_CALL gal_physics_get_body(gal_context*c,uint64_t id,gal_body_state*out){return physics_call(c,PhysicsOp::BodyState,&id,out);}
int GAL_CALL gal_physics_step(gal_context*c,gal_physics_step_result*out){return physics_call(c,PhysicsOp::Step,nullptr,out);}
int GAL_CALL gal_physics_events(gal_context*c,gal_physics_event*out,uint32_t cap,uint32_t*count){PhysicsEventsRequest r{out,cap};return physics_call(c,PhysicsOp::Events,&r,count);}
int GAL_CALL gal_physics_ray_cast(gal_context*c,const gal_physics_ray*q,gal_physics_ray_hit*out){return physics_call(c,PhysicsOp::Ray,q,out);}
int GAL_CALL gal_physics_query_aabb(gal_context*c,const gal_physics_aabb*q,uint64_t*out,uint32_t cap,uint32_t*count){PhysicsAabbRequest r{q,out,cap};return physics_call(c,PhysicsOp::Aabb,&r,count);}
int GAL_CALL gal_physics_query_overlap_v1(gal_context*c,const gal_physics_overlap_query_v1*q,uint64_t*out,uint32_t cap,uint32_t*count){PhysicsOverlapRequest r{q,out,cap};return physics_call(c,PhysicsOp::Overlap,&r,count);}
int GAL_CALL gal_physics_get_state(gal_context*c,gal_physics_state*out){return physics_call(c,PhysicsOp::State,nullptr,out);}
}
