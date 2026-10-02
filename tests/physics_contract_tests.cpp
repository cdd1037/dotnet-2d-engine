#include "gal_physics.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <thread>
#define REQUIRE(x) do { if(!(x)) { std::cerr<<"FAIL line "<<__LINE__<<": "<<#x<<" error="<<gal_last_error()<<'\n'; return 1; } } while(0)
static_assert(sizeof(gal_shape_def)==72&&offsetof(gal_shape_def,category)==48,"legacy shape ABI");
static_assert(sizeof(gal_physics_config)==32&&sizeof(gal_body_def)==56&&sizeof(gal_body_state)==40,"legacy body ABI");
static_assert(sizeof(gal_physics_ray)==40&&sizeof(gal_physics_ray_hit)==48&&sizeof(gal_physics_aabb)==40,"legacy query ABI");
static_assert(sizeof(gal_physics_event)==40&&sizeof(gal_physics_step_result)==16&&sizeof(gal_physics_state)==32,"legacy result ABI");
static_assert(sizeof(gal_capsule_def_v1)==72&&offsetof(gal_capsule_def_v1,x1)==16&&offsetof(gal_capsule_def_v1,radius)==32&&offsetof(gal_capsule_def_v1,category)==48&&offsetof(gal_capsule_def_v1,group)==64&&offsetof(gal_capsule_def_v1,reserved2)==68,"capsule ABI");
static_assert(sizeof(gal_physics_overlap_query_v1)==56&&offsetof(gal_physics_overlap_query_v1,x)==16&&offsetof(gal_physics_overlap_query_v1,angle)==32&&offsetof(gal_physics_overlap_query_v1,reserved)==36&&offsetof(gal_physics_overlap_query_v1,category)==40,"overlap ABI");
constexpr uint64_t sentinel=UINT64_MAX-123;
static gal_capsule_def_v1 capsule_def(){return {sizeof(gal_capsule_def_v1),GAL_CAPSULE_VERSION,0,0,-1,0,1,0,.5f,1,.3f,0,1,UINT64_MAX,0,0};}
static gal_physics_overlap_query_v1 circle_query(float x=0,float y=0,float radius=1){return {sizeof(gal_physics_overlap_query_v1),GAL_OVERLAP_QUERY_VERSION,GAL_SHAPE_CIRCLE,0,x,y,radius,0,0,0,1,UINT64_MAX};}
#if GAL_TEST_PHYSICS_ENABLED
static gal_body_def body_def(float x=0,float y=0,uint32_t type=GAL_BODY_STATIC){return {sizeof(gal_body_def),GAL_PHYSICS_VERSION,type,0,x,y,0,0,0,0,1,0,0,0};}
static gal_shape_def circle_def(float x=0,float y=0,float radius=.1f){return {sizeof(gal_shape_def),GAL_PHYSICS_VERSION,GAL_SHAPE_CIRCLE,0,x,y,0,radius,0,1,.3f,0,1,UINT64_MAX,0,0};}
static int validation(gal_context*c,uint64_t body){
 auto capsule=capsule_def();auto query=circle_query();uint64_t id=sentinel;uint32_t count=77;std::array<uint64_t,512> ids;ids.fill(sentinel);
 REQUIRE(gal_physics_create_capsule_v1(c,body,nullptr,&id)==-1&&id==0);
 REQUIRE(gal_physics_create_capsule_v1(c,body,&capsule,nullptr)==-1);
 for(int test=0;test<25;++test){auto bad=capsule;switch(test){
  case 0:--bad.size;break;case 1:++bad.size;break;case 2:bad.version=0;break;case 3:++bad.version;break;
  case 4:bad.flags=2;break;case 5:bad.flags=UINT32_MAX;break;case 6:bad.reserved=1;break;case 7:bad.reserved2=1;break;
  case 8:bad.x1=-100.1f;break;case 9:bad.y1=100.1f;break;case 10:bad.x2=100.1f;break;case 11:bad.y2=-100.1f;break;
  case 12:bad.radius=.00099f;break;case 13:bad.radius=100.1f;break;case 14:bad.density=-.01f;break;case 15:bad.density=10001;break;
  case 16:bad.friction=-.01f;break;case 17:bad.friction=10.1f;break;case 18:bad.restitution=-.01f;break;case 19:bad.restitution=1.1f;break;
  case 20:bad.x2=bad.x1;bad.y2=bad.y1;break;case 21:bad.x1=bad.y1=bad.y2=0;bad.x2=.00999f;break;
  case 22:bad.x1=bad.y1=0;bad.x2=.006f;bad.y2=.00799f;break;case 23:bad.radius=0;break;case 24:bad.radius=-1;break;
 }id=sentinel;REQUIRE(gal_physics_create_capsule_v1(c,body,&bad,&id)==-1&&id==0);}
 float gal_capsule_def_v1::*capsule_numbers[]={&gal_capsule_def_v1::x1,&gal_capsule_def_v1::y1,&gal_capsule_def_v1::x2,&gal_capsule_def_v1::y2,&gal_capsule_def_v1::radius,&gal_capsule_def_v1::density,&gal_capsule_def_v1::friction,&gal_capsule_def_v1::restitution};
 for(auto field:capsule_numbers)for(float invalid:{NAN,INFINITY,-INFINITY}){auto bad=capsule;bad.*field=invalid;REQUIRE(gal_physics_create_capsule_v1(c,body,&bad,&id)==-1&&id==0);}
 REQUIRE(gal_physics_query_overlap_v1(c,nullptr,ids.data(),512,&count)==-1&&count==77);
 REQUIRE(gal_physics_query_overlap_v1(c,&query,ids.data(),512,nullptr)==-1);
 REQUIRE(gal_physics_query_overlap_v1(c,&query,ids.data(),513,&count)==-1&&count==77);
 REQUIRE(gal_physics_query_overlap_v1(c,&query,ids.data(),UINT32_MAX,&count)==-1&&count==77);
 for(int test=0;test<26;++test){auto bad=query;switch(test){
  case 0:--bad.size;break;case 1:++bad.size;break;case 2:bad.version=0;break;case 3:++bad.version;break;case 4:bad.type=2;break;
  case 5:bad.flags=3;break;case 6:bad.flags=4;break;case 7:bad.flags=UINT32_MAX;break;case 8:bad.reserved=1;break;
  case 9:bad.x=10000.1f;break;case 10:bad.y=-10000.1f;break;case 11:bad.a=.00099f;break;case 12:bad.a=100.1f;break;
  case 13:bad.b=.1f;break;case 14:bad.angle=.1f;break;case 15:bad.a=0;break;case 16:bad.a=-1;break;
  case 17:bad.type=GAL_SHAPE_BOX;bad.b=.00099f;break;case 18:bad.type=GAL_SHAPE_BOX;bad.b=100.1f;break;
  case 19:bad.type=GAL_SHAPE_BOX;bad.b=1;bad.angle=10000.1f;break;case 20:bad.type=GAL_SHAPE_BOX;bad.b=1;bad.angle=-10000.1f;break;
  case 21:bad.type=GAL_SHAPE_BOX;bad.b=0;break;case 22:bad.type=GAL_SHAPE_BOX;bad.b=-1;break;
  case 23:bad.x=-10000.1f;break;case 24:bad.y=10000.1f;break;case 25:bad.type=UINT32_MAX;break;
 }REQUIRE(gal_physics_query_overlap_v1(c,&bad,ids.data(),512,&count)==-1&&count==77);}
 float gal_physics_overlap_query_v1::*query_numbers[]={&gal_physics_overlap_query_v1::x,&gal_physics_overlap_query_v1::y,&gal_physics_overlap_query_v1::a,&gal_physics_overlap_query_v1::b,&gal_physics_overlap_query_v1::angle};
 for(auto field:query_numbers)for(float invalid:{NAN,INFINITY,-INFINITY}){auto bad=query;bad.*field=invalid;REQUIRE(gal_physics_query_overlap_v1(c,&bad,ids.data(),512,&count)==-1&&count==77);bad.type=GAL_SHAPE_BOX;REQUIRE(gal_physics_query_overlap_v1(c,&bad,ids.data(),512,&count)==-1&&count==77);}
 REQUIRE(std::all_of(ids.begin(),ids.end(),[](uint64_t value){return value==sentinel;}));
 gal_physics_state state{sizeof(state),0,0,0,0,0,0,0};REQUIRE(gal_physics_get_state(c,&state)==0&&state.shapes==0&&state.retired_shapes==0);
 // Inclusive geometry/material limits, including the authored float minimum.
 for(int test=0;test<3;++test){auto edge=capsule;if(test==0){edge.x1=edge.y1=edge.y2=0;edge.x2=.01f;edge.radius=.001f;edge.density=edge.friction=edge.restitution=0;}
  if(test==1){edge.x1=edge.y1=-100;edge.x2=edge.y2=100;edge.radius=100;edge.density=10000;edge.friction=10;edge.restitution=1;}
  if(test==2){edge.x1=edge.y1=100;edge.x2=edge.y2=-100;}
  REQUIRE(gal_physics_create_capsule_v1(c,body,&edge,&id)==0&&id);REQUIRE(gal_physics_release_shape(c,id)==0);
 }
 for(int test=0;test<3;++test){auto edge=query;if(test==0){edge.x=10000;edge.y=-10000;edge.a=.001f;}if(test==1){edge.type=GAL_SHAPE_BOX;edge.x=-10000;edge.y=10000;edge.a=edge.b=100;edge.angle=10000;}if(test==2){edge.type=GAL_SHAPE_BOX;edge.a=edge.b=.001f;edge.angle=-10000;}
  REQUIRE(gal_physics_query_overlap_v1(c,&edge,nullptr,0,&count)==0&&count==0);
 }
 return 0;
}
static int geometry_and_filters(gal_context*c,uint64_t body){
 uint64_t corner=0,actual=0,box_corner=0,box_actual=0;auto shape=circle_def(.9f,.9f,.05f);REQUIRE(gal_physics_create_shape(c,body,&shape,&corner)==0);
 shape=circle_def(.4f,0,.05f);REQUIRE(gal_physics_create_shape(c,body,&shape,&actual)==0);
 auto q=circle_query();std::array<uint64_t,512>ids{};uint32_t count=0;
 gal_physics_aabb broad{sizeof(broad),GAL_PHYSICS_VERSION,-1,-1,1,1,1,UINT64_MAX};
 REQUIRE(gal_physics_query_aabb(c,&broad,ids.data(),512,&count)==0&&count==2);
 REQUIRE(gal_physics_query_overlap_v1(c,&q,ids.data(),512,&count)==0&&count==1&&ids[0]==actual);
 shape=circle_def(11.3f,-1.3f,.05f);REQUIRE(gal_physics_create_shape(c,body,&shape,&box_corner)==0);
 shape=circle_def(11,1,.05f);REQUIRE(gal_physics_create_shape(c,body,&shape,&box_actual)==0);
 q=circle_query(10);q.type=GAL_SHAPE_BOX;q.a=2;q.b=.1f;q.angle=.78539816339f;
 broad.lower_x=8.5f;broad.upper_x=11.5f;broad.lower_y=-1.5f;broad.upper_y=1.5f;
 REQUIRE(gal_physics_query_aabb(c,&broad,ids.data(),512,&count)==0&&count==2);
 REQUIRE(gal_physics_query_overlap_v1(c,&q,ids.data(),512,&count)==0&&count==1&&ids[0]==box_actual);
 // Reciprocal 64-bit filters ignore collision groups, while sensor selection is explicit.
 constexpr uint64_t high=uint64_t(1)<<63;auto capsule=capsule_def();capsule.category=high;capsule.mask=4;capsule.group=-12;
 uint64_t solid=0,sensor=0,masked=0;auto far_body=body_def(20);uint64_t filtered_body=0;REQUIRE(gal_physics_create_body(c,&far_body,&filtered_body)==0);
 REQUIRE(gal_physics_create_capsule_v1(c,filtered_body,&capsule,&solid)==0);capsule.flags=GAL_SHAPE_SENSOR;
 REQUIRE(gal_physics_create_capsule_v1(c,filtered_body,&capsule,&sensor)==0);capsule.mask=0;capsule.group=12;
 REQUIRE(gal_physics_create_capsule_v1(c,filtered_body,&capsule,&masked)==0);
 q=circle_query(20);q.category=4;q.mask=high;
 REQUIRE(gal_physics_query_overlap_v1(c,&q,ids.data(),512,&count)==0&&count==2&&ids[0]==solid&&ids[1]==sensor);
 q.flags=GAL_QUERY_EXCLUDE_SENSORS;REQUIRE(gal_physics_query_overlap_v1(c,&q,ids.data(),512,&count)==0&&count==1&&ids[0]==solid);
 q.flags=GAL_QUERY_ONLY_SENSORS;REQUIRE(gal_physics_query_overlap_v1(c,&q,ids.data(),512,&count)==0&&count==1&&ids[0]==sensor);
 q.flags=0;q.category=2;REQUIRE(gal_physics_query_overlap_v1(c,&q,ids.data(),512,&count)==0&&count==0);
 q.category=4;q.mask=1;REQUIRE(gal_physics_query_overlap_v1(c,&q,ids.data(),512,&count)==0&&count==0);
 q.mask=high;ids.fill(sentinel);
 REQUIRE(gal_physics_query_overlap_v1(c,&q,ids.data(),1,&count)==-1&&count==2);
 REQUIRE(std::all_of(ids.begin(),ids.end(),[](uint64_t value){return value==sentinel;}));
 REQUIRE(gal_physics_query_overlap_v1(c,&q,nullptr,512,&count)==-1&&count==2);
 REQUIRE(gal_physics_query_overlap_v1(c,&q,nullptr,0,&count)==-1&&count==2);
 REQUIRE(gal_physics_release_shape(c,solid)==0);REQUIRE(gal_physics_release_shape(c,solid)==-1);
 REQUIRE(gal_physics_query_overlap_v1(c,&q,ids.data(),512,&count)==0&&count==1&&ids[0]==sensor);
 REQUIRE(gal_physics_release_body(c,filtered_body)==0);REQUIRE(gal_physics_release_shape(c,sensor)==-1);
 REQUIRE(gal_physics_query_overlap_v1(c,&q,nullptr,0,&count)==0&&count==0);
 uint64_t rejected=sentinel;REQUIRE(gal_physics_create_capsule_v1(c,filtered_body,&capsule,&rejected)==-1&&rejected==0);
 // Local capsule endpoints follow both translation and rotation of their body.
 far_body=body_def(30,10,GAL_BODY_DYNAMIC);far_body.angle=1.57079632679f;uint64_t rotated_body=0,rotated_shape=0;
 REQUIRE(gal_physics_create_body(c,&far_body,&rotated_body)==0);capsule=capsule_def();capsule.x1=-2;capsule.x2=2;capsule.radius=.1f;
 REQUIRE(gal_physics_create_capsule_v1(c,rotated_body,&capsule,&rotated_shape)==0);
 q=circle_query(30,11.8f,.05f);REQUIRE(gal_physics_query_overlap_v1(c,&q,ids.data(),512,&count)==0&&count==1&&ids[0]==rotated_shape);
 q=circle_query(31.8f,10,.05f);REQUIRE(gal_physics_query_overlap_v1(c,&q,ids.data(),512,&count)==0&&count==0);
 REQUIRE(gal_physics_release_body(c,rotated_body)==0);
 return 0;
}
static int simulation_and_capacity(gal_context*c){
 auto def=body_def(50,0,GAL_BODY_DYNAMIC);uint64_t body=0,id=0;REQUIRE(gal_physics_create_body(c,&def,&body)==0);
 auto capsule=capsule_def();capsule.density=2;REQUIRE(gal_physics_create_capsule_v1(c,body,&capsule,&id)==0);
 REQUIRE(gal_physics_body_command(c,body,GAL_BODY_IMPULSE,10,0,0)==0);gal_body_state state{sizeof(state),0,0,0,0,0,0,0,0,0};
 REQUIRE(gal_physics_get_body(c,body,&state)==0);REQUIRE(std::abs(state.vx-10.f/(2.f*(2.f+3.14159265359f*.25f)))<.001f);
 REQUIRE(gal_physics_release_body(c,body)==0);
 def=body_def(50);uint64_t sensor_body=0,sensor_shape=0;REQUIRE(gal_physics_create_body(c,&def,&sensor_body)==0);capsule.flags=GAL_SHAPE_SENSOR;capsule.density=0;
 REQUIRE(gal_physics_create_capsule_v1(c,sensor_body,&capsule,&sensor_shape)==0);
 def=body_def(50,0,GAL_BODY_DYNAMIC);REQUIRE(gal_physics_create_body(c,&def,&body)==0);auto circle=circle_def();REQUIRE(gal_physics_create_shape(c,body,&circle,&id)==0);
 gal_physics_step_result step{sizeof(step),0,0,0};REQUIRE(gal_physics_step(c,&step)==0&&step.dropped==0);
 std::array<gal_physics_event,1024>events{};uint32_t count=0;REQUIRE(gal_physics_events(c,events.data(),1024,&count)==0);
 REQUIRE(std::any_of(events.begin(),events.begin()+count,[&](const gal_physics_event&e){return e.type==GAL_SENSOR_BEGIN&&e.shape_a==sensor_shape&&e.shape_b==id;}));
 REQUIRE(gal_physics_release_shape(c,sensor_shape)==0);REQUIRE(gal_physics_step(c,&step)==0&&step.dropped==0);REQUIRE(gal_physics_events(c,events.data(),1024,&count)==0);
 REQUIRE(std::any_of(events.begin(),events.begin()+count,[&](const gal_physics_event&e){return e.type==GAL_SENSOR_END&&e.shape_a==sensor_shape&&e.shape_b==id&&(e.flags&GAL_EVENT_REMOVED_A);}));
 REQUIRE(gal_physics_close(c)==0);gal_physics_config config{sizeof(config),GAL_PHYSICS_VERSION,0,0,1.f/60,4,0,0};REQUIRE(gal_physics_open(c,&config)==0);
 def=body_def();REQUIRE(gal_physics_create_body(c,&def,&body)==0);capsule=capsule_def();std::array<uint64_t,512>all{},output{};
 for(auto&value:all)REQUIRE(gal_physics_create_capsule_v1(c,body,&capsule,&value)==0&&value);
 uint64_t rejected=sentinel;REQUIRE(gal_physics_create_capsule_v1(c,body,&capsule,&rejected)==-1&&rejected==0);
 auto q=circle_query();output.fill(sentinel);REQUIRE(gal_physics_query_overlap_v1(c,&q,output.data(),511,&count)==-1&&count==512);
 REQUIRE(std::all_of(output.begin(),output.end(),[](uint64_t value){return value==sentinel;}));
 REQUIRE(gal_physics_query_overlap_v1(c,&q,output.data(),512,&count)==0&&count==512&&output==all);
 REQUIRE(gal_physics_release_shape(c,all[173])==0);REQUIRE(gal_physics_create_capsule_v1(c,body,&capsule,&rejected)==-1&&rejected==0);
 REQUIRE(gal_physics_query_overlap_v1(c,&q,output.data(),512,&count)==0&&count==511&&std::find(output.begin(),output.begin()+count,all[173])==output.begin()+count);
 REQUIRE(gal_physics_step(c,&step)==0);REQUIRE(gal_physics_create_capsule_v1(c,body,&capsule,&rejected)==0&&rejected>all.back());
 REQUIRE(gal_physics_query_overlap_v1(c,&q,output.data(),512,&count)==0&&count==512&&std::is_sorted(output.begin(),output.end())&&output.back()==rejected);
 REQUIRE(gal_physics_release_body(c,body)==0);REQUIRE(gal_physics_query_overlap_v1(c,&q,nullptr,0,&count)==0&&count==0);
 REQUIRE(gal_physics_release_shape(c,rejected)==-1);
 return 0;
}
#endif
int main(){
 gal_config config{sizeof(config),1,64,64,1,GAL_HEADLESS};gal_context*c=nullptr;
 auto capsule=capsule_def();auto query=circle_query();uint64_t id=sentinel;uint32_t count=77;std::array<uint64_t,512>ids;ids.fill(sentinel);
 REQUIRE(gal_physics_create_capsule_v1(nullptr,0,&capsule,&id)==-1&&id==sentinel);
 REQUIRE(gal_physics_query_overlap_v1(nullptr,&query,ids.data(),512,&count)==-1&&count==77);
 REQUIRE(gal_create(&config,&c)==0);
 REQUIRE(gal_physics_create_capsule_v1(c,0,&capsule,&id)==-1&&id==sentinel);
 REQUIRE(gal_physics_query_overlap_v1(c,&query,ids.data(),512,&count)==-1&&count==77);
 gal_physics_config physics{sizeof(physics),GAL_PHYSICS_VERSION,0,0,1.f/60,4,0,0};
#if GAL_TEST_PHYSICS_ENABLED
 REQUIRE(gal_physics_open(c,&physics)==0);uint64_t body=0;auto def=body_def();REQUIRE(gal_physics_create_body(c,&def,&body)==0);
 REQUIRE(validation(c,body)==0);REQUIRE(geometry_and_filters(c,body)==0);
#else
 REQUIRE(gal_physics_open(c,&physics)==-1&&std::strstr(gal_last_error(),"not enabled"));uint64_t body=1;
 REQUIRE(gal_physics_create_capsule_v1(c,body,&capsule,&id)==-1&&std::strstr(gal_last_error(),"not enabled"));
 REQUIRE(gal_physics_query_overlap_v1(c,&query,ids.data(),512,&count)==-1&&std::strstr(gal_last_error(),"not enabled"));
 REQUIRE(gal_physics_create_capsule_v1(c,0,nullptr,nullptr)==-1);REQUIRE(gal_physics_query_overlap_v1(c,nullptr,nullptr,UINT32_MAX,nullptr)==-1);
#endif
 id=sentinel;count=77;int create_status=0,query_status=0;
 std::thread wrong([&]{create_status=gal_physics_create_capsule_v1(c,body,&capsule,&id);query_status=gal_physics_query_overlap_v1(c,&query,ids.data(),512,&count);});wrong.join();
 REQUIRE(create_status==-1&&query_status==-1&&id==sentinel&&count==77);
 auto*foreign=reinterpret_cast<gal_context*>(uintptr_t(1));REQUIRE(gal_physics_create_capsule_v1(foreign,body,&capsule,&id)==-1&&id==sentinel);
 REQUIRE(gal_physics_query_overlap_v1(foreign,&query,ids.data(),512,&count)==-1&&count==77);
 gal_camera camera{0,0,1};REQUIRE(gal_begin(c,&camera)==0);
 REQUIRE(gal_physics_create_capsule_v1(c,body,&capsule,&id)==-1&&id==sentinel&&std::strstr(gal_last_error(),"frame"));
 REQUIRE(gal_physics_query_overlap_v1(c,&query,ids.data(),512,&count)==-1&&count==77&&std::strstr(gal_last_error(),"frame"));REQUIRE(gal_abort(c)==0);
#if GAL_TEST_PHYSICS_ENABLED
 REQUIRE(simulation_and_capacity(c)==0);REQUIRE(gal_physics_close(c)==0);id=sentinel;
 REQUIRE(gal_physics_create_capsule_v1(c,body,&capsule,&id)==-1&&id==sentinel);REQUIRE(gal_physics_query_overlap_v1(c,&query,ids.data(),512,&count)==-1&&count==77);
 REQUIRE(gal_physics_open(c,&physics)==0);REQUIRE(gal_physics_create_capsule_v1(c,body,&capsule,&id)==-1&&id==0);
 REQUIRE(gal_physics_query_overlap_v1(c,&query,nullptr,0,&count)==0&&count==0);
 uint64_t closing_body=0,closing_shape=0;REQUIRE(gal_physics_create_body(c,&def,&closing_body)==0);REQUIRE(gal_physics_create_capsule_v1(c,closing_body,&capsule,&closing_shape)==0);
 REQUIRE(gal_physics_close(c)==0);REQUIRE(gal_physics_release_shape(c,closing_shape)==-1);REQUIRE(gal_physics_open(c,&physics)==0);
 REQUIRE(gal_physics_create_capsule_v1(c,closing_body,&capsule,&id)==-1&&id==0);REQUIRE(gal_physics_release_shape(c,closing_shape)==-1);
 REQUIRE(gal_physics_query_overlap_v1(c,&query,nullptr,0,&count)==0&&count==0);REQUIRE(gal_physics_create_body(c,&def,&closing_body)==0);
 REQUIRE(gal_physics_create_capsule_v1(c,closing_body,&capsule,&id)==0&&id>closing_shape);closing_shape=id;REQUIRE(gal_destroy(c)==0);
 id=sentinel;count=77;REQUIRE(gal_physics_create_capsule_v1(c,body,&capsule,&id)==-1&&id==sentinel);REQUIRE(gal_physics_query_overlap_v1(c,&query,ids.data(),512,&count)==-1&&count==77);
 REQUIRE(gal_create(&config,&c)==0);REQUIRE(gal_physics_open(c,&physics)==0);REQUIRE(gal_physics_create_capsule_v1(c,body,&capsule,&id)==-1&&id==0);
 REQUIRE(gal_physics_release_shape(c,closing_shape)==-1);REQUIRE(gal_physics_create_capsule_v1(c,closing_body,&capsule,&id)==-1&&id==0);
 REQUIRE(gal_physics_query_overlap_v1(c,&query,nullptr,0,&count)==0&&count==0);
#endif
 REQUIRE(gal_destroy(c)==0);std::cout<<"PASS physics capsule/overlap ABI and "<<(GAL_TEST_PHYSICS_ENABLED?"enabled narrow-phase, limits, filters, sensors, capacity and lifetime":"disabled dispatch")<<" contracts, including frame/thread/context guards\n";
 return 0;
}
