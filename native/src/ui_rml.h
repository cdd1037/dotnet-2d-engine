#pragma once
#include "gal_ui.h"
#include "gal_ui_model.h"
#include <SDL3/SDL.h>
#include <string>
struct UiRml;
UiRml* ui_create(SDL_GPUDevice*,SDL_Window*,const char*,std::string&);
void ui_destroy(UiRml*) noexcept;
bool ui_load(UiRml*,const char*,std::string&,bool game=false,const gal_bound_ui_target* targets=nullptr,uint32_t count=0,const char*const* images=nullptr,uint32_t image_count=0,const gal_ui_data_schema* schema=nullptr,uint32_t schema_count=0,const gal_ui_command* commands=nullptr,uint32_t command_count=0,const char* stylesheet=nullptr,const gal_ui_data_snapshot* initial=nullptr,const gal_ui_data_value* values=nullptr);
bool ui_set_game_model(UiRml*,const gal_game_ui_model&,std::string&);
bool ui_poll_game_action(UiRml*,gal_game_ui_action&,std::string&);
bool ui_game_test_command(UiRml*,uint32_t,uint32_t,std::string&);
bool ui_set_model(UiRml*,const gal_ui_model&,std::string&);
void ui_state(UiRml*,gal_ui_state&);
void ui_text_state(UiRml*,gal_ui_text_state&);
bool ui_poll_action(UiRml*,gal_ui_action&,std::string&);
bool ui_test_command(UiRml*,uint32_t,uint32_t,std::string&);
bool ui_input(UiRml*,const SDL_Event&);
void ui_window_state(UiRml*,bool focused,bool visible);
bool ui_keyboard_focus(UiRml*);
bool ui_render(UiRml*,SDL_GPUCommandBuffer*,SDL_GPUTexture*,int,int,std::string&);

bool ui_valid_utf8(const char*,size_t,size_t);
bool ui_apply_bound(UiRml*,const gal_bound_ui_snapshot&,const gal_bound_ui_value*,const gal_bound_ui_row*,std::string&);
bool ui_poll_bound(UiRml*,gal_bound_ui_action&,std::string&);
bool ui_test_bound(UiRml*,uint32_t,gal_bound_ui_action&,std::string&);

bool ui_apply_model(UiRml*,const gal_ui_data_snapshot&,const gal_ui_data_value*,std::string&);
bool ui_poll_model(UiRml*,gal_ui_event&,std::string&);
bool ui_test_model(UiRml*,uint32_t,const char*,uint32_t,gal_ui_event&,std::string&);
