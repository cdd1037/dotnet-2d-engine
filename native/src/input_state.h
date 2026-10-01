#pragma once
#include "gal.h"
#include <array>
#include <cmath>

// Platform-independent event accumulator. State is owned by one backend/main thread.
struct InputState {
 std::array<uint64_t,GAL_KEY_WORDS> down{},game{},blocked{};
 uint32_t buttons=0,game_buttons=0,blocked_buttons=0;
 bool focused=true;
 void begin(gal_input_v2& s)const{
  s.flags=focused?GAL_INPUT_FOCUSED:0;
  for(int i=0;i<GAL_KEY_WORDS;i++){s.keys_down[i]=down[i];s.game_keys_down[i]=game[i];}
  s.buttons_down=buttons;s.game_buttons_down=game_buttons;
 }
 void key(gal_input_v2& s,uint32_t code,bool pressed,bool repeat,bool consumed){
  if(!focused||code==0||code>=GAL_KEY_COUNT)return;
  const uint32_t word=code/64;const uint64_t bit=uint64_t(1)<<(code%64);
  if(consumed)s.consumed|=GAL_CONSUMED_KEYBOARD;
  if(pressed){
   // Repeats cannot resurrect held keys after focus loss or UI capture.
   if(repeat)return;
   if(!(down[word]&bit))s.keys_pressed[word]|=bit;
   down[word]|=bit;
   if(consumed)blocked[word]|=bit;
   if(blocked[word]&bit){if(game[word]&bit)s.game_keys_released[word]|=bit;game[word]&=~bit;s.game_keys_pressed[word]&=~bit;}
   else {if(!(game[word]&bit))s.game_keys_pressed[word]|=bit;game[word]|=bit;}
  }else{
   if(down[word]&bit)s.keys_released[word]|=bit;
   if(game[word]&bit)s.game_keys_released[word]|=bit;
   down[word]&=~bit;game[word]&=~bit;blocked[word]&=~bit;
  }
  s.keys_down[word]=down[word];s.game_keys_down[word]=game[word];
 }
 void button(gal_input_v2& s,uint32_t number,bool pressed,bool consumed){
  if(!focused||number==0||number>32)return;
  uint32_t bit=uint32_t(1)<<(number-1);
  if(consumed)s.consumed|=GAL_CONSUMED_POINTER;
  if(pressed){
   if(!(buttons&bit))s.buttons_pressed|=bit;
   buttons|=bit;if(consumed)blocked_buttons|=bit;
   if(blocked_buttons&bit){if(game_buttons&bit)s.game_buttons_released|=bit;game_buttons&=~bit;s.game_buttons_pressed&=~bit;}
   else {if(!(game_buttons&bit))s.game_buttons_pressed|=bit;game_buttons|=bit;}
  }else{
   if(buttons&bit)s.buttons_released|=bit;
   if(game_buttons&bit)s.game_buttons_released|=bit;
   buttons&=~bit;game_buttons&=~bit;blocked_buttons&=~bit;
  }
  s.buttons_down=buttons;s.game_buttons_down=game_buttons;
 }
 void capture_keyboard(gal_input_v2& s){
  s.consumed|=GAL_CONSUMED_KEYBOARD;
  for(int i=0;i<GAL_KEY_WORDS;i++){blocked[i]|=down[i];s.game_keys_released[i]|=game[i];game[i]=s.game_keys_down[i]=s.game_keys_pressed[i]=0;}
 }
 void focus(gal_input_v2& s,bool value){
  if(value!=focused)s.flags|=GAL_INPUT_FOCUS_CHANGED;
  focused=value;
  if(value){s.flags|=GAL_INPUT_FOCUSED;return;}
  s.flags&=~GAL_INPUT_FOCUSED;
  for(int i=0;i<GAL_KEY_WORDS;i++){
   s.keys_released[i]|=down[i];s.game_keys_released[i]|=game[i];
   down[i]=game[i]=blocked[i]=0;s.keys_down[i]=s.game_keys_down[i]=s.keys_pressed[i]=s.game_keys_pressed[i]=0;
  }
  s.buttons_released|=buttons;s.game_buttons_released|=game_buttons;
  buttons=game_buttons=blocked_buttons=0;s.buttons_down=s.game_buttons_down=s.buttons_pressed=s.game_buttons_pressed=0;
  s.wheel_x=s.wheel_y=s.game_wheel_x=s.game_wheel_y=0;
 }
 void wheel(gal_input_v2& s,float x,float y,bool consumed)const{
  if(!focused||!std::isfinite(x)||!std::isfinite(y))return;
  s.wheel_x+=x;s.wheel_y+=y;
  if(consumed)s.consumed|=GAL_CONSUMED_WHEEL;else{s.game_wheel_x+=x;s.game_wheel_y+=y;}
 }
 static void viewport(gal_input_v2& s,int w,int h,int pw,int ph,bool minimized){
  s.window_width=w;s.window_height=h;s.pixel_width=pw;s.pixel_height=ph;
  s.flags&=~GAL_INPUT_DRAWABLE;
  if(!minimized&&w>0&&h>0&&pw>0&&ph>0&&w<=16384&&h<=16384&&pw<=16384&&ph<=16384)s.flags|=GAL_INPUT_DRAWABLE;
 }
};
