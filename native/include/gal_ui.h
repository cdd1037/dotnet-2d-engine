#ifndef GAL_UI_H
#define GAL_UI_H
#include "gal.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Optional experimental API v1. Main thread, outside active sprite frame.
   Fixed-size UTF8 buffers are NUL-terminated; no pointers/callbacks are retained.
   Settings model only: this is deliberately not a DOM binding. */
typedef struct { uint32_t size,generation; int32_t volume; uint32_t reserved; char name[128],status[256]; } gal_ui_model;
typedef struct { uint32_t size,generation,action; int32_t volume; char name[128]; } gal_ui_action;
typedef struct { uint32_t size,generation,loaded,pending,queued,overflow,keyboard_focus; float scroll_top; char diagnostic[512]; } gal_ui_state;
enum { GAL_UI_APPLY=1,GAL_UI_RESET=2,GAL_UI_CHANGED=3 };
enum { GAL_UI_TEST_APPLY=1,GAL_UI_TEST_RESET=2,GAL_UI_TEST_FOCUS=3,GAL_UI_TEST_SCROLL=4,GAL_UI_TEST_TEXT=5,GAL_UI_TEST_CLICK_APPLY=6,
 GAL_UI_TEST_SDL_TAP=7,GAL_UI_TEST_SDL_CLICK=8,GAL_UI_TEST_SDL_DOWN=9,GAL_UI_TEST_SDL_UP=10,
 GAL_UI_TEST_SDL_WHEEL=11,GAL_UI_TEST_SDL_OUTSIDE=12,GAL_UI_TEST_RESIZE=13,GAL_UI_TEST_RESTORE_SIZE=14,
 GAL_UI_TEST_MINIMIZE=15,GAL_UI_TEST_RESTORE=16,GAL_UI_TEST_FOCUS_LOST=17,GAL_UI_TEST_FOCUS_GAINED=18 };
GAL_API int GAL_CALL gal_ui_open(gal_context*,const char* rml_path,const char* font_path);
GAL_API int GAL_CALL gal_ui_close(gal_context*);
GAL_API int GAL_CALL gal_ui_set_model(gal_context*,const gal_ui_model*);
/* action=0 means queue empty. Overflow is explicit in get_state. */
GAL_API int GAL_CALL gal_ui_poll_action(gal_context*,gal_ui_action*);
GAL_API int GAL_CALL gal_ui_get_state(gal_context*,gal_ui_state*);
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
#ifdef __cplusplus
}
#endif
#endif
