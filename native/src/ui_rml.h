#pragma once
#include "gal_ui.h"
#include <SDL3/SDL.h>
#include <string>
struct UiRml;
UiRml* ui_create(SDL_GPUDevice*,SDL_Window*,const char*,std::string&);
void ui_destroy(UiRml*) noexcept;
bool ui_load(UiRml*,const char*,std::string&,bool game=false);
bool ui_set_game_model(UiRml*,const gal_game_ui_model&,std::string&);
bool ui_poll_game_action(UiRml*,gal_game_ui_action&,std::string&);
bool ui_game_test_command(UiRml*,uint32_t,uint32_t,std::string&);
bool ui_set_model(UiRml*,const gal_ui_model&,std::string&);
void ui_state(UiRml*,gal_ui_state&);
bool ui_poll_action(UiRml*,gal_ui_action&,std::string&);
bool ui_test_command(UiRml*,uint32_t,uint32_t,std::string&);
bool ui_input(UiRml*,const SDL_Event&);
bool ui_keyboard_focus(UiRml*);
bool ui_render(UiRml*,SDL_GPUCommandBuffer*,SDL_GPUTexture*,int,int,std::string&);
