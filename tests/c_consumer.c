#include "gal.h"
#include "gal_audio.h"
#include "gal_ui.h"
#include "gal_ui_model.h"
#include "gal_physics.h"
#include <stddef.h>
_Static_assert(sizeof(gal_ui_data_schema)==64, "generic data schema");
_Static_assert(sizeof(gal_ui_command)==72, "generic command");
_Static_assert(sizeof(gal_ui_data_value)==288 && offsetof(gal_ui_data_value,text)==32, "generic copied value");
_Static_assert(sizeof(gal_ui_data_snapshot)==24, "generic snapshot");
_Static_assert(sizeof(gal_ui_argument)==280, "generic argument");
_Static_assert(sizeof(gal_ui_event)==1144 && offsetof(gal_ui_event,arguments)==24, "generic event");
_Static_assert(sizeof(gal_bound_ui_target)==64, "bound target");
_Static_assert(sizeof(gal_bound_ui_value)==288, "bound value");
_Static_assert(sizeof(gal_bound_ui_row)==272, "bound row");
_Static_assert(sizeof(gal_bound_ui_snapshot)==24, "bound snapshot");
_Static_assert(sizeof(gal_bound_ui_action)==304, "bound action");
_Static_assert(offsetof(gal_bound_ui_action,text)==48, "bound action text");
_Static_assert(sizeof(gal_ui_text_state)==832, "UI text state");
_Static_assert(offsetof(gal_ui_text_state,value)==64, "UI text payload offset");
_Static_assert(sizeof(gal_physics_config)==32, "physics config");
_Static_assert(sizeof(gal_body_def)==56, "physics body");
_Static_assert(sizeof(gal_shape_def)==72, "physics shape");
_Static_assert(offsetof(gal_shape_def,category)==48, "physics filter offset");
_Static_assert(GAL_CAPSULE_VERSION==1 && GAL_OVERLAP_QUERY_VERSION==1, "physics extension versions");
_Static_assert(GAL_QUERY_EXCLUDE_SENSORS==1 && GAL_QUERY_ONLY_SENSORS==2, "overlap sensor flags");
_Static_assert(sizeof(gal_capsule_def_v1)==72, "physics capsule");
_Static_assert(offsetof(gal_capsule_def_v1,x1)==16 && offsetof(gal_capsule_def_v1,radius)==32, "capsule geometry offset");
_Static_assert(offsetof(gal_capsule_def_v1,category)==48 && offsetof(gal_capsule_def_v1,group)==64 && offsetof(gal_capsule_def_v1,reserved2)==68, "capsule filter/reserved offsets");
_Static_assert(sizeof(gal_physics_overlap_query_v1)==56, "physics overlap query");
_Static_assert(offsetof(gal_physics_overlap_query_v1,x)==16 && offsetof(gal_physics_overlap_query_v1,angle)==32, "overlap geometry offset");
_Static_assert(offsetof(gal_physics_overlap_query_v1,reserved)==36 && offsetof(gal_physics_overlap_query_v1,category)==40 && offsetof(gal_physics_overlap_query_v1,mask)==48, "overlap filter/reserved offsets");
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
_Static_assert(GAL_MATERIAL_VERSION==1 && GAL_MATERIAL_DRAW_VERSION==1, "material versions");
_Static_assert(GAL_MATERIAL_PARAMETER_BYTES==32 && GAL_MATERIAL_CAPACITY==64, "material limits");
_Static_assert(sizeof(gal_material_desc)==16, "material descriptor");
_Static_assert(offsetof(gal_material_desc,fragment_bytes)==8, "material fragment length offset");
_Static_assert(offsetof(gal_material_desc,parameter_bytes)==12, "material parameter length offset");
_Static_assert(sizeof(gal_material_draw_v1)==136, "material draw");
_Static_assert(offsetof(gal_material_draw_v1,sprite)==8, "material sprite offset");
_Static_assert(offsetof(gal_material_draw_v1,material)==96, "material handle offset");
_Static_assert(offsetof(gal_material_draw_v1,parameters)==104, "material parameter offset");
_Static_assert(GAL_TARGET_VERSION==1 && GAL_TARGET_CAPACITY==8 && GAL_RENDER_PASS_VERSION==1 && GAL_RENDER_PASS_CAPACITY==16, "target/pass constants");
_Static_assert(sizeof(gal_target_desc)==16 && offsetof(gal_target_desc,width)==8, "target descriptor");
_Static_assert(sizeof(gal_render_pass_v1)==56, "render pass");
_Static_assert(offsetof(gal_render_pass_v1,target)==8 && offsetof(gal_render_pass_v1,camera)==16, "render pass target/camera");
_Static_assert(offsetof(gal_render_pass_v1,clear)==28 && offsetof(gal_render_pass_v1,first_draw)==44, "render pass clear/range");
_Static_assert(offsetof(gal_render_pass_v1,draw_count)==48 && offsetof(gal_render_pass_v1,reserved)==52, "render pass count/reserved");
_Static_assert(sizeof(gal_clip_rect)==32, "world scissor");
_Static_assert(offsetof(gal_clip_rect,flags)==8, "world scissor flags offset");
_Static_assert(offsetof(gal_clip_rect,x)==16, "world scissor x offset");
_Static_assert(offsetof(gal_clip_rect,width)==24, "world scissor width offset");
_Static_assert(offsetof(gal_clip_rect,height)==28, "world scissor height offset");
_Static_assert(sizeof(gal_texture_info)==16, "texture dimensions");
_Static_assert(sizeof(gal_input)==32, "input");
_Static_assert(sizeof(gal_input_v2)==472, "input v2");
_Static_assert(offsetof(gal_input_v2,keys_down)==80, "input key offset");
_Static_assert(offsetof(gal_input_v2,consumed)==464, "input consumption offset");
_Static_assert(sizeof(gal_stats)==20, "stats");
int main(void){
 gal_config cfg={sizeof(gal_config),1,64,64,1,GAL_HEADLESS};gal_context* c=0;if(gal_create(&cfg,&c)!=0)return 1;
 gal_input_v2 input={0};input.size=sizeof(input);input.version=GAL_INPUT_VERSION;if(gal_poll_v2(c,&input)!=0||input.pixel_width!=64)return 2;
 gal_camera camera={0,0,1};gal_clip_rect clip={sizeof(gal_clip_rect),GAL_CLIP_VERSION,GAL_CLIP_ENABLED,0,-1,-1,10,10};
 gal_draw_v2 draw={0};draw.size=sizeof(draw);draw.version=GAL_DRAW_VERSION;draw.draw.m11=draw.draw.m22=1;draw.draw.w=draw.draw.h=10;draw.draw.r=draw.draw.g=draw.draw.b=draw.draw.a=1;
 if(gal_begin(c,&camera)!=0||gal_submit_draws_clipped_v1(c,&draw,1,&clip,1)!=0||gal_end(c)!=0)return 3;
 const uint8_t fragment[]={3,2,35,7,0,0,1,0,0,0,0,0,1,0,0,0,0,0,0,0};
 gal_material_desc desc={sizeof(desc),GAL_MATERIAL_VERSION,sizeof(fragment),GAL_MATERIAL_PARAMETER_BYTES};uint64_t material=0;uint32_t live=0;
 if(gal_material_create_v1(c,&desc,fragment,&material)!=0||!material||gal_material_count(c,&live)!=0||live!=1)return 4;
 gal_material_draw_v1 custom={0};custom.size=sizeof(custom);custom.version=GAL_MATERIAL_DRAW_VERSION;custom.sprite=draw;custom.material=material;custom.parameters[0]=.5f;custom.parameters[7]=-2;
 if(gal_begin(c,&camera)!=0||gal_submit_material_draws_v1(c,&custom,1,&clip,1)!=0||gal_end(c)!=0)return 5;
 if(gal_material_release(c,material)!=0||gal_material_count(c,&live)!=0||live!=0)return 6;
 gal_target_desc target_desc={sizeof(target_desc),GAL_TARGET_VERSION,16,8};uint64_t target=0;
 if(gal_target_create_v1(c,&target_desc,&target)!=0||!target||gal_texture_count(c,&live)!=0||live!=1)return 7;
 gal_texture_info info={sizeof(info),0,0,0};if(gal_texture_get_info(c,target,&info)!=0||info.width!=16||info.height!=8||gal_texture_release(c,target)!=-1)return 8;
 gal_render_pass_v1 passes[2]={{sizeof(gal_render_pass_v1),GAL_RENDER_PASS_VERSION,target,{0,0,1},{0,0,0,0},0,0,0},{sizeof(gal_render_pass_v1),GAL_RENDER_PASS_VERSION,0,{0,0,1},{0,0,0,1},0,1,0}};
 custom.material=0;for(unsigned i=0;i<8;i++)custom.parameters[i]=0;custom.sprite.draw.texture=target;
 if(gal_render_frame_v1(c,passes,2,&custom,1,&clip,1)!=0)return 9;
 if(gal_target_release(c,target)!=0||gal_texture_count(c,&live)!=0||live!=0||gal_target_release(c,target)!=-1)return 10;
 /* This is a C11 signature/export check, including nested const pointers.
    UI remains unsupported for a headless context, even with valid inputs. */
 gal_bound_ui_target binding={sizeof(binding),1,0,0,"label"};const char* images[]={"image.png"};
 if(gal_bound_ui_open_images(c,"sample.rml","font.ttc",&binding,1,images,1)!=-1)return 11;
 if(gal_bound_ui_open_images(c,0,0,0,0,0,0)!=-1)return 12;
 if(gal_bound_ui_open_images(c,"sample.rml","font.ttc",&binding,1,0,1)!=-1)return 13;
 if(gal_bound_ui_open_images(c,"sample.rml","font.ttc",&binding,1,images,33)!=-1)return 14;
 /* New exports remain callable from C in both enabled and disabled builds.
    A world was deliberately never opened, so both calls must fail safely. */
 gal_capsule_def_v1 capsule={0};capsule.size=sizeof(capsule);capsule.version=GAL_CAPSULE_VERSION;
 gal_physics_overlap_query_v1 overlap={0};overlap.size=sizeof(overlap);overlap.version=GAL_OVERLAP_QUERY_VERSION;
 uint64_t shape=0;uint32_t count=0;
 if(gal_physics_create_capsule_v1(c,0,&capsule,&shape)!=-1)return 15;
 if(gal_physics_query_overlap_v1(c,&overlap,0,0,&count)!=-1)return 16;
 return gal_destroy(c)!=0;
}
