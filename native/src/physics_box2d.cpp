#include "physics_backend.h"
#include <box2d/box2d.h>
#include <array>
#include <algorithm>
#include <cmath>
#include <memory>
namespace {
constexpr size_t max_bodies=256,max_shapes=512,max_events=1024;
uint64_t next_physics_id=1;
struct Body {uint64_t id=0;b2BodyId native{};};
struct Shape {uint64_t id=0,body=0;b2ShapeId native{};bool alive=false;};
bool error(std::string&e,const char*why){e=why;return false;}
bool bounded(float x,float limit){return std::isfinite(x)&&std::abs(x)<=limit;}
bool range(float x,float lower,float upper){return std::isfinite(x)&&x>=lower&&x<=upper;}
}
struct PhysicsBackend {
 b2WorldId world{};float dt=0;int substeps=0;uint32_t steps=0,event_count=0,dropped=0;
 std::array<Body,max_bodies>bodies{};std::array<Shape,max_shapes>shapes{};std::array<gal_physics_event,max_events>events{};
 Body*body(uint64_t id){for(auto&b:bodies)if(id&&b.id==id)return &b;return nullptr;}
 Shape*shape(uint64_t id){for(auto&s:shapes)if(id&&s.id==id&&s.alive)return &s;return nullptr;}
 Shape*lookup(b2ShapeId id){uint64_t raw=b2StoreShapeId(id);for(auto&s:shapes)if(s.id&&b2StoreShapeId(s.native)==raw)return &s;return nullptr;}
 void event(uint32_t type,b2ShapeId first,b2ShapeId second){
  auto*a=lookup(first);auto*b=lookup(second);
  if(!a||!b||event_count==max_events){dropped++;return;}
  events[event_count++]={type,(a->alive?0u:uint32_t(GAL_EVENT_REMOVED_A))|(b->alive?0u:uint32_t(GAL_EVENT_REMOVED_B)),a->id,b->id,a->body,b->body};
 }
};
void physics_destroy(PhysicsBackend*p){if(!p)return;if(b2World_IsValid(p->world))b2DestroyWorld(p->world);delete p;}
static bool create_body(PhysicsBackend&p,const gal_body_def*d,uint64_t*out,std::string&e){
 if(!out)return error(e,"null physics body output");
 *out=0;
 if(!d||d->size!=sizeof(*d)||d->version!=1||d->type>2||(d->flags&~3u)||d->reserved
 ||!bounded(d->x,10000)||!bounded(d->y,10000)||!bounded(d->angle,10000)
 ||!bounded(d->vx,1000)||!bounded(d->vy,1000)||!bounded(d->angular_velocity,100)
 ||!bounded(d->gravity_scale,100)||!range(d->linear_damping,0,100)||!range(d->angular_damping,0,100)
 ||(d->type==GAL_BODY_STATIC&&(d->vx!=0||d->vy!=0||d->angular_velocity!=0))
 ||((d->flags&GAL_BODY_BULLET)&&d->type!=GAL_BODY_DYNAMIC))return error(e,"invalid physics body definition/limits");
 auto slot=std::find_if(p.bodies.begin(),p.bodies.end(),[](const Body&b){return !b.id;});
 if(slot==p.bodies.end()||!next_physics_id)return error(e,"physics body capacity exhausted (256)");
 auto def=b2DefaultBodyDef();def.type=b2BodyType(d->type);def.position={d->x,d->y};def.rotation=b2MakeRot(d->angle);
 def.linearVelocity={d->vx,d->vy};def.angularVelocity=d->angular_velocity;def.gravityScale=d->gravity_scale;
 def.linearDamping=d->linear_damping;def.angularDamping=d->angular_damping;def.fixedRotation=(d->flags&GAL_BODY_FIXED_ROTATION)!=0;def.isBullet=(d->flags&GAL_BODY_BULLET)!=0;
 auto id=b2CreateBody(p.world,&def);if(!b2Body_IsValid(id))return error(e,"Box2D body creation failed");
 *slot={next_physics_id++,id};*out=slot->id;return true;
}
static bool create_shape(PhysicsBackend&p,const PhysicsShapeRequest&r,uint64_t*out,std::string&e){
 if(!out)return error(e,"null physics shape output");
 *out=0;auto*body=p.body(r.body);if(!body)return error(e,"stale or foreign physics body");const auto*d=r.def;
 if(!d||d->size!=sizeof(*d)||d->version!=1||d->type>1||(d->flags&~1u)||d->reserved
 ||!bounded(d->offset_x,100)||!bounded(d->offset_y,100)||!bounded(d->angle,10000)||!range(d->a,.001f,100)
 ||(d->type==GAL_SHAPE_CIRCLE?(d->b!=0||d->angle!=0):!range(d->b,.001f,100))
 ||!range(d->density,0,10000)||!range(d->friction,0,10)||!range(d->restitution,0,1))return error(e,"invalid physics shape definition/limits");
 auto slot=std::find_if(p.shapes.begin(),p.shapes.end(),[](const Shape&s){return !s.id;});
 if(slot==p.shapes.end()||!next_physics_id)return error(e,"physics shape capacity exhausted (512 including retired identities until next step)");
 auto def=b2DefaultShapeDef();def.density=d->density;def.material.friction=d->friction;def.material.restitution=d->restitution;
 def.filter={d->category,d->mask,d->group};def.isSensor=(d->flags&GAL_SHAPE_SENSOR)!=0;def.enableSensorEvents=true;def.enableContactEvents=true;
 b2ShapeId id{};
 if(d->type==GAL_SHAPE_CIRCLE){b2Circle circle{{d->offset_x,d->offset_y},d->a};id=b2CreateCircleShape(body->native,&def,&circle);}
 else{auto box=b2MakeOffsetBox(d->a,d->b,{d->offset_x,d->offset_y},b2MakeRot(d->angle));id=b2CreatePolygonShape(body->native,&def,&box);}
 if(!b2Shape_IsValid(id))return error(e,"Box2D shape creation failed");
 *slot={next_physics_id++,body->id,id,true};*out=slot->id;return true;
}
static bool create_capsule(PhysicsBackend&p,const PhysicsCapsuleRequest&r,uint64_t*out,std::string&e){
 if(!out)return error(e,"null physics shape output");
 *out=0;auto*body=p.body(r.body);if(!body)return error(e,"stale or foreign physics body");const auto*d=r.def;
 if(!d||d->size!=sizeof(*d)||d->version!=GAL_CAPSULE_VERSION||(d->flags&~uint32_t(GAL_SHAPE_SENSOR))||d->reserved||d->reserved2
 ||!bounded(d->x1,100)||!bounded(d->y1,100)||!bounded(d->x2,100)||!bounded(d->y2,100)||!range(d->radius,.001f,100)
 ||!range(d->density,0,10000)||!range(d->friction,0,10)||!range(d->restitution,0,1))return error(e,"invalid physics capsule definition/limits");
 const double dx=double(d->x2)-double(d->x1),dy=double(d->y2)-double(d->y1);
 constexpr double minimum=double(.01f);
 if(dx*dx+dy*dy<minimum*minimum)return error(e,"physics capsule endpoint distance must be at least .01 meters");
 auto slot=std::find_if(p.shapes.begin(),p.shapes.end(),[](const Shape&s){return !s.id;});
 if(slot==p.shapes.end()||!next_physics_id)return error(e,"physics shape capacity exhausted (512 including retired identities until next step)");
 auto def=b2DefaultShapeDef();def.density=d->density;def.material.friction=d->friction;def.material.restitution=d->restitution;
 def.filter={d->category,d->mask,d->group};def.isSensor=(d->flags&GAL_SHAPE_SENSOR)!=0;def.enableSensorEvents=true;def.enableContactEvents=true;
 const b2Capsule capsule{{d->x1,d->y1},{d->x2,d->y2},d->radius};
 const auto id=b2CreateCapsuleShape(body->native,&def,&capsule);if(!b2Shape_IsValid(id))return error(e,"Box2D capsule creation failed");
 *slot={next_physics_id++,body->id,id,true};*out=slot->id;return true;
}
struct Overlap {PhysicsBackend*p;std::array<uint64_t,max_shapes>ids{};uint32_t count=0;uint32_t flags=0;};
static bool overlap_callback(b2ShapeId id,void*context){
 auto&result=*static_cast<Overlap*>(context);auto*s=result.p->lookup(id);
 if(s&&s->alive&&result.count<max_shapes){
  if(result.flags){const bool sensor=b2Shape_IsSensor(id);if((result.flags==GAL_QUERY_EXCLUDE_SENSORS&&sensor)||(result.flags==GAL_QUERY_ONLY_SENSORS&&!sensor))return true;}
  result.ids[result.count++]=s->id;
 }
 return true;
}
bool physics_dispatch(PhysicsBackend*&p,PhysicsOp op,const void*in,void*out,std::string&e){
 if(op==PhysicsOp::Open){
  const auto*c=static_cast<const gal_physics_config*>(in);
  if(!c||c->size!=sizeof(*c)||c->version!=1||c->flags||c->reserved||!bounded(c->gravity_x,1000)||!bounded(c->gravity_y,1000)||!range(c->step_seconds,1.f/240,1.f/15)||c->substeps<1||c->substeps>8)return error(e,"invalid physics config/fixed-step limits");
  if(p)return error(e,"physics world already open");
  std::unique_ptr<PhysicsBackend,decltype(&physics_destroy)>candidate(new PhysicsBackend,physics_destroy);
  auto def=b2DefaultWorldDef();def.gravity={c->gravity_x,c->gravity_y};def.workerCount=0;def.maximumLinearSpeed=1000;
  candidate->world=b2CreateWorld(&def);if(!b2World_IsValid(candidate->world))return error(e,"Box2D world creation failed");
  candidate->dt=c->step_seconds;candidate->substeps=int(c->substeps);p=candidate.release();return true;
 }
 if(!p)return error(e,"physics world is not open");
 if(op==PhysicsOp::Close){physics_destroy(p);p=nullptr;return true;}
 if(op==PhysicsOp::CreateBody)return create_body(*p,static_cast<const gal_body_def*>(in),static_cast<uint64_t*>(out),e);
 if(op==PhysicsOp::CreateShape)return create_shape(*p,*static_cast<const PhysicsShapeRequest*>(in),static_cast<uint64_t*>(out),e);
 if(op==PhysicsOp::CreateCapsule)return create_capsule(*p,*static_cast<const PhysicsCapsuleRequest*>(in),static_cast<uint64_t*>(out),e);
 if(op==PhysicsOp::ReleaseShape){auto*s=p->shape(*static_cast<const uint64_t*>(in));if(!s)return error(e,"stale or foreign physics shape");b2DestroyShape(s->native,true);s->alive=false;return true;}
 if(op==PhysicsOp::ReleaseBody){auto*b=p->body(*static_cast<const uint64_t*>(in));if(!b)return error(e,"stale or foreign physics body");for(auto&s:p->shapes)if(s.alive&&s.body==b->id)s.alive=false;b2DestroyBody(b->native);*b={};return true;}
 if(op==PhysicsOp::BodyState){auto*b=p->body(*static_cast<const uint64_t*>(in));auto*s=static_cast<gal_body_state*>(out);if(!b)return error(e,"stale or foreign physics body");if(!s||s->size!=sizeof(*s)||s->reserved)return error(e,"invalid physics body state size/reserved");
  auto at=b2Body_GetPosition(b->native);auto v=b2Body_GetLinearVelocity(b->native);auto rotation=b2Body_GetRotation(b->native);*s={sizeof(*s),b2Body_IsAwake(b->native)?1u:0u,at.x,at.y,std::atan2(rotation.s,rotation.c),v.x,v.y,b2Body_GetAngularVelocity(b->native),uint32_t(b2Body_GetType(b->native)),0};return true;}
 if(op==PhysicsOp::BodyCommand){const auto&r=*static_cast<const PhysicsCommand*>(in);auto*b=p->body(r.body);if(!b)return error(e,"stale or foreign physics body");
  if(r.command<1||r.command>4||!bounded(r.x,r.command==1?10000:1000)||!bounded(r.y,r.command==1?10000:1000)||!bounded(r.z,r.command==1?10000:100)||(r.command>2&&r.z!=0))return error(e,"invalid physics body command/limits");
  if(r.command==GAL_BODY_POSE){b2Body_SetTransform(b->native,{r.x,r.y},b2MakeRot(r.z));b2Body_SetAwake(b->native,true); }
  else if(r.command==GAL_BODY_VELOCITY){if(b2Body_GetType(b->native)==b2_staticBody)return error(e,"velocity requires a dynamic or kinematic body");b2Body_SetLinearVelocity(b->native,{r.x,r.y});b2Body_SetAngularVelocity(b->native,r.z);}
  else{if(b2Body_GetType(b->native)!=b2_dynamicBody)return error(e,"force/impulse requires a dynamic body");if(r.command==GAL_BODY_IMPULSE)b2Body_ApplyLinearImpulseToCenter(b->native,{r.x,r.y},true);else b2Body_ApplyForceToCenter(b->native,{r.x,r.y},true);}
  return true;
 }
 if(op==PhysicsOp::Step){auto*r=static_cast<gal_physics_step_result*>(out);if(!r||r->size!=sizeof(*r)||p->steps==UINT32_MAX)return error(e,"invalid step result or step counter exhausted");
  b2World_Step(p->world,p->dt,p->substeps);p->steps++;p->event_count=p->dropped=0;
  const auto contacts=b2World_GetContactEvents(p->world);const auto sensors=b2World_GetSensorEvents(p->world);
  for(int i=0;i<contacts.beginCount;i++)p->event(GAL_CONTACT_BEGIN,contacts.beginEvents[i].shapeIdA,contacts.beginEvents[i].shapeIdB);
  for(int i=0;i<contacts.endCount;i++)p->event(GAL_CONTACT_END,contacts.endEvents[i].shapeIdA,contacts.endEvents[i].shapeIdB);
  for(int i=0;i<sensors.beginCount;i++)p->event(GAL_SENSOR_BEGIN,sensors.beginEvents[i].sensorShapeId,sensors.beginEvents[i].visitorShapeId);
  for(int i=0;i<sensors.endCount;i++)p->event(GAL_SENSOR_END,sensors.endEvents[i].sensorShapeId,sensors.endEvents[i].visitorShapeId);
  for(auto&s:p->shapes)if(s.id&&!s.alive)s={};
  *r={sizeof(*r),p->event_count,p->dropped,p->steps};return true;
 }
 if(op==PhysicsOp::Events){const auto&r=*static_cast<const PhysicsEventsRequest*>(in);auto*count=static_cast<uint32_t*>(out);if(!count)return error(e,"null physics event count");*count=p->event_count;if(r.capacity<p->event_count||(p->event_count&&!r.output))return error(e,"physics event output capacity insufficient");std::copy_n(p->events.data(),p->event_count,r.output);return true;}
 if(op==PhysicsOp::Ray){const auto*q=static_cast<const gal_physics_ray*>(in);auto*r=static_cast<gal_physics_ray_hit*>(out);
  if(!q||q->size!=sizeof(*q)||q->version!=1||!bounded(q->x,10000)||!bounded(q->y,10000)||!bounded(q->dx,20000)||!bounded(q->dy,20000)||(q->dx==0&&q->dy==0)||!r||r->size!=sizeof(*r)||r->reserved)return error(e,"invalid ray query/output");
  const auto hit=b2World_CastRayClosest(p->world,{q->x,q->y},{q->dx,q->dy},{q->category,q->mask});
  gal_physics_ray_hit candidate{};candidate.size=sizeof(candidate);if(hit.hit){auto*s=p->lookup(hit.shapeId);if(!s||!s->alive)return error(e,"ray returned unmapped shape");candidate.hit=1;candidate.shape=s->id;candidate.body=s->body;candidate.x=hit.point.x;candidate.y=hit.point.y;candidate.normal_x=hit.normal.x;candidate.normal_y=hit.normal.y;candidate.fraction=hit.fraction;}*r=candidate;return true;
 }
 if(op==PhysicsOp::Aabb){const auto&r=*static_cast<const PhysicsAabbRequest*>(in);const auto*q=r.query;auto*count=static_cast<uint32_t*>(out);
  if(!q||q->size!=sizeof(*q)||q->version!=1||!bounded(q->lower_x,10000)||!bounded(q->lower_y,10000)||!bounded(q->upper_x,10000)||!bounded(q->upper_y,10000)||q->lower_x>q->upper_x||q->lower_y>q->upper_y||!count||r.capacity>max_shapes)return error(e,"invalid broad-phase AABB query");
  Overlap result{p,{},0};b2World_OverlapAABB(p->world,{{q->lower_x,q->lower_y},{q->upper_x,q->upper_y}},{q->category,q->mask},overlap_callback,&result);*count=result.count;
  if(r.capacity<result.count||(result.count&&!r.output))return error(e,"AABB output capacity insufficient");
  std::sort(result.ids.begin(),result.ids.begin()+result.count);std::copy_n(result.ids.data(),result.count,r.output);return true;
 }
 if(op==PhysicsOp::Overlap){const auto&r=*static_cast<const PhysicsOverlapRequest*>(in);const auto*q=r.query;auto*count=static_cast<uint32_t*>(out);
  if(!q||q->size!=sizeof(*q)||q->version!=GAL_OVERLAP_QUERY_VERSION||q->type>GAL_SHAPE_BOX||q->flags>GAL_QUERY_ONLY_SENSORS||q->reserved
  ||!bounded(q->x,10000)||!bounded(q->y,10000)||!bounded(q->angle,10000)||!range(q->a,.001f,100)
  ||(q->type==GAL_SHAPE_CIRCLE?(q->b!=0||q->angle!=0):!range(q->b,.001f,100))||!count||r.capacity>max_shapes)return error(e,"invalid physics overlap query/limits");
  b2ShapeProxy proxy{};
  if(q->type==GAL_SHAPE_CIRCLE){const b2Vec2 center{q->x,q->y};proxy=b2MakeProxy(&center,1,q->a);}
  else{const auto box=b2MakeOffsetBox(q->a,q->b,{q->x,q->y},b2MakeRot(q->angle));proxy=b2MakeProxy(box.vertices,box.count,box.radius);}
  Overlap result{p,{},0,q->flags};b2World_OverlapShape(p->world,&proxy,{q->category,q->mask},overlap_callback,&result);*count=result.count;
  if(r.capacity<result.count||(result.count&&!r.output))return error(e,"overlap output capacity insufficient");
  std::sort(result.ids.begin(),result.ids.begin()+result.count);if(result.count)std::copy_n(result.ids.data(),result.count,r.output);return true;
 }
 if(op==PhysicsOp::State){auto*s=static_cast<gal_physics_state*>(out);if(!s||s->size!=sizeof(*s)||s->reserved)return error(e,"invalid physics state size/reserved");*s={sizeof(*s),0,0,0,p->steps,p->event_count,p->dropped,0};for(const auto&b:p->bodies)if(b.id)s->bodies++;for(const auto&shape:p->shapes)if(shape.id){if(shape.alive)s->shapes++;else s->retired_shapes++;}return true;}
 return error(e,"unsupported physics operation");
}
