#pragma once
#include "gal_physics.h"
#include <string>
struct PhysicsBackend;
enum class PhysicsOp {Open,Close,CreateBody,ReleaseBody,CreateShape,ReleaseShape,BodyCommand,BodyState,Step,Events,Ray,Aabb,State};
struct PhysicsShapeRequest {uint64_t body;const gal_shape_def*def;};
struct PhysicsCommand {uint64_t body;uint32_t command;float x,y,z;};
struct PhysicsEventsRequest {gal_physics_event*output;uint32_t capacity;};
struct PhysicsAabbRequest {const gal_physics_aabb*query;uint64_t*output;uint32_t capacity;};
bool physics_dispatch(PhysicsBackend*&,PhysicsOp,const void*,void*,std::string&);
void physics_destroy(PhysicsBackend*);
