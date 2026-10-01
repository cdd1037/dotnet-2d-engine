#ifndef GAL_H
#define GAL_H
#include <stdint.h>
#ifdef _WIN32
#ifdef GAL_BUILD
#define GAL_API __declspec(dllexport)
#else
#define GAL_API __declspec(dllimport)
#endif
#define GAL_CALL __cdecl
#else
#define GAL_API __attribute__((visibility("default")))
#define GAL_CALL
#endif
#ifdef __cplusplus
extern "C" {
#endif
/* ABI v1. All calls on the creating/main thread. Exactly one live context.
   All pointer arguments are borrowed for the call. UTF-8 errors borrowed until next call.
   Status: 0 success, -1 invalid/state/backend failure. No exception crosses ABI. */
typedef struct gal_context gal_context;
typedef struct { uint32_t size, abi_version; int32_t width,height; uint32_t max_sprites,flags; } gal_config;
enum { GAL_HEADLESS=1, GAL_AUDIO=2 }; /* HEADLESS disables automatic graphics/legacy tone. Explicit gal_audio.h mixer opening is independent. */
typedef struct { float x,y,zoom; } gal_camera;
typedef struct { float x,y,w,h,r,g,b,a; } gal_sprite;
/* Affine: x_world=m11*x+m21*y+tx; y_world=m12*x+m22*y+ty. Texture0 is builtin. */
typedef struct { float m11,m12,m21,m22,tx,ty,w,h,r,g,b,a; uint64_t texture; } gal_draw;
/* Additive draw v2. All-zero source rect selects legacy full texture UVs.
   Otherwise positive integer texel rectangle must fit its texture. Region endpoints
   sample texel centers with linear filtering, preventing adjacent atlas bleed.
   Flips change sampling only, never geometry/pivot/order. Unknown bits rejected. */
enum { GAL_DRAW_VERSION=2, GAL_FLIP_X=1, GAL_FLIP_Y=2 };
typedef struct { uint32_t size,version; gal_draw draw; int32_t source_x,source_y,source_w,source_h; uint32_t flags,reserved; } gal_draw_v2;
/* Trusted, offline-prepared SPIR-V 1.0 fragment shaders only. The caller verifies
   the fixed sprite interface: main entry point, vec2 UV at location 0, vec4 color
   at location 1, straight-alpha vec4 output at location 0, one sampler at set 2
   binding 0, and one 32-byte std140 uniform buffer at set 3 binding 0. This API
   validates the bounded header, not shader semantics or resource reflection.
   Material creation/release must occur outside a frame. At most 64 are live;
   handles belong to their creating context and are invalid after release/destroy.
   Fragment code is borrowed only during create. Failed create returns handle 0.
   Headless contexts validate the header and ownership without compiling it. */
enum { GAL_MATERIAL_VERSION=1, GAL_MATERIAL_DRAW_VERSION=1, GAL_MATERIAL_PARAMETER_BYTES=32, GAL_MATERIAL_CAPACITY=64 };
typedef struct { uint32_t size,version,fragment_bytes,parameter_bytes; } gal_material_desc;
/* Parameters are copied per draw; all must be finite. Material 0 selects the
   existing sprite pipeline and requires all parameters to be zero. */
typedef struct { uint32_t size,version; gal_draw_v2 sprite; uint64_t material; float parameters[8]; } gal_material_draw_v1;
/* Explicit RGBA8 render targets own a private attachment and a sampled texture.
   The returned identity is also a borrowed texture binding; release it only with
   gal_target_release. Dimensions are 1..4096, at most eight targets, with a 64 MiB
   combined budget (8 bytes/pixel for the pair). Target create/release is forbidden
   during a legacy frame. Targets start transparent; sampled colors remain straight
   alpha after an internal resolve. Headless targets validate ownership only. */
enum { GAL_TARGET_VERSION=1, GAL_TARGET_CAPACITY=8, GAL_RENDER_PASS_VERSION=1, GAL_RENDER_PASS_CAPACITY=16 };
typedef struct { uint32_t size,version; int32_t width,height; } gal_target_desc;
/* Exactly the last pass targets the window (0); earlier passes target live owned
   targets. Ranges must partition the draw array in order. Cameras and scissors use
   each target's pixels, or the last polled framebuffer for the window. All clear
   channels must be finite in [0,1]. Every pass clears its attachment. */
typedef struct { uint32_t size,version; uint64_t target; gal_camera camera; float clear[4]; uint32_t first_draw,draw_count,reserved; } gal_render_pass_v1;
/* Additive world scissor v1, in framebuffer pixels with a top-left origin.
   Enabled extents are nonnegative; zero area clips everything. Coordinates may
   span int32 and are intersected safely with the acquired framebuffer. Disabled
   rectangles require all four coordinates/extents to be zero. Unknown bits and
   nonzero reserved are rejected. Scissors do not affect the clear or UI pass. */
enum { GAL_CLIP_VERSION=1, GAL_CLIP_ENABLED=1 };
typedef struct { uint32_t size,version,flags,reserved; int32_t x,y,width,height; } gal_clip_rect;
typedef struct { uint32_t size; int32_t width,height; uint32_t reserved; } gal_texture_info;
typedef struct { uint32_t size,quit,keys; float wheel,mouse_x,mouse_y; int32_t width,height; } gal_input;
enum { GAL_LEFT=1,GAL_RIGHT=2,GAL_UP=4,GAL_DOWN=8,GAL_SPACE=16,GAL_ESCAPE=32,GAL_INTERACT=64,GAL_DROP=128,GAL_TRANSITION=256,GAL_SAVE=512,GAL_LOAD=1024,GAL_FOCUS_LOST=2048 };
/* Additive input contract v2. Poll exactly one version per tick: both consume events.
   Key indexes are SDL3 physical scancodes (0..511), not text or layout characters.
   Pointer coordinates are logical window units; dimensions are separate pixel units.
   Edges mean at least one transition since the previous poll, not an ordered queue.
   game_* excludes UI-consumed inputs, latched until release. Raw state remains observable.
   Positive viewport dimensions + DRAWABLE are required for coordinate conversion. */
enum { GAL_INPUT_VERSION=2, GAL_KEY_WORDS=8, GAL_KEY_COUNT=512 };
enum { GAL_INPUT_FOCUSED=1, GAL_INPUT_DRAWABLE=2, GAL_INPUT_FOCUS_CHANGED=4 };
enum { GAL_CONSUMED_KEYBOARD=1, GAL_CONSUMED_POINTER=2, GAL_CONSUMED_WHEEL=4, GAL_CONSUMED_TEXT=8 };
typedef struct {
 uint32_t size,version,quit,flags;
 int32_t window_width,window_height,pixel_width,pixel_height;
 float mouse_x,mouse_y,wheel_x,wheel_y,game_wheel_x,game_wheel_y;
 uint32_t buttons_down,buttons_pressed,buttons_released;
 uint32_t game_buttons_down,game_buttons_pressed,game_buttons_released;
 uint64_t keys_down[GAL_KEY_WORDS],keys_pressed[GAL_KEY_WORDS],keys_released[GAL_KEY_WORDS];
 uint64_t game_keys_down[GAL_KEY_WORDS],game_keys_pressed[GAL_KEY_WORDS],game_keys_released[GAL_KEY_WORDS];
 uint32_t consumed,reserved;
} gal_input_v2;
typedef struct { uint32_t size,frames,sprites,draw_calls; uint32_t audio_plays; } gal_stats;
GAL_API uint32_t GAL_CALL gal_abi_version(void);
GAL_API const char* GAL_CALL gal_last_error(void);
GAL_API int GAL_CALL gal_create(const gal_config*,gal_context**);
GAL_API int GAL_CALL gal_destroy(gal_context*);
GAL_API const char* GAL_CALL gal_backend(gal_context*);
GAL_API int GAL_CALL gal_poll(gal_context*,gal_input*);
GAL_API int GAL_CALL gal_poll_v2(gal_context*,gal_input_v2*);
GAL_API int GAL_CALL gal_begin(gal_context*,const gal_camera*);
GAL_API int GAL_CALL gal_submit(gal_context*,const gal_sprite*,uint32_t);
GAL_API int GAL_CALL gal_submit_draws(gal_context*,const gal_draw*,uint32_t);
GAL_API int GAL_CALL gal_submit_draws_v2(gal_context*,const gal_draw_v2*,uint32_t);
/* clip_count 0: unclipped; 1: broadcast; otherwise must equal draw_count.
   Validate supplied clips even for zero draws. Every batch is atomic on failure.
   Legacy submit calls always use an unclipped scissor, including in mixed frames. */
GAL_API int GAL_CALL gal_submit_draws_clipped_v1(gal_context*,const gal_draw_v2*,uint32_t draw_count,const gal_clip_rect*,uint32_t clip_count);
/* Same atomic batch/clip contract as gal_submit_draws_clipped_v1. Existing submit
   entry points always restore the default material and zero parameters. */
GAL_API int GAL_CALL gal_submit_material_draws_v1(gal_context*,const gal_material_draw_v1*,uint32_t draw_count,const gal_clip_rect*,uint32_t clip_count);
GAL_API int GAL_CALL gal_material_create_v1(gal_context*,const gal_material_desc*,const uint8_t* fragment,uint64_t* material);
GAL_API int GAL_CALL gal_material_release(gal_context*,uint64_t material);
GAL_API int GAL_CALL gal_material_count(gal_context*,uint32_t* count);
GAL_API int GAL_CALL gal_target_create_v1(gal_context*,const gal_target_desc*,uint64_t* target);
GAL_API int GAL_CALL gal_target_release(gal_context*,uint64_t target);
/* Atomic validation precedes all rendering. Rejects an active legacy frame and
   sampling the current attachment's target identity. Clips have the same 0/1/N
   contract as material submissions. Counts one frame and all user draws; GPU draw
   statistics include internal target resolves and exclude UI. A backend failure
   may leave executed target contents changed; a validation failure never does. */
GAL_API int GAL_CALL gal_render_frame_v1(gal_context*,const gal_render_pass_v1*,uint32_t pass_count,const gal_material_draw_v1*,uint32_t draw_count,const gal_clip_rect*,uint32_t clip_count);
GAL_API int GAL_CALL gal_texture_get_info(gal_context*,uint64_t,gal_texture_info*);
GAL_API int GAL_CALL gal_texture_load_bmp(gal_context*,const char* utf8_path,uint64_t* texture);
GAL_API int GAL_CALL gal_texture_release(gal_context*,uint64_t texture);
GAL_API int GAL_CALL gal_texture_count(gal_context*,uint32_t* count);
GAL_API int GAL_CALL gal_end(gal_context*);
GAL_API int GAL_CALL gal_abort(gal_context*); /* discard active frame; does not render/count */
GAL_API int GAL_CALL gal_play_tone(gal_context*);
GAL_API int GAL_CALL gal_get_stats(gal_context*,gal_stats*);
#ifdef __cplusplus
}
#endif
#endif
