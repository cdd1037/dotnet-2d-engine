#include "gal.h"
#include "backend.h"
#include "clip_rect.h"
#include <cstdio>
#include <cmath>
#include <limits>
#include <stdexcept>
#include <vector>
#include <thread>
struct Backend{int32_t width,height;};
static int create_mode=0,draw_mode=0, poll_mode=0,destroyed=0;
static std::vector<Vertex> captured;
static std::vector<DrawRun> captured_runs;
static std::vector<ScissorRect> submitted_scissors;
Backend* backend_create(gal_config&config,std::string& e){if(create_mode==1){e="injected creation error";return nullptr;}if(create_mode==2)throw std::runtime_error("create");return new Backend{config.width,config.height};}
void backend_destroy(Backend*b){if(b)++destroyed;delete b;}
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
#define R(x) do{if(!(x)){std::fprintf(stderr,"FAIL %d %s error=%s\n",__LINE__,#x,gal_last_error());return 1;}}while(0)
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
 R(gal_texture_release(c,texture)==0);R(gal_texture_release(c,texture)==-1);R(gal_texture_count(c,&live)==0&&live==0);R(gal_texture_get_info(c,texture,&info)==-1);
 R(gal_begin(c,&cam)==0);R(gal_submit_draws(c,&affine,1)==-1);R(gal_abort(c)==0);
 gal_input_v2 versioned{};versioned.size=sizeof(versioned);versioned.version=GAL_INPUT_VERSION;versioned.quit=77;
 for(int mode=1;mode<=2;mode++){poll_mode=mode;R(gal_poll_v2(c,&versioned)==-1&&versioned.quit==77);}
 poll_mode=3;R(gal_poll_v2(c,&versioned)==0&&versioned.pixel_width==200&&versioned.window_width==100);
 R(gal_begin(c,&cam)==0);R(gal_submit(c,&s,1)==0);R(gal_end(c)==0);R(std::abs(captured[0].x+.9f)<1e-5f);
 poll_mode=4;R(gal_poll_v2(c,&versioned)==0&&!(versioned.flags&GAL_INPUT_DRAWABLE)&&versioned.pixel_width==0);
 R(gal_begin(c,&cam)==0);R(gal_submit(c,&s,1)==0);R(gal_end(c)==0);R(std::abs(captured[0].x+.9f)<1e-5f); // last valid projection survives zero viewport.
 int wrong=0;std::thread t([&]{wrong=gal_destroy(c);});t.join();R(wrong==-1);R(gal_destroy(c)==0&&destroyed==1);R(gal_destroy(c)==-1);
 std::puts("PASS injected create/poll/draw/tone failures, state recovery, geometry, overflow, abort stats, skipped draw stats, wrong-thread destroy");
}

bool backend_texture_load(Backend*,const char*,uint64_t,int32_t&w,int32_t&h,std::string&){w=8;h=4;return true;}
void backend_texture_release(Backend*,uint64_t){}
