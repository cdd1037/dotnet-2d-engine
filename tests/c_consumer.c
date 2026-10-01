#include "gal.h"
#include "gal_audio.h"
#include "gal_physics.h"
#include <stddef.h>
_Static_assert(sizeof(gal_physics_config)==32, "physics config");
_Static_assert(sizeof(gal_body_def)==56, "physics body");
_Static_assert(sizeof(gal_shape_def)==72, "physics shape");
_Static_assert(offsetof(gal_shape_def,category)==48, "physics filter offset");
_Static_assert(sizeof(gal_physics_event)==40, "physics event");
_Static_assert(sizeof(gal_physics_ray_hit)==48, "physics ray hit");
_Static_assert(sizeof(gal_audio_config)==16, "audio config");
_Static_assert(sizeof(gal_voice_state)==24, "voice state");
_Static_assert(sizeof(gal_audio_state)==32, "audio state");
_Static_assert(offsetof(gal_audio_state,decoded_bytes)==16, "PCM accounting offset");
_Static_assert(sizeof(gal_config)==24, "config");
_Static_assert(sizeof(gal_camera)==12, "camera");
_Static_assert(sizeof(gal_sprite)==32, "sprite");
_Static_assert(sizeof(gal_draw)==56, "affine draw");
_Static_assert(sizeof(gal_draw_v2)==88, "region draw");
_Static_assert(offsetof(gal_draw_v2,draw)==8, "region affine offset");
_Static_assert(offsetof(gal_draw_v2,source_x)==64, "region source offset");
_Static_assert(sizeof(gal_texture_info)==16, "texture dimensions");
_Static_assert(sizeof(gal_input)==32, "input");
_Static_assert(sizeof(gal_input_v2)==472, "input v2");
_Static_assert(offsetof(gal_input_v2,keys_down)==80, "input key offset");
_Static_assert(offsetof(gal_input_v2,consumed)==464, "input consumption offset");
_Static_assert(sizeof(gal_stats)==20, "stats");
int main(void){gal_config cfg={sizeof(gal_config),1,64,64,1,GAL_HEADLESS};gal_context* c=0;if(gal_create(&cfg,&c)!=0)return 1;gal_input_v2 input={0};input.size=sizeof(input);input.version=GAL_INPUT_VERSION;if(gal_poll_v2(c,&input)!=0||input.pixel_width!=64)return 2;return gal_destroy(c)!=0;}
