#ifndef GAL_PHYSICS_H
#define GAL_PHYSICS_H
#include "gal.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Optional, single-threaded Box2D world per context. Main-thread calls outside
   sprite frames. Units: meters, seconds, radians, kg. No render-transform binding.
   All records are size/version checked; no Box2D structs/pointers cross the ABI. */
enum { GAL_PHYSICS_VERSION=1, GAL_BODY_STATIC=0,GAL_BODY_KINEMATIC=1,GAL_BODY_DYNAMIC=2 };
enum { GAL_BODY_FIXED_ROTATION=1,GAL_BODY_BULLET=2, GAL_SHAPE_SENSOR=1 };
enum { GAL_SHAPE_CIRCLE=0,GAL_SHAPE_BOX=1 };
enum { GAL_BODY_POSE=1,GAL_BODY_VELOCITY=2,GAL_BODY_IMPULSE=3,GAL_BODY_FORCE=4 };
enum { GAL_CONTACT_BEGIN=1,GAL_CONTACT_END=2,GAL_SENSOR_BEGIN=3,GAL_SENSOR_END=4 };
enum { GAL_EVENT_REMOVED_A=1,GAL_EVENT_REMOVED_B=2 };
typedef struct { uint32_t size,version;float gravity_x,gravity_y,step_seconds;uint32_t substeps,flags,reserved; } gal_physics_config;
typedef struct { uint32_t size,version,type,flags;float x,y,angle,vx,vy,angular_velocity,gravity_scale,linear_damping,angular_damping;uint32_t reserved; } gal_body_def;
/* Circle uses a=radius,b=0,angle=0; box uses a/b=positive half extents. Offset and
   angle are local to the body. Sensor shapes still contribute mass when density>0. */
typedef struct { uint32_t size,version,type,flags;float offset_x,offset_y,angle,a,b,density,friction,restitution;uint64_t category,mask;int32_t group;uint32_t reserved; } gal_shape_def;
typedef struct { uint32_t size,flags;float x,y,angle,vx,vy,angular_velocity;uint32_t type,reserved; } gal_body_state;
typedef struct { uint32_t size,events,dropped,step; } gal_physics_step_result;
typedef struct { uint32_t type,flags;uint64_t shape_a,shape_b,body_a,body_b; } gal_physics_event;
typedef struct { uint32_t size,version;float x,y,dx,dy;uint64_t category,mask; } gal_physics_ray;
typedef struct { uint32_t size,hit;uint64_t shape,body;float x,y,normal_x,normal_y,fraction;uint32_t reserved; } gal_physics_ray_hit;
typedef struct { uint32_t size,version;float lower_x,lower_y,upper_x,upper_y;uint64_t category,mask; } gal_physics_aabb;
typedef struct { uint32_t size,bodies,shapes,retired_shapes,steps,events,dropped,reserved; } gal_physics_state;
GAL_API int GAL_CALL gal_physics_open(gal_context*,const gal_physics_config*);
GAL_API int GAL_CALL gal_physics_close(gal_context*);
GAL_API int GAL_CALL gal_physics_create_body(gal_context*,const gal_body_def*,uint64_t* body);
GAL_API int GAL_CALL gal_physics_release_body(gal_context*,uint64_t body);
GAL_API int GAL_CALL gal_physics_create_shape(gal_context*,uint64_t body,const gal_shape_def*,uint64_t* shape);
GAL_API int GAL_CALL gal_physics_release_shape(gal_context*,uint64_t shape);
GAL_API int GAL_CALL gal_physics_body_command(gal_context*,uint64_t body,uint32_t command,float x,float y,float z);
GAL_API int GAL_CALL gal_physics_get_body(gal_context*,uint64_t body,gal_body_state*);
/* A successful step always advances once. dropped>0 explicitly reports event
   truncation; do not retry that step. Events are copied, not borrowed callbacks. */
GAL_API int GAL_CALL gal_physics_step(gal_context*,gal_physics_step_result*);
GAL_API int GAL_CALL gal_physics_events(gal_context*,gal_physics_event*,uint32_t capacity,uint32_t* count);
/* Closest ray ignores initial overlap, matching Box2D 3.1.1 convenience API. */
GAL_API int GAL_CALL gal_physics_ray_cast(gal_context*,const gal_physics_ray*,gal_physics_ray_hit*);
/* Broad-phase AABB query, not exact shape overlap. Sorted stable engine IDs.
   Insufficient capacity reports required count and leaves the array unchanged. */
GAL_API int GAL_CALL gal_physics_query_aabb(gal_context*,const gal_physics_aabb*,uint64_t*,uint32_t capacity,uint32_t* count);
GAL_API int GAL_CALL gal_physics_get_state(gal_context*,gal_physics_state*);
#ifdef __cplusplus
}
#endif
#endif
