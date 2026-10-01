#include "gal.h"
#include <cassert>
#include <cmath>
#include <cstring>
#include <iostream>
#include <thread>
#define REQUIRE(x) do { if(!(x)) { std::cerr<<"FAIL line "<<__LINE__<<": "<<#x<<" error="<<gal_last_error()<<'\n'; return 1; } } while(0)
int main(){
 static_assert(sizeof(gal_config)==24 && sizeof(gal_sprite)==32 && sizeof(gal_input)==32 && sizeof(gal_stats)==20,"ABI sizes");
 static_assert(sizeof(gal_draw_v2)==88&&sizeof(gal_clip_rect)==32,"additive draw and clip ABI sizes");
 gal_config config{sizeof(config),1,640,480,64,GAL_HEADLESS};
 REQUIRE(gal_abi_version()==1);
 REQUIRE(gal_create(nullptr,nullptr)==-1);
 REQUIRE(gal_destroy(nullptr)==-1);
 for(int repeat=0;repeat<100;repeat++){
  gal_context*c=nullptr;REQUIRE(gal_create(&config,&c)==0);REQUIRE(c);
  REQUIRE(std::strcmp(gal_backend(c),"headless-validation")==0);
  gal_context*duplicate=nullptr;REQUIRE(gal_create(&config,&duplicate)==-1);REQUIRE(!duplicate);
  gal_stats stats{};stats.size=sizeof(stats);int status=0;std::thread wrong([&]{status=gal_get_stats(c,&stats);});wrong.join();REQUIRE(status==-1);
  gal_input input{};input.size=sizeof(input);REQUIRE(gal_poll(c,&input)==0);REQUIRE(input.width==640 && input.height==480);
  gal_camera camera{0,0,1};gal_sprite sprite{10,20,30,40,1,0.5f,0,0.6f};
  REQUIRE(gal_end(c)==-1);REQUIRE(gal_submit(c,&sprite,1)==-1);
  camera.zoom=NAN;REQUIRE(gal_begin(c,&camera)==-1);camera.zoom=1;
  REQUIRE(gal_begin(c,&camera)==0);REQUIRE(gal_begin(c,&camera)==-1);REQUIRE(gal_poll(c,&input)==-1);
  REQUIRE(gal_submit(c,nullptr,0)==0);REQUIRE(gal_submit(c,nullptr,1)==-1);
  REQUIRE(gal_submit(c,&sprite,65)==-1);
  sprite.x=INFINITY;REQUIRE(gal_submit(c,&sprite,1)==-1);sprite.x=10;
  REQUIRE(gal_submit(c,&sprite,1)==0);REQUIRE(gal_end(c)==0);
  REQUIRE(gal_get_stats(c,&stats)==0);REQUIRE(stats.frames==1 && stats.sprites==1 && stats.draw_calls==0);
  REQUIRE(gal_abort(c)==-1);REQUIRE(gal_begin(c,&camera)==0);REQUIRE(gal_submit(c,&sprite,1)==0);REQUIRE(gal_abort(c)==0);REQUIRE(gal_get_stats(c,&stats)==0);REQUIRE(stats.frames==1);
  gal_draw_v2 draw{sizeof(draw),GAL_DRAW_VERSION,{1,0,0,1,0,0,10,10,1,1,1,1,0},0,0,0,0,0,0};
  gal_clip_rect clip{sizeof(clip),GAL_CLIP_VERSION,GAL_CLIP_ENABLED,0,INT32_MIN,INT32_MAX,INT32_MAX,0};
  REQUIRE(gal_submit_draws_clipped_v1(c,&draw,1,&clip,1)==-1);REQUIRE(gal_begin(c,&camera)==0);
  REQUIRE(gal_submit_draws_clipped_v1(c,&draw,1,&clip,1)==0);REQUIRE(gal_submit_draws_clipped_v1(c,nullptr,0,nullptr,0)==0);
  clip.reserved=1;REQUIRE(gal_submit_draws_clipped_v1(c,nullptr,0,&clip,1)==-1);REQUIRE(gal_end(c)==0);
  REQUIRE(gal_get_stats(c,&stats)==0);REQUIRE(stats.frames==2&&stats.sprites==2&&stats.draw_calls==0);
  REQUIRE(gal_play_tone(c)==-1);REQUIRE(gal_destroy(c)==0);REQUIRE(gal_destroy(c)==-1);
 }
 std::cout<<"PASS native ABI/state/thread/lifecycle contracts (100 recreate cycles)\n";
}
