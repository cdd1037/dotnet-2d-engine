#pragma once
#include "gal_physics.h"
#include <string>
struct PhysicsBackend;
enum class PhysicsOp {Open,Close,CreateBody,ReleaseBody,CreateShape,CreateCapsule,ReleaseShape,BodyCommand,BodyState,Step,Events,Ray,Aabb,Overlap,State};
struct PhysicsShapeRequest {uint64_t body;const gal_shape_def*def;};
struct PhysicsCapsuleRequest {uint64_t body;const gal_capsule_def_v1*def;};
struct PhysicsCommand {uint64_t body;uint32_t command;float x,y,z;};
struct PhysicsEventsRequest {gal_physics_event*output;uint32_t capacity;};
struct PhysicsAabbRequest {const gal_physics_aabb*query;uint64_t*output;uint32_t capacity;};
struct PhysicsOverlapRequest {const gal_physics_overlap_query_v1*query;uint64_t*output;uint32_t capacity;};
bool physics_dispatch(PhysicsBackend*&,PhysicsOp,const void*,void*,std::string&);
void physics_destroy(PhysicsBackend*);
