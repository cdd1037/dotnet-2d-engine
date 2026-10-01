#include "ui_rml.h"
#include "RmlUi_Platform_SDL.h"
#include "RmlUi_Renderer_SDL_GPU.h"
#include <RmlUi/Core.h>
#include <RmlUi/Core/Elements/ElementFormControlInput.h>
#include <array>
#include <cstdio>
#include <cstring>
#include <memory>
#include <algorithm>
#include <limits>

struct UiSystem final: SystemInterface_SDL {
 using SystemInterface_SDL::SystemInterface_SDL;
 char diagnostic[512]{}; uint32_t warnings=0; double elapsed=0;
 bool LogMessage(Rml::Log::Type type,const Rml::String& message) override {
  if(type<=Rml::Log::LT_WARNING){warnings++;std::snprintf(diagnostic,sizeof(diagnostic),"upstream:%s",message.c_str());}
  return true;
 }
 double GetElapsedTime() override{return elapsed;}
 void Clear(){warnings=0;diagnostic[0]=0;}
};
static uint32_t next_generation=1;
static const char* game_button(uint32_t action){switch(action){case 10:return "game-start";case 11:return "game-resume";case 12:return "game-save";case 13:return "game-load";case 14:return "game-restart";case 15:return "game-menu";case 16:return "game-pause";default:return nullptr;}}

struct UiRml final: Rml::EventListener {
 SDL_GPUDevice* device;SDL_Window* window;UiSystem system;
 std::unique_ptr<RenderInterface_SDL_GPU> renderer;
 TextInputMethodEditor_SDL ime;
 Rml::Context* current=nullptr;Rml::Context* pending=nullptr;
 Rml::ElementDocument* document=nullptr;Rml::ElementDocument* candidate=nullptr;
 uint32_t generation=0,serial=0;bool initialized=false;
 bool game=false,pending_game=false;uint32_t game_screen=0,game_flags=0;
 bool GameAllowed(uint32_t action)const{
  switch(action){case 10:return game_screen==0;case 11:return game_screen==2;case 12:return game_screen==2&&(game_flags&1);case 13:return (game_screen==0||game_screen==2)&&(game_flags&2);case 14:case 15:return game_screen>=2;case 16:return game_screen==1;default:return false;}
 }

