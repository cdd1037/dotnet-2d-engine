#include "gal.h"
_Static_assert(sizeof(gal_config)==24, "config");
_Static_assert(sizeof(gal_camera)==12, "camera");
_Static_assert(sizeof(gal_sprite)==32, "sprite");
_Static_assert(sizeof(gal_draw)==56, "affine draw");
_Static_assert(sizeof(gal_input)==32, "input");
_Static_assert(sizeof(gal_stats)==20, "stats");
int main(void){gal_config cfg={sizeof(gal_config),1,64,64,1,GAL_HEADLESS};gal_context* c=0;if(gal_create(&cfg,&c)!=0)return 1;return gal_destroy(c)!=0;}
