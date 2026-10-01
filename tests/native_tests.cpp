#include "gal.h"
#include <algorithm>
#include <cassert>
#include <array>
#include <cmath>
#include <cstddef>
#include <cstring>
#include <iostream>
#include <thread>
#include <vector>
#define REQUIRE(x) do { if(!(x)) { std::cerr<<"FAIL line "<<__LINE__<<": "<<#x<<" error="<<gal_last_error()<<'\n'; return 1; } } while(0)
static void material_word(uint8_t* bytes,uint32_t offset,uint32_t value){
 for(uint32_t i=0;i<4;i++)bytes[offset+i]=uint8_t(value>>(i*8));
}
static int material_contract(){
 static_assert(GAL_MATERIAL_VERSION==1&&GAL_MATERIAL_DRAW_VERSION==1&&GAL_MATERIAL_PARAMETER_BYTES==32&&GAL_MATERIAL_CAPACITY==64,"material constants");
 static_assert(sizeof(gal_material_desc)==16&&offsetof(gal_material_desc,fragment_bytes)==8&&offsetof(gal_material_desc,parameter_bytes)==12,"material descriptor ABI");
 static_assert(sizeof(gal_material_draw_v1)==136&&offsetof(gal_material_draw_v1,sprite)==8&&offsetof(gal_material_draw_v1,material)==96&&offsetof(gal_material_draw_v1,parameters)==104,"material draw ABI");
 // Header-only blobs are intentionally accepted by headless validation. Compilation belongs to the graphics backend.
 const std::array<uint8_t,20> fragment={3,2,35,7,0,0,1,0,0,0,0,0,1,0,0,0,0,0,0,0};
 gal_material_desc desc{sizeof(desc),GAL_MATERIAL_VERSION,uint32_t(fragment.size()),GAL_MATERIAL_PARAMETER_BYTES};
 gal_config config{sizeof(config),1,64,64,3,GAL_HEADLESS};gal_context*c=nullptr;gal_camera camera{0,0,1};
 uint64_t id=77;uint32_t live=99;
 REQUIRE(gal_material_create_v1(nullptr,&desc,fragment.data(),&id)==-1&&id==0);
 REQUIRE(gal_material_release(nullptr,1)==-1);REQUIRE(gal_material_count(nullptr,&live)==-1);
 REQUIRE(gal_submit_material_draws_v1(nullptr,nullptr,0,nullptr,0)==-1);
 REQUIRE(gal_create(&config,&c)==0);REQUIRE(gal_material_count(c,&live)==0&&live==0);
 id=77;REQUIRE(gal_material_create_v1(c,nullptr,fragment.data(),&id)==-1&&id==0);
 REQUIRE(gal_material_create_v1(c,&desc,nullptr,&id)==-1);REQUIRE(gal_material_create_v1(c,&desc,fragment.data(),nullptr)==-1);
 REQUIRE(gal_material_count(c,nullptr)==-1);REQUIRE(gal_material_release(c,0)==-1);REQUIRE(gal_material_release(c,UINT64_MAX)==-1);
 std::vector<uint8_t> large(256u*1024u+4);std::copy(fragment.begin(),fragment.end(),large.begin());
 for(int invalid=0;invalid<11;invalid++){
  auto bad=desc;
  switch(invalid){case 0:bad.size--;break;case 1:bad.size++;break;case 2:bad.version=0;break;case 3:bad.version++;break;
   case 4:bad.parameter_bytes=0;break;case 5:bad.parameter_bytes--;break;case 6:bad.parameter_bytes++;break;
   case 7:bad.fragment_bytes=0;break;case 8:bad.fragment_bytes=16;break;case 9:bad.fragment_bytes=21;break;case 10:bad.fragment_bytes=uint32_t(large.size());break;}
  id=77;REQUIRE(gal_material_create_v1(c,&bad,large.data(),&id)==-1&&id==0);REQUIRE(gal_material_count(c,&live)==0&&live==0);
 }
 struct HeaderCase{uint32_t offset,value;};
 const HeaderCase invalid_headers[]={{0,0},{0,0x03022307},{4,0},{4,0x00010001},{4,0x00010100},{12,0},{12,0x00400000},{12,UINT32_MAX},{16,1}};
 for(const auto&test:invalid_headers){auto bad=fragment;material_word(bad.data(),test.offset,test.value);REQUIRE(gal_material_create_v1(c,&desc,bad.data(),&id)==-1);REQUIRE(gal_material_count(c,&live)==0&&live==0);}
 // Both inclusive size/bound limits and a byte-aligned input pointer are valid.
 auto maximum=desc;maximum.fragment_bytes=256u*1024u;material_word(large.data(),12,0x003fffff);
 REQUIRE(gal_material_create_v1(c,&maximum,large.data(),&id)==0&&id);uint64_t previous=id;REQUIRE(gal_material_release(c,id)==0);
 std::array<uint8_t,21> unaligned{};std::copy(fragment.begin(),fragment.end(),unaligned.begin()+1);
 REQUIRE(gal_material_create_v1(c,&desc,unaligned.data()+1,&id)==0&&id>previous);previous=id;
 uint64_t failed=77;int create_status=0,release_status=0,count_status=0;
 std::thread wrong([&]{create_status=gal_material_create_v1(c,&desc,fragment.data(),&failed);release_status=gal_material_release(c,id);count_status=gal_material_count(c,&live);});wrong.join();
 REQUIRE(create_status==-1&&release_status==-1&&count_status==-1&&failed==0);REQUIRE(gal_material_count(c,&live)==0&&live==1);
 gal_material_draw_v1 draw{};draw.size=sizeof(draw);draw.version=GAL_MATERIAL_DRAW_VERSION;draw.material=id;
 draw.sprite={sizeof(gal_draw_v2),GAL_DRAW_VERSION,{1,0,0,1,0,0,10,10,1,1,1,1,0},0,0,0,0,0,0};
 for(uint32_t i=0;i<8;i++)draw.parameters[i]=float(i)-3.5f;
 const gal_clip_rect clip{sizeof(clip),GAL_CLIP_VERSION,GAL_CLIP_ENABLED,0,0,0,10,10};
 REQUIRE(gal_submit_material_draws_v1(c,&draw,1,nullptr,0)==-1);REQUIRE(gal_begin(c,&camera)==0);
 failed=77;REQUIRE(gal_material_create_v1(c,&desc,fragment.data(),&failed)==-1&&failed==0);REQUIRE(gal_material_release(c,id)==-1);REQUIRE(gal_material_count(c,&live)==0&&live==1);
 int draw_status=0;std::thread wrong_draw([&]{draw_status=gal_submit_material_draws_v1(c,&draw,1,nullptr,0);});wrong_draw.join();REQUIRE(draw_status==-1);
 REQUIRE(gal_submit_material_draws_v1(c,nullptr,0,nullptr,0)==0);REQUIRE(gal_submit_material_draws_v1(c,nullptr,0,&clip,1)==0);
 REQUIRE(gal_submit_material_draws_v1(c,nullptr,1,nullptr,0)==-1);REQUIRE(gal_submit_material_draws_v1(c,&draw,1,nullptr,1)==-1);
 gal_clip_rect clips[2]={clip,clip};REQUIRE(gal_submit_material_draws_v1(c,&draw,1,clips,2)==-1);REQUIRE(gal_submit_material_draws_v1(c,nullptr,0,clips,2)==-1);
 auto bad_clip=clip;bad_clip.reserved=1;REQUIRE(gal_submit_material_draws_v1(c,nullptr,0,&bad_clip,1)==-1);
 REQUIRE(gal_submit_material_draws_v1(c,&draw,1,&clip,1)==0);REQUIRE(gal_end(c)==0);
 gal_stats stats{sizeof(stats),0,0,0,0};REQUIRE(gal_get_stats(c,&stats)==0&&stats.frames==1&&stats.sprites==1&&stats.draw_calls==0);
 // A bad late entry must leave the preceding work and all remaining capacity intact.
 for(int invalid=0;invalid<24;invalid++){
  gal_material_draw_v1 candidates[2]={draw,draw};auto&bad=candidates[1];
  switch(invalid){case 0:bad.size--;break;case 1:bad.version=0;break;case 2:bad.material=UINT64_MAX;break;
   case 3:bad.material=0;break;case 4:bad.sprite.size--;break;case 5:bad.sprite.version++;break;case 6:bad.sprite.reserved=1;break;
   case 7:bad.sprite.flags=4;break;case 8:bad.sprite.draw.texture=UINT64_MAX;break;case 9:bad.sprite.draw.m11=INFINITY;break;
   case 10:bad.sprite.draw.r=2;break;case 11:bad.sprite.draw.w=-1;break;case 12:bad.sprite.source_x=1;break;
   case 13:bad.sprite.draw.w=3.4e38f;bad.sprite.draw.m11=3.4e38f;break;case 14:bad.parameters[0]=INFINITY;break;case 15:bad.parameters[7]=-INFINITY;break;
   default:bad.parameters[invalid-16]=NAN;break;}
  REQUIRE(gal_begin(c,&camera)==0);REQUIRE(gal_submit_material_draws_v1(c,&draw,1,&clip,1)==0);
  REQUIRE(gal_submit_material_draws_v1(c,candidates,2,&clip,1)==-1);
  candidates[1]=draw;REQUIRE(gal_submit_material_draws_v1(c,candidates,2,nullptr,0)==0);REQUIRE(gal_end(c)==0);
  REQUIRE(gal_get_stats(c,&stats)==0&&stats.sprites==1+3u*uint32_t(invalid+1));
 }
 REQUIRE(gal_begin(c,&camera)==0);REQUIRE(gal_submit_material_draws_v1(c,&draw,UINT32_MAX,nullptr,0)==-1);REQUIRE(gal_abort(c)==0);
 auto builtin=draw;builtin.material=0;for(auto&value:builtin.parameters)value=0;
 REQUIRE(gal_begin(c,&camera)==0);REQUIRE(gal_submit_material_draws_v1(c,&builtin,1,nullptr,0)==0);REQUIRE(gal_abort(c)==0);
 REQUIRE(gal_material_release(c,id)==0);REQUIRE(gal_material_release(c,id)==-1);REQUIRE(gal_material_count(c,&live)==0&&live==0);
 REQUIRE(gal_begin(c,&camera)==0);REQUIRE(gal_submit_material_draws_v1(c,&draw,1,nullptr,0)==-1);REQUIRE(gal_abort(c)==0);
 std::array<uint64_t,GAL_MATERIAL_CAPACITY> handles{};
 for(auto&handle:handles){REQUIRE(gal_material_create_v1(c,&desc,fragment.data(),&handle)==0&&handle>previous);previous=handle;}
 REQUIRE(gal_material_count(c,&live)==0&&live==GAL_MATERIAL_CAPACITY);failed=77;REQUIRE(gal_material_create_v1(c,&desc,fragment.data(),&failed)==-1&&failed==0);
 REQUIRE(gal_material_release(c,handles[17])==0);REQUIRE(gal_material_create_v1(c,&desc,fragment.data(),&id)==0&&id>previous);previous=id;
 REQUIRE(gal_material_release(c,handles[17])==-1);REQUIRE(gal_material_count(c,&live)==0&&live==GAL_MATERIAL_CAPACITY);
 REQUIRE(gal_destroy(c)==0);REQUIRE(gal_material_release(c,id)==-1);REQUIRE(gal_material_count(c,&live)==-1);
 REQUIRE(gal_create(&config,&c)==0);REQUIRE(gal_material_count(c,&live)==0&&live==0);REQUIRE(gal_material_release(c,id)==-1);
 REQUIRE(gal_material_create_v1(c,&desc,fragment.data(),&id)==0&&id>previous);draw.material=handles[0];
 REQUIRE(gal_begin(c,&camera)==0);REQUIRE(gal_submit_material_draws_v1(c,&draw,1,nullptr,0)==-1);REQUIRE(gal_abort(c)==0);REQUIRE(gal_destroy(c)==0);
 std::cout<<"PASS material ABI/header/parameter validation, atomic batches, capacity, frame/thread ownership and stale/context lifecycle contracts\n";
 return 0;
}
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
 REQUIRE(material_contract()==0);
 std::cout<<"PASS native ABI/state/thread/lifecycle contracts (100 recreate cycles)\n";
}