 std::array<gal_ui_action,64> actions{};uint32_t first=0,count=0,overflow=0;
 char diagnostic[512]{};
 UiRml(SDL_GPUDevice*d,SDL_Window*w):device(d),window(w),system(w){}
 void ProcessEvent(Rml::Event& event) override {
  const auto& id=event.GetCurrentElement()->GetId();
  if(game){
   uint32_t action=0;for(uint32_t i=10;i<=16;i++)if(id==game_button(i)){action=i;break;}
   if(!GameAllowed(action))return;
   if(count==actions.size()){overflow++;return;}
   actions[(first+count)%actions.size()]={sizeof(gal_ui_action),generation,action,0,{}};count++;return;
  }
  uint32_t action=id=="apply"?GAL_UI_APPLY:id=="reset"?GAL_UI_RESET:GAL_UI_CHANGED;
  if(count==actions.size()){overflow++;return;}
  gal_ui_action value{sizeof(gal_ui_action),generation,action,0,{}};
  auto*name=dynamic_cast<Rml::ElementFormControlInput*>(document->GetElementById("player-name"));
  auto*volume=dynamic_cast<Rml::ElementFormControlInput*>(document->GetElementById("volume"));
  if(!name||!volume)return;
  const auto text=name->GetValue();if(text.size()>=sizeof(value.name)){overflow++;std::snprintf(diagnostic,sizeof(diagnostic),"UI event name exceeds 127 UTF8 bytes");return;}
  std::snprintf(value.name,sizeof(value.name),"%s",text.c_str());
  value.volume=std::clamp(std::atoi(volume->GetValue().c_str()),0,100);
  actions[(first+count)%actions.size()]=value;count++;
 }
 void DropCandidate(){if(pending)Rml::RemoveContext(pending->GetName());pending=nullptr;candidate=nullptr;}
};
static bool fail(std::string&e,const char*text){e=text;return false;}
static void remember(UiRml*u,const char*s){std::snprintf(u->diagnostic,sizeof(u->diagnostic),"%s",s);}
void ui_destroy(UiRml*u) noexcept {
 if(!u)return;
 try {
  if(u->initialized)Rml::Shutdown();
  if(u->renderer){u->renderer->Shutdown();u->renderer.reset();}
  Rml::SetTextInputHandler(nullptr);Rml::SetSystemInterface(nullptr);Rml::SetRenderInterface(nullptr);
  SDL_WaitForGPUIdle(u->device);
 }catch(...){/* No exception crosses destruction boundary. */}
 delete u;
}
UiRml* ui_create(SDL_GPUDevice*d,SDL_Window*w,const char*font,std::string&e){
 std::unique_ptr<UiRml,decltype(&ui_destroy)> u(new UiRml(d,w),ui_destroy);
 Rml::SetSystemInterface(&u->system);
 u->renderer=std::make_unique<RenderInterface_SDL_GPU>(d,w);
 if(u->system.warnings){e=u->system.diagnostic;return nullptr;}
 Rml::SetRenderInterface(u->renderer.get());Rml::SetTextInputHandler(&u->ime);
 if(!Rml::Initialise()){e="RmlUi initialization failed";return nullptr;}u->initialized=true;
 // Reuse the installed SC face explicitly; file loading owns font bytes.
 if(!Rml::LoadFontFace(font,"Noto Sans CJK SC",Rml::Style::FontStyle::Normal,Rml::Style::FontWeight::Normal,false,2)){
  e="font load failed (expected SC collection face index 2)";return nullptr;
 }
 if(u->system.warnings){e=u->system.diagnostic;return nullptr;}
 return u.release();
}
bool ui_load(UiRml*u,const char*path,std::string&e,bool game){
 u->pending_game=game;
 u->DropCandidate();Rml::Factory::ClearStyleSheetCache();u->system.Clear();u->diagnostic[0]=0;
 int w=0,h=0;SDL_GetWindowSizeInPixels(u->window,&w,&h);
 u->pending=Rml::CreateContext("gal-stage-"+std::to_string(++u->serial),{w,h});
 if(!u->pending)return fail(e,"could not create staging context");
 u->pending->SetDensityIndependentPixelRatio(SDL_GetWindowDisplayScale(u->window));
 u->candidate=u->pending->LoadDocument(path);
 bool valid=u->candidate;
 if(game){
  for(const char*id:{"game-panel","game-title","game-objective","game-status","game-time","game-actions","hud","hud-objective","hud-status","hud-time"})if(!u->candidate||!u->candidate->GetElementById(id))valid=false;
  for(uint32_t action=10;action<=16;action++)if(!u->candidate||!u->candidate->GetElementById(game_button(action))||u->candidate->GetElementById(game_button(action))->GetTagName()!="button")valid=false;
 }else{
  for(const char*id:{"player-name","volume","apply","reset","status","item-list"})if(!u->candidate||!u->candidate->GetElementById(id))valid=false;
  if(valid){for(const char*id:{"player-name","volume"})if(!dynamic_cast<Rml::ElementFormControlInput*>(u->candidate->GetElementById(id)))valid=false;}
 }
 if(!valid||u->system.warnings){remember(u,u->system.warnings?u->system.diagnostic:"missing required profile element");u->DropCandidate();return fail(e,u->diagnostic);}
 u->candidate->Show();u->pending->Update();
 if(u->system.warnings){remember(u,u->system.diagnostic);u->DropCandidate();return fail(e,u->diagnostic);}
 return true;
}
static bool utf8(const char*text,size_t cap,size_t max_scalars){
 size_t n=0;while(n<cap&&text[n])n++;if(n==cap)return false;
 size_t scalars=0;for(size_t i=0;i<n;){if(++scalars>max_scalars)return false;unsigned char c=text[i++];if(c<0x20||c==0x7f)return false;if(c<0x80)continue;uint32_t cp;int need;if(c>=0xc2&&c<=0xdf){cp=c&31;need=1;}else if(c>=0xe0&&c<=0xef){cp=c&15;need=2;}else if(c>=0xf0&&c<=0xf4){cp=c&7;need=3;}else return false;int length=need;while(need--){if(i>=n||(static_cast<unsigned char>(text[i])&0xc0)!=0x80)return false;cp=(cp<<6)|(text[i++]&63);}if((length==2&&cp<0x800)||(length==3&&cp<0x10000)||cp>0x10ffff||(cp>=0x80&&cp<=0x9f)||(cp>=0xd800&&cp<=0xdfff))return false;}
 return true;
}
bool ui_set_model(UiRml*u,const gal_ui_model&m,std::string&e){
 if(u->game||!u->document||m.generation!=u->generation)return fail(e,"stale UI generation or wrong profile");
 if(m.reserved||m.volume<0||m.volume>100||!utf8(m.name,sizeof(m.name),32)||!utf8(m.status,sizeof(m.status),255))return fail(e,"invalid UI model fields/UTF8");
 auto*name=dynamic_cast<Rml::ElementFormControlInput*>(u->document->GetElementById("player-name"));
 auto*volume=dynamic_cast<Rml::ElementFormControlInput*>(u->document->GetElementById("volume"));
 if(!name||!volume)return fail(e,"invalid input element types");
 name->SetValue(m.name);volume->SetValue(std::to_string(m.volume));
 u->document->GetElementById("status")->SetInnerRML(Rml::StringUtilities::EncodeRml(m.status));
 return true;
}
bool ui_set_game_model(UiRml*u,const gal_game_ui_model&m,std::string&e){
 if(!u->game||!u->document||m.generation!=u->generation)return fail(e,"stale game UI generation or wrong profile");
 if(m.screen>4||m.seconds>9999||m.flags>3||m.reserved||!utf8(m.title,sizeof(m.title),127)||!utf8(m.objective,sizeof(m.objective),255)||!utf8(m.status,sizeof(m.status),255))return fail(e,"invalid game UI model fields/UTF8");
 if(m.screen!=u->game_screen||m.flags!=u->game_flags){
  if(next_generation==std::numeric_limits<uint32_t>::max())return fail(e,"UI generation exhausted");
  u->generation=next_generation++;u->first=u->count=u->overflow=0;
 }
 u->game_screen=m.screen;u->game_flags=m.flags;
 auto text=[&](const char*id,const char*value){u->document->GetElementById(id)->SetInnerRML(Rml::StringUtilities::EncodeRml(value));};
 text("game-title",m.title);text("game-objective",m.objective);text("hud-objective",m.objective);text("game-status",m.status);text("hud-status",m.status);
 std::string timer="Time / 剩余: "+std::to_string(m.seconds)+" s";text("game-time",timer.c_str());text("hud-time",timer.c_str());
 u->document->GetElementById("game-panel")->SetProperty("display",m.screen==1?"none":"block");
 u->document->GetElementById("hud")->SetProperty("display",m.screen==1?"block":"none");
 for(uint32_t action=10;action<=16;action++)u->document->GetElementById(game_button(action))->SetProperty("display",u->GameAllowed(action)?"inline-block":"none");
 return true;
}
bool ui_poll_game_action(UiRml*u,gal_game_ui_action&a,std::string&e){
 if(!u->game)return fail(e,"game UI profile required");
 a={sizeof(a),u->generation,0,0};if(u->count){const auto&queued=u->actions[u->first];a.generation=queued.generation;a.action=queued.action;u->first=(u->first+1)%u->actions.size();u->count--;}return true;
}
bool ui_game_test_command(UiRml*u,uint32_t generation,uint32_t command,std::string&e){
 if(!u->game||!u->document||generation!=u->generation)return fail(e,"stale game UI generation or wrong profile");
 if(command==100||command==101){SDL_Event event{};event.type=command==100?SDL_EVENT_WINDOW_FOCUS_LOST:SDL_EVENT_WINDOW_FOCUS_GAINED;event.window.windowID=SDL_GetWindowID(u->window);if(!SDL_PushEvent(&event))return fail(e,"could not enqueue focus probe event");return true;}
 bool pointer=command>=210&&command<=216;if(pointer)command-=200;
 if(!u->GameAllowed(command))return fail(e,"game action unavailable on this screen");
 auto*element=u->document->GetElementById(game_button(command));
 if(pointer){auto offset=element->GetAbsoluteOffset();u->current->ProcessMouseMove(int(offset.x+8),int(offset.y+8),0);u->current->ProcessMouseButtonDown(0,0);u->current->ProcessMouseButtonUp(0,0);}
 else {element->DispatchEvent("click",{});}
 return true;
}
void ui_state(UiRml*u,gal_ui_state&s){s={sizeof(s),u->generation,u->document?1u:0u,u->pending?1u:0u,u->count,u->overflow,ui_keyboard_focus(u)?1u:0u,u->document&&!u->game?u->document->GetElementById("item-list")->GetScrollTop():0,{}};std::snprintf(s.diagnostic,sizeof(s.diagnostic),"%s",u->diagnostic);}
bool ui_poll_action(UiRml*u,gal_ui_action&a,std::string&e){if(u->game)return fail(e,"settings UI profile required");a={sizeof(a),u->generation,0,0,{}};if(u->count){a=u->actions[u->first];u->first=(u->first+1)%u->actions.size();u->count--;}return true;}
bool ui_test_command(UiRml*u,uint32_t generation,uint32_t command,std::string&e){
 if(u->game||!u->document||generation!=u->generation)return fail(e,"stale UI generation or wrong profile");
 if(command==GAL_UI_TEST_APPLY||command==GAL_UI_TEST_RESET)u->document->GetElementById(command==GAL_UI_TEST_APPLY?"apply":"reset")->DispatchEvent("click",{});
 else if(command==GAL_UI_TEST_FOCUS)u->document->GetElementById("player-name")->Focus();
 else if(command==GAL_UI_TEST_SCROLL)u->document->GetElementById("item-list")->SetScrollTop(150);
 else if(command==GAL_UI_TEST_TEXT)u->current->ProcessTextInput("星");
 else if(command==GAL_UI_TEST_CLICK_APPLY){auto*element=u->document->GetElementById("apply");auto offset=element->GetAbsoluteOffset();u->current->ProcessMouseMove(int(offset.x+8),int(offset.y+8),0);u->current->ProcessMouseButtonDown(0,0);u->current->ProcessMouseButtonUp(0,0);}
 else return fail(e,"unknown UI test command");
 return true;
}
void ui_input(UiRml*u,const SDL_Event&e){if(!u->current)return;SDL_Event copy=e;if(e.type==SDL_EVENT_TEXT_EDITING)u->ime.HandleEdit(e.edit);else RmlSDL::InputEventHandler(u->current,u->window,copy);}
bool ui_keyboard_focus(UiRml*u){if(!u->current)return false;auto*focused=u->current->GetFocusElement();return focused&&focused->GetTagName()=="input";}
bool ui_render(UiRml*u,SDL_GPUCommandBuffer*cmd,SDL_GPUTexture*target,int w,int h,std::string&e){
 u->system.elapsed+=1.0/60.0;
 if(u->pending){
  u->system.Clear();u->pending->SetDimensions({w,h});u->pending->Update();
  SDL_GPUTextureCreateInfo info{};info.type=SDL_GPU_TEXTURETYPE_2D;info.format=SDL_GetGPUSwapchainTextureFormat(u->device,u->window);info.usage=SDL_GPU_TEXTUREUSAGE_COLOR_TARGET;info.width=w;info.height=h;info.layer_count_or_depth=1;info.num_levels=1;
  SDL_GPUTexture*stage=SDL_CreateGPUTexture(u->device,&info);if(!stage)return fail(e,SDL_GetError());
  SDL_GPUColorTargetInfo clear{};clear.texture=stage;clear.load_op=SDL_GPU_LOADOP_CLEAR;clear.store_op=SDL_GPU_STOREOP_STORE;
  auto*pass=SDL_BeginGPURenderPass(cmd,&clear,1,nullptr);SDL_EndGPURenderPass(pass);
  u->renderer->BeginFrame(cmd,stage,w,h);u->pending->Render();u->renderer->EndFrame();SDL_ReleaseGPUTexture(u->device,stage);
  if(u->system.warnings){remember(u,u->system.diagnostic);u->DropCandidate();}
  else{
   if(next_generation==std::numeric_limits<uint32_t>::max())return fail(e,"UI generation exhausted");
   if(u->current)Rml::RemoveContext(u->current->GetName());
   u->current=u->pending;u->document=u->candidate;u->pending=nullptr;u->candidate=nullptr;
   u->generation=next_generation++;u->game=u->pending_game;u->game_screen=0;u->game_flags=0;u->first=u->count=u->overflow=0;u->diagnostic[0]=0;
   if(u->game){for(uint32_t action=10;action<=16;action++)u->document->GetElementById(game_button(action))->AddEventListener("click",u);}
   else{
   for(const char*id:{"apply","reset"})u->document->GetElementById(id)->AddEventListener("click",u);
   for(const char*id:{"player-name","volume"})u->document->GetElementById(id)->AddEventListener("change",u);
   }
  }
 }
 if(u->current){u->system.Clear();u->current->SetDimensions({w,h});u->current->SetDensityIndependentPixelRatio(SDL_GetWindowDisplayScale(u->window));u->current->Update();u->renderer->BeginFrame(cmd,target,w,h);u->current->Render();u->renderer->EndFrame();if(u->system.warnings){remember(u,u->system.diagnostic);return fail(e,u->diagnostic);}}
 return true;
}
