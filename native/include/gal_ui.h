#ifndef GAL_UI_H
#define GAL_UI_H
#include "gal.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Optional experimental API v1. Main thread, outside active sprite frame.
   Fixed-size UTF8 buffers are NUL-terminated; no pointers/callbacks are retained.
   The first records are the original settings profile; additive profiles follow. */
typedef struct { uint32_t size,generation; int32_t volume; uint32_t reserved; char name[128],status[256]; } gal_ui_model;
typedef struct { uint32_t size,generation,action; int32_t volume; char name[128]; } gal_ui_action;
typedef struct { uint32_t size,generation,loaded,pending,queued,overflow,keyboard_focus; float scroll_top; char diagnostic[512]; } gal_ui_state;
enum { GAL_UI_APPLY=1,GAL_UI_RESET=2,GAL_UI_CHANGED=3 };
enum { GAL_UI_TEST_APPLY=1,GAL_UI_TEST_RESET=2,GAL_UI_TEST_FOCUS=3,GAL_UI_TEST_SCROLL=4,GAL_UI_TEST_TEXT=5,GAL_UI_TEST_CLICK_APPLY=6,
 GAL_UI_TEST_SDL_TAP=7,GAL_UI_TEST_SDL_CLICK=8,GAL_UI_TEST_SDL_DOWN=9,GAL_UI_TEST_SDL_UP=10,
 GAL_UI_TEST_SDL_WHEEL=11,GAL_UI_TEST_SDL_OUTSIDE=12,GAL_UI_TEST_RESIZE=13,GAL_UI_TEST_RESTORE_SIZE=14,
 GAL_UI_TEST_MINIMIZE=15,GAL_UI_TEST_RESTORE=16,GAL_UI_TEST_FOCUS_LOST=17,GAL_UI_TEST_FOCUS_GAINED=18,
 GAL_UI_TEST_PREEDIT_ASCII=19,GAL_UI_TEST_PREEDIT_CJK=20,GAL_UI_TEST_PREEDIT_END=21,GAL_UI_TEST_COMMIT_CJK=22,
 GAL_UI_TEST_BLUR=23,GAL_UI_TEST_SELECT_RANGE=24,GAL_UI_TEST_BAD_EDIT_UTF8=25,GAL_UI_TEST_BAD_EDIT_RANGE=26,
 GAL_UI_TEST_LONG_EDIT=27,GAL_UI_TEST_SELECT_END=28,GAL_UI_TEST_COMPOSITION_ESCAPE=29 };
GAL_API int GAL_CALL gal_ui_open(gal_context*,const char* rml_path,const char* font_path);
GAL_API int GAL_CALL gal_ui_close(gal_context*);
GAL_API int GAL_CALL gal_ui_set_model(gal_context*,const gal_ui_model*);
/* action=0 means queue empty. Overflow is explicit in get_state. */
GAL_API int GAL_CALL gal_ui_poll_action(gal_context*,gal_ui_action*);
GAL_API int GAL_CALL gal_ui_get_state(gal_context*,gal_ui_state*);
/* Read-only text bridge diagnostics v1. Value may contain a bounded preedit, not a committed model.
   flags: active context=1, composing=2, SDL text active=4, window focus=8,
   keyboard requested=16, candidate geometry valid=32, visible=64, SDL inline-composition capability=128. Rect is in WINDOW coordinates. */
typedef struct { uint32_t size,version,generation,flags; int32_t selection_start,selection_end,area_x,area_y,area_w,area_h;
 float caret_x,caret_y,line_height; uint32_t preedit_scalars,failures,reserved; char value[512],diagnostic[256]; } gal_ui_text_state;
GAL_API int GAL_CALL gal_ui_get_text_state(gal_context*,gal_ui_text_state*);
/* Deterministic probe input, not OS-input/IME verification. */
GAL_API int GAL_CALL gal_ui_test_command(gal_context*,uint32_t generation,uint32_t command);
GAL_API int GAL_CALL gal_capture_next(gal_context*,const char* bmp_path);
/* Fixed mission profile, separate from settings. Screen: title/play/pause/won/lost.
   flags: bit0 can-save, bit1 can-load. Set changes generation on screen/flags change. */
