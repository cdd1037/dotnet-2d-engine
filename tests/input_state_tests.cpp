#include "input_state.h"
#include <cstdio>
#include <limits>
#define R(x) do{if(!(x)){std::fprintf(stderr,"INPUT FAIL %d %s\n",__LINE__,#x);return 1;}}while(0)
static bool has(const uint64_t* bits,int code){return (bits[code/64]&(uint64_t(1)<<(code%64)))!=0;}
int main(){
 InputState state;gal_input_v2 s{};state.begin(s);
 state.key(s,8,true,false,false);state.key(s,8,false,false,false);
 R(has(s.keys_pressed,8)&&has(s.keys_released,8)&&!has(s.keys_down,8));
 R(has(s.game_keys_pressed,8)&&has(s.game_keys_released,8)&&!has(s.game_keys_down,8));
 s={};state.begin(s);R(!has(s.keys_pressed,8)&&!has(s.keys_down,8));
 state.key(s,8,true,false,true);R(has(s.keys_pressed,8)&&has(s.keys_down,8)&&!has(s.game_keys_down,8));
 s={};state.begin(s);state.key(s,8,true,true,false);R(has(s.keys_down,8)&&!has(s.game_keys_down,8)&&!has(s.keys_pressed,8));
 state.key(s,8,false,false,false);state.key(s,8,true,false,false);R(has(s.game_keys_pressed,8)&&has(s.game_keys_down,8));
 state.capture_keyboard(s);R(!has(s.game_keys_down,8)&&!has(s.game_keys_pressed,8)&&has(s.game_keys_released,8));
 s={};state.begin(s);R(has(s.keys_down,8)&&!has(s.game_keys_down,8));
 state.key(s,8,false,false,false);state.key(s,4,true,false,false);state.key(s,7,true,false,true);
 R(has(s.game_keys_down,4)&&!has(s.game_keys_down,7)); // unrelated unconsumed key survives.
 state.button(s,1,true,true);R(s.buttons_down==1&&s.game_buttons_down==0);
 s={};state.begin(s);state.button(s,1,false,false);state.button(s,1,true,false);state.button(s,1,false,false);
 R(s.buttons_pressed==1&&s.buttons_released==1&&s.game_buttons_pressed==1&&s.game_buttons_released==1&&s.buttons_down==0);
 state.wheel(s,1,2,true);state.wheel(s,3,4,false);R(s.wheel_x==4&&s.wheel_y==6&&s.game_wheel_x==3&&s.game_wheel_y==4&&(s.consumed&GAL_CONSUMED_WHEEL));
 state.wheel(s,std::numeric_limits<float>::quiet_NaN(),0,false);R(s.wheel_x==4);
 state.key(s,511,true,false,false);R(has(s.keys_down,511));state.key(s,512,true,false,false);state.key(s,0,true,false,false);
 state.focus(s,false);R(!(s.flags&GAL_INPUT_FOCUSED)&&(s.flags&GAL_INPUT_FOCUS_CHANGED));
 R(!has(s.keys_down,4)&&has(s.keys_released,4)&&!has(s.game_keys_pressed,4)&&s.game_wheel_y==0);
 state.key(s,8,true,false,false);state.button(s,1,true,false);R(!has(s.keys_down,8)&&s.buttons_down==0);
 s={};state.begin(s);R(!(s.flags&GAL_INPUT_FOCUSED));state.focus(s,true);state.key(s,8,true,true,false);R(!has(s.keys_down,8));
 state.key(s,8,false,false,false);state.key(s,8,true,false,false);R(has(s.keys_pressed,8)&&has(s.game_keys_pressed,8));
 InputState::viewport(s,960,540,1920,1080,false);R((s.flags&GAL_INPUT_DRAWABLE)&&s.window_width==960&&s.pixel_width==1920);
 InputState::viewport(s,960,540,1920,1080,true);R(!(s.flags&GAL_INPUT_DRAWABLE));
 InputState::viewport(s,0,540,1920,1080,false);R(!(s.flags&GAL_INPUT_DRAWABLE)&&s.window_width==0);
 InputState::viewport(s,960,540,-1,1080,false);R(!(s.flags&GAL_INPUT_DRAWABLE)&&s.pixel_width==-1);
 InputState::viewport(s,960,540,20000,1080,false);R(!(s.flags&GAL_INPUT_DRAWABLE));
 InputState::viewport(s,384,288,384,288,false);R(s.flags&GAL_INPUT_DRAWABLE);
 std::puts("PASS event-derived input edges, consumed/held routing, focus boundaries and drawable viewport contracts");
}
