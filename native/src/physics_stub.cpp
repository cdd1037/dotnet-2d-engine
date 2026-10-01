#include "physics_backend.h"
bool physics_dispatch(PhysicsBackend*&,PhysicsOp,const void*,void*,std::string&e){e="physics module not enabled in this build";return false;}
void physics_destroy(PhysicsBackend*){}