typedef struct { uint32_t size,generation,screen,seconds,flags,reserved; char title[128],objective[256],status[256]; } gal_game_ui_model;
typedef struct { uint32_t size,generation,action,reserved; } gal_game_ui_action;
enum { GAL_GAME_START=10,GAL_GAME_RESUME=11,GAL_GAME_SAVE=12,GAL_GAME_LOAD=13,GAL_GAME_RESTART=14,GAL_GAME_MENU=15,GAL_GAME_PAUSE=16 };
GAL_API int GAL_CALL gal_game_ui_open(gal_context*,const char* rml_path,const char* font_path);
GAL_API int GAL_CALL gal_game_ui_set_model(gal_context*,const gal_game_ui_model*);
GAL_API int GAL_CALL gal_game_ui_poll_action(gal_context*,gal_game_ui_action*);
/* Probe-only: 100/101 queue focus-lost/gained SDL events; 200+action uses Rml pointer hit-testing. */
GAL_API int GAL_CALL gal_game_ui_test_command(gal_context*,uint32_t generation,uint32_t command);
/* Bounded binding profile v1, additive to the settings/game ABIs. All arrays copied during call.
   1..32 targets, <=64 rows total. IDs ASCII, 1..47 bytes; text <=255 UTF8 bytes.
   Values are a complete ordered snapshot (target=index). Revision must advance by one.
   No retained pointers or callbacks. Each accepted snapshot retires prior actions. */
enum { GAL_BOUND_TEXT=1,GAL_BOUND_TEXT_INPUT=2,GAL_BOUND_BOOLEAN=3,GAL_BOUND_NUMBER=4,GAL_BOUND_ACTION=5,GAL_BOUND_LIST=6 };
typedef struct { uint32_t size,kind,action,reserved; char id[48]; } gal_bound_ui_target;
/* flags: enabled=1, boolean checked=2. number: integer 0..100 for NUMBER, otherwise zero. */
typedef struct { uint32_t size,target,flags,row_first,row_count,reserved; double number; char text[256]; } gal_bound_ui_value;
/* IDs nonzero and unique within a list. flags: enabled=1, selected=2. */
typedef struct { uint64_t id; uint32_t flags,reserved; char text[256]; } gal_bound_ui_row;
typedef struct { uint32_t size,version,generation,revision,value_count,row_count; } gal_bound_ui_snapshot;
typedef struct { uint32_t size,generation,revision,target,action,kind; uint64_t row; double number; uint32_t flags,reserved; char text[256]; } gal_bound_ui_action;
GAL_API int GAL_CALL gal_bound_ui_open(gal_context*,const char* rml_path,const char* font_path,const gal_bound_ui_target*,uint32_t count);
// Additive static-image profile. Paths are safe relative paths from rml_path.
// Every listed raster image is decoded/uploaded before staging succeeds, even if hidden.
// GAL_ENABLE_SVG adds strict static .svg sources, with a 1x1 parse/raster/upload preflight.
// SVGs use manifest-owned bytes and reserve resolved raster budgets before rendering.
// Maximum 32 unique images, 16 MiB encoded and 64 MiB RGBA per document.
GAL_API int GAL_CALL gal_bound_ui_open_images(gal_context*,const char* rml_path,const char* font_path,const gal_bound_ui_target*,uint32_t count,const char* const* image_paths,uint32_t image_count);
GAL_API int GAL_CALL gal_bound_ui_apply(gal_context*,const gal_bound_ui_snapshot*,const gal_bound_ui_value*,const gal_bound_ui_row*);
/* action=0 means queue empty; common gal_ui_get_state exposes overflow. */
GAL_API int GAL_CALL gal_bound_ui_poll(gal_context*,gal_bound_ui_action*);
/* Probe-only: 1 dispatch click, 2 hit-tested click, 3 focus, 4 set value/change, 5 read displayed value,
   6 pointer down, 7 pointer up, 8 queued preedit, 9 queued CJK commit, 10 queued astral commit. Generation/revision/target/row identify the current element. */
// Probe 11 returns owned raster image textures (excluding font/SVG textures) in number.
// Probe 12: SVG retained variants in number, SVG RGBA bytes in row; 13: SVG manifest count.
// Probe 14: persistent synthetic UI density 0.5..3 in number; 15: SDL window resize
// width in number and height in row (64..4096). Test helpers, not production display APIs.
GAL_API int GAL_CALL gal_bound_ui_test_command(gal_context*,uint32_t command,gal_bound_ui_action*);
#ifdef __cplusplus
}
#endif
#endif
