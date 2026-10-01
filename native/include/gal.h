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
enum { GAL_HEADLESS=1, GAL_AUDIO=2 }; /* HEADLESS validates/submits CPU batches only, no rendering/audio. */
typedef struct { float x,y,zoom; } gal_camera;
typedef struct { float x,y,w,h,r,g,b,a; } gal_sprite;
/* Affine: x_world=m11*x+m21*y+tx; y_world=m12*x+m22*y+ty. Texture0 is builtin. */
typedef struct { float m11,m12,m21,m22,tx,ty,w,h,r,g,b,a; uint64_t texture; } gal_draw;
typedef struct { uint32_t size,quit,keys; float wheel,mouse_x,mouse_y; int32_t width,height; } gal_input;
enum { GAL_LEFT=1,GAL_RIGHT=2,GAL_UP=4,GAL_DOWN=8,GAL_SPACE=16,GAL_ESCAPE=32,GAL_INTERACT=64,GAL_DROP=128,GAL_TRANSITION=256,GAL_SAVE=512,GAL_LOAD=1024,GAL_FOCUS_LOST=2048 };
typedef struct { uint32_t size,frames,sprites,draw_calls; uint32_t audio_plays; } gal_stats;
GAL_API uint32_t GAL_CALL gal_abi_version(void);
GAL_API const char* GAL_CALL gal_last_error(void);
GAL_API int GAL_CALL gal_create(const gal_config*,gal_context**);
GAL_API int GAL_CALL gal_destroy(gal_context*);
GAL_API const char* GAL_CALL gal_backend(gal_context*);
GAL_API int GAL_CALL gal_poll(gal_context*,gal_input*);
GAL_API int GAL_CALL gal_begin(gal_context*,const gal_camera*);
GAL_API int GAL_CALL gal_submit(gal_context*,const gal_sprite*,uint32_t);
GAL_API int GAL_CALL gal_submit_draws(gal_context*,const gal_draw*,uint32_t);
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
