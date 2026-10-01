#include "gal.h"
#include "backend.h"
#include "clip_rect.h"
#include <algorithm>
#include <array>
#include <cstdio>
#include <cmath>
#include <limits>
#include <stdexcept>
#include <vector>
#include <thread>
struct Backend{int32_t width,height;};
static int create_mode=0,draw_mode=0, poll_mode=0,destroyed=0;
static int material_create_mode=0,material_create_calls=0,material_release_calls=0;
static std::vector<uint64_t> backend_materials;
static std::vector<uint8_t> captured_fragment;
static std::vector<Vertex> captured;
static std::vector<DrawRun> captured_runs;
static std::vector<ScissorRect> submitted_scissors;
Backend* backend_create(gal_config&config,std::string& e){if(create_mode==1){e="injected creation error";return nullptr;}if(create_mode==2)throw std::runtime_error("create");return new Backend{config.width,config.height};}
void backend_destroy(Backend*b){if(b){++destroyed;backend_materials.clear();}delete b;}
const char* backend_name(Backend*){return "mock";}
bool backend_poll(Backend*,gal_input&i,std::string&e){if(poll_mode==1){e="injected input error";return false;}if(poll_mode==2)throw std::runtime_error("poll");if(poll_mode==3)i.width=200;return true;}
bool backend_poll_v2(Backend*,gal_input_v2&i,std::string&e){if(poll_mode==1){e="injected input error";return false;}if(poll_mode==2)throw std::runtime_error("poll");if(poll_mode==3)i.pixel_width=200;if(poll_mode==4)i.pixel_width=i.window_width=0;return true;}
bool backend_draw(Backend*b,const Vertex*v,uint32_t n,const DrawRun*runs,uint32_t run_count,uint32_t& drawn,std::string&e){
 if(draw_mode==1){e="injected draw error";return false;}if(draw_mode==2)throw std::runtime_error("draw");
 captured.clear();captured_runs.clear();submitted_scissors.clear();drawn=0;
 if(n)captured.assign(v,v+n);
 if(run_count)captured_runs.assign(runs,runs+run_count);
 if(draw_mode!=3&&n)for(uint32_t i=0;i<run_count;i++){
  auto rect=intersect_clip(runs[i].clip,b->width,b->height);
  if(rect.width&&rect.height){submitted_scissors.push_back(rect);++drawn;}
 }
 return true;
}
bool backend_tone(Backend*,std::string&){throw std::runtime_error("tone");}
bool backend_material_create(Backend*,const uint8_t*fragment,uint32_t bytes,uint64_t id,std::string&e){
 ++material_create_calls;backend_materials.push_back(id);captured_fragment.assign(fragment,fragment+bytes);
 if(material_create_mode==1){e="injected material error after allocation";return false;}
 if(material_create_mode==2)throw std::runtime_error("material allocation");
 return true;
}
void backend_material_release(Backend*,uint64_t id){++material_release_calls;backend_materials.erase(std::remove(backend_materials.begin(),backend_materials.end(),id),backend_materials.end());}
#define R(x) do{if(!(x)){std::fprintf(stderr,"FAIL %d %s error=%s\n",__LINE__,#x,gal_last_error());return 1;}}while(0)
static bool zero_parameters(const DrawRun&run){for(float value:run.parameters)if(value!=0)return false;return true;}
static int material_contract(gal_context*c,const gal_camera&cam,const gal_draw_v2&region,uint64_t&retained){
 const std::array<uint8_t,20> fragment={3,2,35,7,0,0,1,0,0,0,0,0,1,0,0,0,0,0,0,0};
 gal_material_desc desc{sizeof(desc),GAL_MATERIAL_VERSION,uint32_t(fragment.size()),GAL_MATERIAL_PARAMETER_BYTES};
 uint64_t first=0,second=0;uint32_t live=99;
 R(gal_material_create_v1(c,&desc,fragment.data(),&first)==0&&first);R(backend_materials.size()==1&&backend_materials[0]==first);
 R(captured_fragment.size()==fragment.size()&&std::equal(fragment.begin(),fragment.end(),captured_fragment.begin()));
 // Failed backend creation can allocate first; both failure paths must release only that candidate.
 for(int mode=1;mode<=2;mode++){
  material_create_mode=mode;int prior_releases=material_release_calls;
  second=UINT64_MAX;R(gal_material_create_v1(c,&desc,fragment.data(),&second)==-1&&second==0);
  R(material_release_calls==prior_releases+1&&backend_materials.size()==1&&backend_materials[0]==first);
  R(gal_material_count(c,&live)==0&&live==1);
 }
 material_create_mode=0;int prior_creates=material_create_calls;auto malformed=desc;malformed.parameter_bytes=0;
 R(gal_material_create_v1(c,&malformed,fragment.data(),&second)==-1&&material_create_calls==prior_creates);
 R(gal_material_create_v1(c,&desc,fragment.data(),&second)==0&&second>first);R(gal_material_count(c,&live)==0&&live==2);
 gal_material_draw_v1 record{};record.size=sizeof(record);record.version=GAL_MATERIAL_DRAW_VERSION;record.sprite=region;record.material=first;
 for(uint32_t i=0;i<8;i++)record.parameters[i]=float(i)-2.25f;
 const gal_clip_rect a{sizeof(a),GAL_CLIP_VERSION,GAL_CLIP_ENABLED,0,10,20,30,40};
 const gal_clip_rect b{sizeof(b),GAL_CLIP_VERSION,GAL_CLIP_ENABLED,0,40,50,20,10};
 gal_material_draw_v1 draws[3]={record,record,record};gal_clip_rect clips[3]={a,b,a};
 R(gal_begin(c,&cam)==0);R(gal_submit_material_draws_v1(c,draws,2,&a,1)==0);R(gal_submit_material_draws_v1(c,&record,1,&a,1)==0);R(gal_end(c)==0);
 R(captured.size()==18&&captured_runs.size()==1&&captured_runs[0].first==0&&captured_runs[0].count==18&&captured_runs[0].material==first&&captured_runs[0].texture==region.draw.texture);
 for(uint32_t i=0;i<8;i++)R(captured_runs[0].parameters[i]==record.parameters[i]);
 // Each member of the key is significant. Equal nonadjacent runs never reorder or regroup.
 for(int key=0;key<11;key++){
  draws[0]=draws[1]=draws[2]=record;
  if(key==0)draws[1].material=second;
  else if(key==1){draws[1].sprite.draw.texture=0;draws[1].sprite.source_x=draws[1].sprite.source_y=draws[1].sprite.source_w=draws[1].sprite.source_h=0;}
  else if(key>=3)draws[1].parameters[key-3]+=1;
  R(gal_begin(c,&cam)==0);R(gal_submit_material_draws_v1(c,draws,3,key==2?clips:&a,key==2?3:1)==0);R(gal_end(c)==0);
  R(captured_runs.size()==3&&captured.size()==18);
  for(uint32_t i=0;i<3;i++){
   R(captured_runs[i].first==i*6&&captured_runs[i].count==6&&captured_runs[i].material==draws[i].material&&captured_runs[i].texture==draws[i].sprite.draw.texture);
   for(uint32_t p=0;p<8;p++)R(captured_runs[i].parameters[p]==draws[i].parameters[p]);
  }
  if(key==2)R(captured_runs[0].clip.x==a.x&&captured_runs[1].clip.x==b.x&&captured_runs[2].clip.x==a.x);
 }
 // Custom programs may observe signed zero, so the complete parameter bytes form the key.
 draws[0]=draws[1]=draws[2]=record;for(auto&draw:draws)for(auto&value:draw.parameters)value=0;
 draws[1].parameters[7]=-0.0f;
 R(gal_begin(c,&cam)==0);R(gal_submit_material_draws_v1(c,draws,3,nullptr,0)==0);R(gal_end(c)==0);
 R(captured_runs.size()==3&&!std::signbit(captured_runs[0].parameters[7])&&std::signbit(captured_runs[1].parameters[7])&&!std::signbit(captured_runs[2].parameters[7]));
 // Submitted records, uniform bytes and clip records are borrowed only during the call.
 auto mutable_record=record;auto mutable_clip=a;
 R(gal_begin(c,&cam)==0);R(gal_submit_material_draws_v1(c,&mutable_record,1,&mutable_clip,1)==0);
 mutable_record.parameters[0]=42;mutable_record.sprite.draw.tx+=10;mutable_clip=b;
 R(gal_submit_material_draws_v1(c,&mutable_record,1,&mutable_clip,1)==0);
 mutable_record.parameters[0]=-100;mutable_record.material=second;mutable_clip.x=-100;
 R(gal_end(c)==0);R(captured_runs.size()==2&&captured_runs[0].parameters[0]==record.parameters[0]&&captured_runs[1].parameters[0]==42);
 R(captured_runs[0].material==first&&captured_runs[1].material==first&&captured_runs[0].clip.x==a.x&&captured_runs[1].clip.x==b.x);
 R(std::abs(captured[6].x-captured[0].x-.2f)<1e-5f);
 // All four legacy calls reset the material and every parameter in a mixed frame.
 for(int legacy=0;legacy<4;legacy++){
  auto d=record;d.sprite.draw.texture=0;d.sprite.source_x=d.sprite.source_y=d.sprite.source_w=d.sprite.source_h=0;
  gal_sprite sprite{0,0,10,10,1,1,1,1};const gal_clip_rect*clip=legacy==3?&a:nullptr;uint32_t clip_count=legacy==3?1:0;
  R(gal_begin(c,&cam)==0);R(gal_submit_material_draws_v1(c,&d,1,clip,clip_count)==0);
  R((legacy==0?gal_submit(c,&sprite,1):legacy==1?gal_submit_draws(c,&d.sprite.draw,1):legacy==2?gal_submit_draws_v2(c,&d.sprite,1):gal_submit_draws_clipped_v1(c,&d.sprite,1,&a,1))==0);
  R(gal_submit_material_draws_v1(c,&d,1,clip,clip_count)==0);R(gal_end(c)==0);
  R(captured_runs.size()==3&&captured_runs[0].material==first&&captured_runs[1].material==0&&captured_runs[2].material==first&&zero_parameters(captured_runs[1]));
 }
 auto builtin=record;builtin.material=0;for(auto&value:builtin.parameters)value=-0.0f;
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_v2(c,&region,1)==0);R(gal_submit_material_draws_v1(c,&builtin,1,nullptr,0)==0);R(gal_submit_draws(c,&region.draw,1)==0);R(gal_end(c)==0);
 R(captured_runs.size()==1&&captured_runs[0].count==18&&captured_runs[0].material==0&&zero_parameters(captured_runs[0]));
 for(float value:captured_runs[0].parameters)R(!std::signbit(value));
 // Invalid late entries cannot alter an existing run or consume the two remaining slots.
 for(int invalid=0;invalid<16;invalid++){
  gal_material_draw_v1 candidates[2]={record,record};auto&bad=candidates[1];gal_clip_rect candidate_clips[2]={a,a};
  switch(invalid){case 0:bad.size--;break;case 1:bad.version++;break;case 2:bad.material=UINT64_MAX;break;case 3:bad.parameters[7]=NAN;break;
   case 4:bad.material=0;break;case 5:bad.sprite.size--;break;case 6:bad.sprite.draw.texture=UINT64_MAX;break;case 7:bad.sprite.source_x=INT32_MAX;break;
   case 8:bad.sprite.draw.r=2;break;case 9:bad.sprite.draw.m11=INFINITY;break;case 10:candidate_clips[1].reserved=1;break;case 11:candidate_clips[1].flags=2;break;
   case 12:candidate_clips[1].width=-1;break;case 13:candidate_clips[1].size--;break;case 14:candidate_clips[1].version++;break;case 15:candidate_clips[1].flags=0;break;}
  R(gal_begin(c,&cam)==0);R(gal_submit_material_draws_v1(c,&record,1,&b,1)==0);
  R(gal_submit_material_draws_v1(c,candidates,2,candidate_clips,2)==-1);
  candidates[1]=record;R(gal_submit_material_draws_v1(c,candidates,2,&b,1)==0);R(gal_end(c)==0);
  R(captured.size()==18&&captured_runs.size()==1&&captured_runs[0].count==18&&captured_runs[0].clip.x==b.x&&captured_runs[0].material==first);
  for(uint32_t p=0;p<8;p++)R(captured_runs[0].parameters[p]==record.parameters[p]);
 }
 // Active-frame mutations are rejected without backend calls, and abort clears the material state.
 prior_creates=material_create_calls;int prior_releases=material_release_calls;
 R(gal_begin(c,&cam)==0);R(gal_submit_material_draws_v1(c,&record,1,&a,1)==0);
 uint64_t candidate=0;R(gal_material_create_v1(c,&desc,fragment.data(),&candidate)==-1);R(gal_material_release(c,first)==-1);
 R(material_create_calls==prior_creates&&material_release_calls==prior_releases);R(gal_abort(c)==0);
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_v2(c,&region,1)==0);R(gal_end(c)==0);R(captured_runs.size()==1&&captured_runs[0].material==0&&zero_parameters(captured_runs[0]));
 R(gal_material_release(c,first)==0);R(gal_material_release(c,first)==-1);R(gal_material_count(c,&live)==0&&live==1&&backend_materials.size()==1&&backend_materials[0]==second);
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_v2(c,&region,1)==0);R(gal_submit_material_draws_v1(c,&record,1,nullptr,0)==-1);R(gal_end(c)==0);
 R(captured.size()==6&&captured_runs.size()==1&&captured_runs[0].material==0);
 retained=second;
 std::puts("PASS material backend rollback, adjacent run keys, copied parameters, legacy/default reset and atomic late-entry rejection");
 return 0;
}
static int clip_contract(gal_context*c,const gal_camera&cam,const gal_draw_v2&region){
 const gal_clip_rect off{sizeof(off),GAL_CLIP_VERSION,0,0,0,0,0,0};
 const gal_clip_rect a{sizeof(a),GAL_CLIP_VERSION,GAL_CLIP_ENABLED,0,10,20,30,40};
 const gal_clip_rect b{sizeof(b),GAL_CLIP_VERSION,GAL_CLIP_ENABLED,0,40,50,20,10};
 gal_draw_v2 draws[3]={region,region,region};gal_clip_rect clips[3]={a,b,a};
 gal_stats stats{sizeof(stats),0,0,0,0};
 R(gal_submit_draws_clipped_v1(c,draws,1,&a,1)==-1);
 // Alternating scissors keep painter order; identical adjacent clips merge across calls.
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_clipped_v1(c,draws,3,clips,3)==0);R(gal_end(c)==0);
 R(captured_runs.size()==3&&captured_runs[0].clip.x==10&&captured_runs[1].clip.x==40&&captured_runs[2].clip.x==10);
 for(uint32_t i=0;i<3;i++)R(captured_runs[i].first==i*6&&captured_runs[i].count==6&&captured_runs[i].texture==region.draw.texture);
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_clipped_v1(c,draws,2,&a,1)==0);R(gal_submit_draws_clipped_v1(c,draws,1,&a,1)==0);R(gal_end(c)==0);
 R(captured_runs.size()==1&&captured_runs[0].count==18&&submitted_scissors.size()==1&&submitted_scissors[0].width==30);
 // Texture alternation must not be regrouped, even under one broadcast scissor.
 draws[1].draw.texture=0;draws[1].source_x=draws[1].source_y=draws[1].source_w=draws[1].source_h=0;
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_clipped_v1(c,draws,3,&a,1)==0);R(gal_end(c)==0);
 R(captured_runs.size()==3&&captured_runs[0].texture==region.draw.texture&&captured_runs[1].texture==0&&captured_runs[2].texture==region.draw.texture);
 draws[1]=region;
 // Each legacy entry point resets clip state and cannot merge through a clipped run.
 for(int legacy=0;legacy<3;legacy++){
  auto d=region;if(legacy==0){d.draw.texture=0;d.source_x=d.source_y=d.source_w=d.source_h=0;}
  gal_sprite sprite{0,0,10,10,1,1,1,1};
  R(gal_begin(c,&cam)==0);R(gal_submit_draws_clipped_v1(c,&d,1,&a,1)==0);
  R((legacy==0?gal_submit(c,&sprite,1):legacy==1?gal_submit_draws(c,&d.draw,1):gal_submit_draws_v2(c,&d,1))==0);
  R(gal_submit_draws_clipped_v1(c,&d,1,&a,1)==0);R(gal_end(c)==0);
  R(captured_runs.size()==3&&captured_runs[0].clip.flags==GAL_CLIP_ENABLED&&captured_runs[1].clip.flags==0&&captured_runs[2].clip.flags==GAL_CLIP_ENABLED);
  R(submitted_scissors.size()==3&&submitted_scissors[1].x==0&&submitted_scissors[1].y==0&&submitted_scissors[1].width==100&&submitted_scissors[1].height==100);
 }
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_v2(c,&region,1)==0);R(gal_submit_draws_clipped_v1(c,&region,1,&off,1)==0);R(gal_submit_draws_clipped_v1(c,&region,1,nullptr,0)==0);R(gal_end(c)==0);
 R(captured_runs.size()==1&&captured_runs[0].count==18&&captured_runs[0].clip.flags==0);
 // Invalid second clip leaves both the preceding batch and remaining capacity intact.
 for(int invalid=0;invalid<10;invalid++){
  gal_clip_rect candidates[2]={a,a};auto&bad=candidates[1];
  switch(invalid){case 0:bad.size--;break;case 1:bad.version++;break;case 2:bad.flags=2;break;case 3:bad.reserved=1;break;case 4:bad.width=-1;break;case 5:bad.height=-1;break;
   case 6:bad=off;bad.x=1;break;case 7:bad=off;bad.y=-1;break;case 8:bad=off;bad.width=1;break;case 9:bad=off;bad.height=1;break;}
  R(gal_begin(c,&cam)==0);R(gal_submit_draws_clipped_v1(c,&region,1,&b,1)==0);
  R(gal_submit_draws_clipped_v1(c,draws,2,candidates,2)==-1);R(gal_submit_draws_v2(c,&region,1)==0);R(gal_end(c)==0);
  R(captured.size()==12&&captured_runs.size()==2&&captured_runs[0].clip.x==b.x&&captured_runs[1].clip.flags==0);
 }
 // Clip validation does not weaken existing affine, source-region, or capacity checks.
 for(int invalid=0;invalid<6;invalid++){
  gal_draw_v2 candidates[2]={region,region};auto&bad=candidates[1];
  switch(invalid){case 0:bad.size--;break;case 1:bad.draw.r=2;break;case 2:bad.draw.texture=UINT64_MAX;break;case 3:bad.source_x=INT32_MAX;break;case 4:bad.draw.m11=INFINITY;break;case 5:bad.flags=4;break;}
  R(gal_begin(c,&cam)==0);R(gal_submit_draws_v2(c,&region,1)==0);R(gal_submit_draws_clipped_v1(c,candidates,2,&a,1)==-1);R(gal_end(c)==0);
  R(captured.size()==6&&captured_runs.size()==1&&captured_runs[0].clip.flags==0);
 }
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_clipped_v1(c,draws,3,&a,1)==0);R(gal_submit_draws_clipped_v1(c,&region,1,&b,1)==-1);R(gal_end(c)==0);
 R(captured.size()==18&&captured_runs.size()==1&&captured_runs[0].clip.x==a.x);
 // Null/count combinations and zero-draw calls still validate all supplied clips.
 R(gal_begin(c,&cam)==0);
 R(gal_submit_draws_clipped_v1(c,nullptr,0,nullptr,0)==0);R(gal_submit_draws_clipped_v1(c,nullptr,0,&a,1)==0);
 R(gal_submit_draws_clipped_v1(c,nullptr,1,&a,1)==-1);R(gal_submit_draws_clipped_v1(c,draws,1,nullptr,1)==-1);
 R(gal_submit_draws_clipped_v1(c,draws,1,clips,2)==-1);R(gal_submit_draws_clipped_v1(c,draws,3,clips,2)==-1);R(gal_submit_draws_clipped_v1(c,nullptr,0,clips,2)==-1);
 auto malformed=a;malformed.version=0;R(gal_submit_draws_clipped_v1(c,nullptr,0,&malformed,1)==-1);R(gal_end(c)==0);
 R(captured.empty()&&captured_runs.empty()&&submitted_scissors.empty());
 // Empty intersections consume sprite capacity, but only visible runs count as draws.
 gal_clip_rect bounds[3]={{sizeof(a),GAL_CLIP_VERSION,GAL_CLIP_ENABLED,0,INT32_MAX,INT32_MAX,INT32_MAX,INT32_MAX},a,{sizeof(a),GAL_CLIP_VERSION,GAL_CLIP_ENABLED,0,-10,-20,30,50}};
 R(gal_get_stats(c,&stats)==0);auto prior=stats;
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_clipped_v1(c,draws,3,bounds,3)==0);R(gal_end(c)==0);R(gal_get_stats(c,&stats)==0);
 R(stats.draw_calls==prior.draw_calls+2&&stats.sprites==prior.sprites+3&&captured_runs.size()==3&&submitted_scissors.size()==2);
 R(submitted_scissors[1].x==0&&submitted_scissors[1].y==0&&submitted_scissors[1].width==20&&submitted_scissors[1].height==30);
 bounds[0]={sizeof(a),GAL_CLIP_VERSION,GAL_CLIP_ENABLED,0,20,30,0,40};bounds[1]={sizeof(a),GAL_CLIP_VERSION,GAL_CLIP_ENABLED,0,20,30,40,0};bounds[2]={sizeof(a),GAL_CLIP_VERSION,GAL_CLIP_ENABLED,0,INT32_MIN,INT32_MIN,INT32_MAX,INT32_MAX};
 prior=stats;R(gal_begin(c,&cam)==0);R(gal_submit_draws_clipped_v1(c,draws,3,bounds,3)==0);R(gal_end(c)==0);R(gal_get_stats(c,&stats)==0);
 R(stats.draw_calls==prior.draw_calls&&stats.sprites==prior.sprites+3&&captured_runs.size()==3&&submitted_scissors.empty());
 // Frame boundaries and aborts must not retain clip state.
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_clipped_v1(c,&region,1,&a,1)==0);R(gal_abort(c)==0);
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_v2(c,&region,1)==0);R(gal_end(c)==0);R(captured_runs.size()==1&&captured_runs[0].clip.flags==0);
 struct Case{int32_t x,y,w,h,fw,fh,ex,ey,ew,eh;};
 const Case cases[]={
  {-10,-20,30,50,100,100,0,0,20,30},{90,95,INT32_MAX,INT32_MAX,100,100,90,95,10,5},
  {INT32_MAX,INT32_MAX,INT32_MAX,INT32_MAX,100,100,100,100,0,0},{INT32_MIN,INT32_MIN,INT32_MAX,INT32_MAX,100,100,0,0,0,0},
  {-1,-1,INT32_MAX,INT32_MAX,100,100,0,0,100,100},{0,0,0,100,100,100,0,0,0,100},
  {0,0,100,0,100,100,0,0,100,0},{100,0,1,100,100,100,100,0,0,100},
  {0,100,100,1,100,100,0,100,100,0},{0,0,INT32_MAX,INT32_MAX,INT32_MAX,INT32_MAX,0,0,INT32_MAX,INT32_MAX},
  {0,0,100,100,0,100,0,0,0,0},{0,0,100,100,100,-1,0,0,0,0}
 };
 for(const auto&test:cases){gal_clip_rect clip{sizeof(clip),GAL_CLIP_VERSION,GAL_CLIP_ENABLED,0,test.x,test.y,test.w,test.h};auto rect=intersect_clip(clip,test.fw,test.fh);R(rect.x==test.ex&&rect.y==test.ey&&rect.width==test.ew&&rect.height==test.eh);}
 auto rect=intersect_clip(off,INT32_MAX,INT32_MAX);R(rect.x==0&&rect.y==0&&rect.width==INT32_MAX&&rect.height==INT32_MAX);
 std::puts("PASS world scissor run order/merging/legacy reset, atomic rejection, counts, int32 bounds/clamp/empty intersections");
 return 0;
}
int main(){
 gal_config config{sizeof(config),1,100,100,3,GAL_AUDIO};gal_context*c=nullptr;gal_camera cam{0,0,1};gal_sprite s{10,20,30,40,1,.5f,.25f,.75f};gal_stats stats{sizeof(stats),0,0,0,0};gal_input input{sizeof(input),0,0,0,0,0,0,0};
 create_mode=1;R(gal_create(&config,&c)==-1&&c==nullptr);create_mode=2;R(gal_create(&config,&c)==-1&&c==nullptr);create_mode=0;R(gal_create(&config,&c)==0);
 R(gal_begin(c,&cam)==0);R(gal_submit(c,&s,1)==0);R(gal_end(c)==0);R(captured.size()==6);R(std::abs(captured[0].x+.8f)<1e-5f&&std::abs(captured[0].y-.6f)<1e-5f);R(std::abs(captured[5].x+.8f)<1e-5f&&std::abs(captured[5].y+.2f)<1e-5f);
 auto huge=s;huge.x=std::numeric_limits<float>::max();R(gal_begin(c,&cam)==0);R(gal_submit(c,&huge,1)==-1);R(gal_abort(c)==0);R(gal_get_stats(c,&stats)==0&&stats.frames==1&&stats.sprites==1&&stats.draw_calls==1);
 for(int mode=1;mode<=2;mode++){draw_mode=mode;R(gal_begin(c,&cam)==0);R(gal_submit(c,&s,1)==0);R(gal_end(c)==-1);R(gal_begin(c,&cam)==0);R(gal_abort(c)==0);R(gal_get_stats(c,&stats)==0&&stats.frames==1);}
 draw_mode=3;R(gal_begin(c,&cam)==0);R(gal_submit(c,&s,1)==0);R(gal_end(c)==0);R(gal_get_stats(c,&stats)==0&&stats.draw_calls==1);draw_mode=0;
 for(int mode=1;mode<=2;mode++){poll_mode=mode;R(gal_poll(c,&input)==-1);}poll_mode=0;R(gal_poll(c,&input)==0);R(gal_play_tone(c)==-1);
 R(gal_begin(c,&cam)==0);R(gal_submit(c,&s,1)==0);poll_mode=3;R(gal_poll(c,&input)==-1);R(gal_submit(c,&s,1)==0);R(gal_end(c)==0);R(captured[0].x==captured[6].x);
 uint64_t texture=0;R(gal_texture_load_bmp(c,"test.bmp",&texture)==0&&texture!=0);uint32_t live=0;R(gal_texture_count(c,&live)==0&&live==1);
 gal_draw affine{0,1,-1,0,50,20,10,20,1,1,1,1,texture};
 R(gal_begin(c,&cam)==0);R(gal_submit_draws(c,&affine,1)==0);R(gal_texture_release(c,texture)==-1);R(gal_end(c)==0);
 R(captured.size()==6&&std::abs(captured[0].x)<1e-5f&&std::abs(captured[0].y-.6f)<1e-5f&&std::abs(captured[2].x+.4f)<1e-5f);
 gal_texture_info info{sizeof(info),-1,-1,0};R(gal_texture_get_info(c,texture,&info)==0&&info.width==8&&info.height==4);
 info.reserved=1;R(gal_texture_get_info(c,texture,&info)==-1);info.reserved=0;R(gal_texture_get_info(c,0,&info)==-1);
 gal_draw_v2 region{sizeof(region),GAL_DRAW_VERSION,affine,0,0,4,4,0,0};
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_v2(c,&region,1)==0);region.source_x=4;region.flags=GAL_FLIP_X|GAL_FLIP_Y;R(gal_submit_draws_v2(c,&region,1)==0);R(gal_end(c)==0);
 R(captured.size()==12&&captured[0].u==.0625f&&captured[0].v==.125f&&captured[1].u==.4375f&&captured[2].v==.875f);
 R(captured[6].u==.9375f&&captured[6].v==.875f&&captured[7].u==.5625f&&captured[8].v==.125f);
 for(int invalid=0;invalid<10;invalid++){
  gal_draw_v2 candidates[2]={region,region};auto&bad=candidates[1];
  switch(invalid){case 0:bad.size--;break;case 1:bad.version++;break;case 2:bad.reserved=1;break;case 3:bad.flags=4;break;case 4:bad.source_x=-1;break;case 5:bad.source_w=0;break;case 6:bad.source_h=5;break;case 7:bad.source_x=INT32_MAX;break;case 8:bad.draw.texture=0;break;case 9:bad.draw.r=2;break;}
  R(gal_begin(c,&cam)==0);R(gal_submit_draws(c,&affine,1)==0);R(gal_submit_draws_v2(c,candidates,2)==-1);R(gal_end(c)==0);R(captured.size()==6); // no partial append after invalid second draw.
 }
 region.source_x=7;region.source_y=3;region.source_w=region.source_h=1;region.flags=0;
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_v2(c,&region,1)==0);R(gal_end(c)==0);R(captured[0].u==captured[1].u&&captured[0].v==captured[2].v);
 region.source_x=region.source_y=region.source_w=region.source_h=0;
 R(gal_begin(c,&cam)==0);R(gal_submit_draws_v2(c,&region,1)==0);R(gal_end(c)==0);R(captured[0].u==0&&captured[1].u==1);
 R(gal_submit_draws_v2(c,&region,1)==-1);
 region.source_w=4;region.source_h=4;
 R(gal_get_stats(c,&stats)==0);uint32_t previous_draws=stats.draw_calls;
 R(gal_begin(c,&cam)==0);R(gal_submit_draws(c,&affine,1)==0);R(gal_submit_draws_v2(c,&region,1)==0);R(gal_end(c)==0);
 R(captured.size()==12&&captured[0].u==0&&captured[6].u==.0625f);R(gal_get_stats(c,&stats)==0&&stats.draw_calls==previous_draws+1);
 R(clip_contract(c,cam,region)==0);
 uint64_t retained_material=0;R(material_contract(c,cam,region,retained_material)==0);
 R(gal_texture_release(c,texture)==0);R(gal_texture_release(c,texture)==-1);R(gal_texture_count(c,&live)==0&&live==0);R(gal_texture_get_info(c,texture,&info)==-1);
 R(gal_begin(c,&cam)==0);R(gal_submit_draws(c,&affine,1)==-1);R(gal_abort(c)==0);
 gal_input_v2 versioned{};versioned.size=sizeof(versioned);versioned.version=GAL_INPUT_VERSION;versioned.quit=77;
 for(int mode=1;mode<=2;mode++){poll_mode=mode;R(gal_poll_v2(c,&versioned)==-1&&versioned.quit==77);}
 poll_mode=3;R(gal_poll_v2(c,&versioned)==0&&versioned.pixel_width==200&&versioned.window_width==100);
 R(gal_begin(c,&cam)==0);R(gal_submit(c,&s,1)==0);R(gal_end(c)==0);R(std::abs(captured[0].x+.9f)<1e-5f);
 poll_mode=4;R(gal_poll_v2(c,&versioned)==0&&!(versioned.flags&GAL_INPUT_DRAWABLE)&&versioned.pixel_width==0);
 R(gal_begin(c,&cam)==0);R(gal_submit(c,&s,1)==0);R(gal_end(c)==0);R(std::abs(captured[0].x+.9f)<1e-5f); // last valid projection survives zero viewport.
 int wrong=0;std::thread t([&]{wrong=gal_destroy(c);});t.join();R(wrong==-1);R(gal_destroy(c)==0&&destroyed==1&&backend_materials.empty());R(gal_destroy(c)==-1);
 // Recreating a backend drops all old materials and does not recycle their handles.
 R(gal_create(&config,&c)==0);R(gal_material_count(c,&live)==0&&live==0);R(gal_material_release(c,retained_material)==-1);
 const uint8_t fragment[]={3,2,35,7,0,0,1,0,0,0,0,0,1,0,0,0,0,0,0,0};gal_material_desc desc{sizeof(desc),GAL_MATERIAL_VERSION,sizeof(fragment),GAL_MATERIAL_PARAMETER_BYTES};uint64_t fresh=0;
 R(gal_material_create_v1(c,&desc,fragment,&fresh)==0&&fresh>retained_material);R(backend_materials.size()==1&&backend_materials[0]==fresh);
 R(gal_destroy(c)==0&&destroyed==2&&backend_materials.empty());
 std::puts("PASS injected create/poll/draw/tone failures, state recovery, geometry, overflow, abort stats, skipped draw stats, wrong-thread destroy");
}

bool backend_texture_load(Backend*,const char*,uint64_t,int32_t&w,int32_t&h,std::string&){w=8;h=4;return true;}
void backend_texture_release(Backend*,uint64_t){}
