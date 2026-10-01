#include "ui_rml.h"
#include "text_input_geometry.h"
#include "RmlUi_Platform_SDL.h"
#include "RmlUi_Renderer_SDL_GPU.h"
#include <RmlUi/Core.h>
#include <RmlUi/Core/TextInputContext.h>
#include <RmlUi/Core/Elements/ElementFormControlInput.h>
#include <array>
#include <cstdio>
#include <cstring>
#include <memory>
#include <algorithm>
#include <limits>

static bool utf8(const char*,size_t,size_t);
struct UiSystem final: SystemInterface_SDL {
 SDL_Window* window;
 explicit UiSystem(SDL_Window*w):SystemInterface_SDL(w),window(w){}
 bool window_focused=true,window_visible=true,keyboard_requested=false,geometry_valid=false,area_sent=false;
 TextInputRect sent_area{};
 Rml::Vector2f caret{};float line_height=0;TextInputRect area{};
 char text_diagnostic[256]{};uint32_t text_failures=0;
 void TextFailure(const char*message){if(text_failures!=std::numeric_limits<uint32_t>::max())text_failures++;std::snprintf(text_diagnostic,sizeof(text_diagnostic),"%s",message);}
 void UpdateKeyboard(){
  if(!keyboard_requested||!window_focused||!window_visible){SDL_StopTextInput(window);return;}
  int ww=0,wh=0,pw=0,ph=0;
  geometry_valid=SDL_GetWindowSize(window,&ww,&wh)&&SDL_GetWindowSizeInPixels(window,&pw,&ph)&&text_input_rect(caret.x,caret.y,line_height,ww,wh,pw,ph,area);
  if(!geometry_valid){SDL_StopTextInput(window);TextFailure("Invalid text input viewport/caret geometry");return;}
  SDL_Rect rect{area.x,area.y,area.w,area.h};
  if(!area_sent||area.x!=sent_area.x||area.y!=sent_area.y||area.w!=sent_area.w||area.h!=sent_area.h){
   if(!SDL_SetTextInputArea(window,&rect,0)){TextFailure(SDL_GetError());return;}
   sent_area=area;area_sent=true;
  }
  if(!SDL_TextInputActive(window)&&!SDL_StartTextInput(window))TextFailure(SDL_GetError());
 }
 void ActivateKeyboard(Rml::Vector2f position,float height)override{caret=position;line_height=height;keyboard_requested=true;UpdateKeyboard();}
 void DeactivateKeyboard()override{keyboard_requested=false;geometry_valid=false;area_sent=false;SDL_StopTextInput(window);SDL_SetTextInputArea(window,nullptr,0);}
 void WindowFocus(bool focused){window_focused=focused;UpdateKeyboard();}
 void WindowVisible(bool visible){window_visible=visible;UpdateKeyboard();}
 char diagnostic[512]{}; uint32_t warnings=0; double elapsed=0;
 bool LogMessage(Rml::Log::Type type,const Rml::String& message) override {
  if(type<=Rml::Log::LT_WARNING){warnings++;std::snprintf(diagnostic,sizeof(diagnostic),"upstream:%s",message.c_str());}
  return true;
 }
 double GetElapsedTime() override{return elapsed;}
 void Clear(){warnings=0;diagnostic[0]=0;}
};

// Lifecycle/validation adapter around the unchanged upstream SDL composition editor.
// It does not generate candidates or implement an operating-system input method.
struct UiIme final: Rml::TextInputHandler {
 UiSystem& system;Rml::TextInputContext* context=nullptr;Rml::ElementFormControlInput* input=nullptr;
 std::unique_ptr<TextInputMethodEditor_SDL> editor;
 bool composing=false;uint32_t scalars=0;Rml::String original;int original_start=0,original_end=0;
 Rml::Rectanglef last_bounds{};int last_w=0,last_h=0;float last_density=0;bool bounds_valid=false;
 explicit UiIme(UiSystem&s):system(s){}
 void Fresh(){editor=std::make_unique<TextInputMethodEditor_SDL>();if(context)editor->OnActivate(context);}
 void ClearState(){composing=false;scalars=0;original.clear();input=nullptr;}
 void Cancel(bool keep_active=true,bool clear_os=true){
  if(composing&&context&&input){input->SetValue(original);context->SetCompositionRange(0,0);context->SetSelectionRange(original_start,original_end);}
  ClearState();editor.reset();if(keep_active&&context)Fresh();
  if(clear_os&&!SDL_ClearComposition(system.window))system.TextFailure(SDL_GetError());
 }
 void OnActivate(Rml::TextInputContext* value)override{
  if(context==value)return;
  Cancel(false);context=value;bounds_valid=false;Fresh();
 }
 void OnDeactivate(Rml::TextInputContext* value)override{
  if(context!=value)return;
  Cancel(false);context=nullptr;bounds_valid=false;
 }
 void OnDestroy(Rml::TextInputContext* value)override{
  if(context!=value)return;
  // The widget may already be tearing down: never call it from OnDestroy.
  ClearState();editor.reset();context=nullptr;bounds_valid=false;system.DeactivateKeyboard();
 }
 bool Edit(const SDL_TextEditingEvent&event,Rml::ElementFormControlInput* focused){
  if(!context||!focused||!system.window_focused||!system.window_visible)return false;
  if(!utf8(event.text,128,64)){system.TextFailure("Rejected preedit: invalid UTF8/control or more than 127 bytes/64 scalars");return true;}
  const int length=int(Rml::StringUtilities::LengthUTF8(Rml::String(event.text)));
  if(event.start< -1||event.length< -1||(event.start>=0&&event.length>=0&&(event.start>length||event.length>length-event.start))){system.TextFailure("Rejected preedit selection range");return true;}
  if(length==0){Cancel(true,false);return true;}
  if(!composing){original=focused->GetValue();context->GetSelectionRange(original_start,original_end);input=focused;}
  if(!editor)Fresh();
  editor->HandleEdit(event);composing=true;scalars=uint32_t(length);return true;
 }
 void RefreshGeometry(int w,int h){
  if(!context)return;
  Rml::Rectanglef bounds;if(!context->GetBoundingBox(bounds))return;
  const float density=SDL_GetWindowDisplayScale(system.window);
  if(!bounds_valid||bounds!=last_bounds||w!=last_w||h!=last_h||density!=last_density){
   last_bounds=bounds;last_w=w;last_h=h;last_density=density;bounds_valid=true;
   int start=0,end=0;context->GetSelectionRange(start,end);context->SetSelectionRange(start,end);
  }
  system.UpdateKeyboard();
 }
};

// Retire already-queued text for this window at document/owner boundaries.
// Keep pointer/key events and other windows; never flush on an ordinary focus event.
static bool SDLCALL keep_other_text(void* userdata,SDL_Event* event){
 const auto id=*static_cast<const SDL_WindowID*>(userdata);
 if(event->type==SDL_EVENT_TEXT_INPUT)return event->text.windowID!=id;
 if(event->type==SDL_EVENT_TEXT_EDITING)return event->edit.windowID!=id;
 if(event->type==SDL_EVENT_TEXT_EDITING_CANDIDATES)return event->edit_candidates.windowID!=id;
 return true;
}
static void discard_queued_text(SDL_Window*window){auto id=SDL_GetWindowID(window);SDL_FilterEvents(keep_other_text,&id);}
static uint32_t next_generation=1;
static const char* game_button(uint32_t action){switch(action){case 10:return "game-start";case 11:return "game-resume";case 12:return "game-save";case 13:return "game-load";case 14:return "game-restart";case 15:return "game-menu";case 16:return "game-pause";default:return nullptr;}}

struct UiRml final: Rml::EventListener {
 SDL_GPUDevice* device;SDL_Window* window;UiSystem system;
 std::unique_ptr<RenderInterface_SDL_GPU> renderer;
 UiIme ime;
 Rml::Context* current=nullptr;Rml::Context* pending=nullptr;
 Rml::ElementDocument* document=nullptr;Rml::ElementDocument* candidate=nullptr;
 uint32_t generation=0,serial=0;bool initialized=false;
 bool game=false,pending_game=false;uint32_t game_screen=0,game_flags=0;
 bool GameAllowed(uint32_t action)const{
  switch(action){case 10:return game_screen==0;case 11:return game_screen==2;case 12:return game_screen==2&&(game_flags&1);case 13:return (game_screen==0||game_screen==2)&&(game_flags&2);case 14:case 15:return game_screen>=2;case 16:return game_screen==1;default:return false;}
 }

 std::array<gal_ui_action,64> actions{};uint32_t first=0,count=0,overflow=0;
 char diagnostic[512]{};
 UiRml(SDL_GPUDevice*d,SDL_Window*w):device(d),window(w),system(w),ime(system){}
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
  u->ime.Cancel(false);u->system.DeactivateKeyboard();discard_queued_text(u->window);
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
 u->candidate->Show(Rml::ModalFlag::None,Rml::FocusFlag::None);u->pending->Update();
 if(u->system.warnings){remember(u,u->system.diagnostic);u->DropCandidate();return fail(e,u->diagnostic);}
 return true;
}
static bool utf8(const char*text,size_t cap,size_t max_scalars){
 if(!text)return false;
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
 u->ime.Cancel();
 name->SetValue(m.name);u->ime.bounds_valid=false;volume->SetValue(std::to_string(m.volume));
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
void ui_text_state(UiRml*u,gal_ui_text_state&s){
 s={};s.size=sizeof(s);s.version=1;s.generation=u->generation;
 const char*hint=SDL_GetHint(SDL_HINT_IME_IMPLEMENTED_UI);
 s.flags=(u->ime.context?1u:0u)|(u->ime.composing?2u:0u)|(SDL_TextInputActive(u->window)?4u:0u)|(u->system.window_focused?8u:0u)|(u->system.keyboard_requested?16u:0u)|(u->system.geometry_valid?32u:0u)|(u->system.window_visible?64u:0u)|((hint&&std::strstr(hint,"composition"))?128u:0u);
 if(u->ime.context)u->ime.context->GetSelectionRange(s.selection_start,s.selection_end);
 SDL_Rect rect{};SDL_GetTextInputArea(u->window,&rect,nullptr);s.area_x=rect.x;s.area_y=rect.y;s.area_w=rect.w;s.area_h=rect.h;
 s.caret_x=u->system.caret.x;s.caret_y=u->system.caret.y;s.line_height=u->system.line_height;s.preedit_scalars=u->ime.scalars;s.failures=u->system.text_failures;
 if(u->current)if(auto*input=dynamic_cast<Rml::ElementFormControlInput*>(u->current->GetFocusElement())){
  const auto value=input->GetValue();if(value.size()<sizeof(s.value))std::snprintf(s.value,sizeof(s.value),"%s",value.c_str());
  else{u->system.TextFailure("Focused value exceeds 511 UTF8 bytes");s.failures=u->system.text_failures;}
 }
 std::snprintf(s.diagnostic,sizeof(s.diagnostic),"%s",u->system.text_diagnostic);
}
bool ui_poll_action(UiRml*u,gal_ui_action&a,std::string&e){if(u->game)return fail(e,"settings UI profile required");a={sizeof(a),u->generation,0,0,{}};if(u->count){a=u->actions[u->first];u->first=(u->first+1)%u->actions.size();u->count--;}return true;}
bool ui_test_command(UiRml*u,uint32_t generation,uint32_t command,std::string&e){
 if(u->game||!u->document||generation!=u->generation)return fail(e,"stale UI generation or wrong profile");
 if(command==GAL_UI_TEST_APPLY||command==GAL_UI_TEST_RESET)u->document->GetElementById(command==GAL_UI_TEST_APPLY?"apply":"reset")->DispatchEvent("click",{});
 else if(command==GAL_UI_TEST_FOCUS)u->document->GetElementById("player-name")->Focus();
 else if(command==GAL_UI_TEST_SCROLL)u->document->GetElementById("item-list")->SetScrollTop(150);
 else if(command==GAL_UI_TEST_TEXT)u->current->ProcessTextInput("星");
 else if(command==GAL_UI_TEST_CLICK_APPLY){auto*element=u->document->GetElementById("apply");auto offset=element->GetAbsoluteOffset();u->current->ProcessMouseMove(int(offset.x+8),int(offset.y+8),0);u->current->ProcessMouseButtonDown(0,0);u->current->ProcessMouseButtonUp(0,0);}
 else if(command==GAL_UI_TEST_COMPOSITION_ESCAPE){for(bool down:{true,false}){SDL_Event event{};event.type=down?SDL_EVENT_KEY_DOWN:SDL_EVENT_KEY_UP;event.key.windowID=SDL_GetWindowID(u->window);event.key.key=SDLK_ESCAPE;event.key.scancode=SDL_SCANCODE_ESCAPE;event.key.down=down;if(!SDL_PushEvent(&event))return fail(e,"could not enqueue escape probe");}}
 else if(command==GAL_UI_TEST_BLUR)u->document->GetElementById("apply")->Focus();
 else if(command==GAL_UI_TEST_SELECT_RANGE||command==GAL_UI_TEST_SELECT_END){auto*input=dynamic_cast<Rml::ElementFormControlInput*>(u->document->GetElementById("player-name"));input->SetSelectionRange(command==GAL_UI_TEST_SELECT_RANGE?1:10000,command==GAL_UI_TEST_SELECT_RANGE?3:10000);}
 else if(command>=GAL_UI_TEST_PREEDIT_ASCII&&command<=GAL_UI_TEST_LONG_EDIT){
  SDL_Event event{};const auto id=SDL_GetWindowID(u->window);
  if(command==GAL_UI_TEST_COMMIT_CJK){event.type=SDL_EVENT_TEXT_INPUT;event.text.windowID=id;event.text.text="你";}
  else{
   static const std::string long_text(128,'a');
   event.type=SDL_EVENT_TEXT_EDITING;event.edit.windowID=id;
   event.edit.text=command==GAL_UI_TEST_PREEDIT_ASCII?"ni":command==GAL_UI_TEST_PREEDIT_CJK?"你好":command==GAL_UI_TEST_BAD_EDIT_UTF8?"\xff":command==GAL_UI_TEST_LONG_EDIT?long_text.c_str():command==GAL_UI_TEST_BAD_EDIT_RANGE?"ni":"";
   event.edit.start=command==GAL_UI_TEST_PREEDIT_CJK?1:command==GAL_UI_TEST_BAD_EDIT_RANGE?5:0;
   event.edit.length=command==GAL_UI_TEST_PREEDIT_CJK?1:command==GAL_UI_TEST_PREEDIT_END?0:2;
  }
  if(!SDL_PushEvent(&event))return fail(e,"could not enqueue text probe");
 }
 else if(command>=GAL_UI_TEST_SDL_TAP&&command<=GAL_UI_TEST_FOCUS_GAINED){
  const auto id=SDL_GetWindowID(u->window);
  auto push=[&](SDL_Event&event){if(!SDL_PushEvent(&event)){e="could not enqueue SDL input probe";return false;}return true;};
  auto key=[&](bool down){SDL_Event event{};event.type=down?SDL_EVENT_KEY_DOWN:SDL_EVENT_KEY_UP;event.key.windowID=id;event.key.scancode=SDL_SCANCODE_E;event.key.key=SDLK_E;event.key.down=down;return push(event);};
  if(command==GAL_UI_TEST_SDL_TAP){if(!key(true)||!key(false))return false;}
  else if(command==GAL_UI_TEST_SDL_DOWN){if(!key(true))return false;}
  else if(command==GAL_UI_TEST_SDL_UP){if(!key(false))return false;}
  else if(command==GAL_UI_TEST_RESIZE||command==GAL_UI_TEST_RESTORE_SIZE){if(!SDL_SetWindowSize(u->window,command==GAL_UI_TEST_RESIZE?384:960,command==GAL_UI_TEST_RESIZE?288:540))return fail(e,SDL_GetError());}
  else if(command>=GAL_UI_TEST_MINIMIZE){SDL_Event event{};event.window.windowID=id;event.type=command==GAL_UI_TEST_MINIMIZE?SDL_EVENT_WINDOW_MINIMIZED:command==GAL_UI_TEST_RESTORE?SDL_EVENT_WINDOW_RESTORED:command==GAL_UI_TEST_FOCUS_LOST?SDL_EVENT_WINDOW_FOCUS_LOST:SDL_EVENT_WINDOW_FOCUS_GAINED;if(!push(event))return false;}
  else{
   auto*element=u->document->GetElementById(command==GAL_UI_TEST_SDL_WHEEL?"item-list":"apply");auto offset=element->GetAbsoluteOffset();float density=SDL_GetWindowPixelDensity(u->window);if(density<=0)return fail(e,"invalid pixel density");
   float x=(offset.x+8)/density,y=(offset.y+8)/density;
   if(command==GAL_UI_TEST_SDL_OUTSIDE){int w=0,h=0;SDL_GetWindowSize(u->window,&w,&h);x=float(w-2);y=float(h-2);}
   SDL_Event motion{};motion.type=SDL_EVENT_MOUSE_MOTION;motion.motion.windowID=id;motion.motion.x=x;motion.motion.y=y;if(!push(motion))return false;
   if(command==GAL_UI_TEST_SDL_WHEEL){SDL_Event wheel{};wheel.type=SDL_EVENT_MOUSE_WHEEL;wheel.wheel.windowID=id;wheel.wheel.y=-1;if(!push(wheel))return false;}
   else for(bool down:{true,false}){SDL_Event button{};button.type=down?SDL_EVENT_MOUSE_BUTTON_DOWN:SDL_EVENT_MOUSE_BUTTON_UP;button.button.windowID=id;button.button.button=SDL_BUTTON_LEFT;button.button.down=down;button.button.x=x;button.button.y=y;if(!push(button))return false;}
  }
 }
 else return fail(e,"unknown UI test command");
 return true;
}
void ui_window_state(UiRml*u,bool focused,bool visible){u->system.WindowFocus(focused);u->system.WindowVisible(visible);}
bool ui_input(UiRml*u,const SDL_Event&e){
 if(e.type==SDL_EVENT_WINDOW_FOCUS_LOST){u->system.WindowFocus(false);u->ime.Cancel();return false;}
 if(e.type==SDL_EVENT_WINDOW_MINIMIZED){u->system.WindowVisible(false);u->ime.Cancel();return false;}
 if(e.type==SDL_EVENT_WINDOW_FOCUS_GAINED){u->system.WindowFocus(true);return false;}
 if(e.type==SDL_EVENT_WINDOW_RESTORED||e.type==SDL_EVENT_WINDOW_MAXIMIZED){u->system.WindowVisible(true);return false;}
 if(!u->current)return false;
 if((!u->system.window_focused||!u->system.window_visible)&&(e.type==SDL_EVENT_TEXT_EDITING||e.type==SDL_EVENT_TEXT_INPUT))return false;
 if(e.type==SDL_EVENT_TEXT_EDITING){auto*focused=dynamic_cast<Rml::ElementFormControlInput*>(u->current->GetFocusElement());return u->ime.Edit(e.edit,focused);}
 if(e.type==SDL_EVENT_TEXT_INPUT){
  if(!utf8(e.text.text,256,255)){u->system.TextFailure("Rejected committed text: invalid UTF8/control or more than 255 bytes");return u->ime.context!=nullptr;}
  if(u->ime.composing)u->ime.Cancel(true,false);
 }
 if(u->ime.composing&&(e.type==SDL_EVENT_KEY_DOWN||e.type==SDL_EVENT_KEY_UP)){
  if(e.type==SDL_EVENT_KEY_DOWN&&e.key.key==SDLK_ESCAPE)u->ime.Cancel();
  return true; // Candidate navigation/edit keys belong to the OS composition, not the underlying widget/game.
 }
 SDL_Event copy=e;return !RmlSDL::InputEventHandler(u->current,u->window,copy);
}
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
   u->ime.Cancel(false);u->system.DeactivateKeyboard();discard_queued_text(u->window);
   if(u->current)Rml::RemoveContext(u->current->GetName());
   u->current=u->pending;u->document=u->candidate;u->pending=nullptr;u->candidate=nullptr;
   u->document->Focus();
   u->generation=next_generation++;u->game=u->pending_game;u->game_screen=0;u->game_flags=0;u->first=u->count=u->overflow=0;u->diagnostic[0]=0;
   if(u->game){for(uint32_t action=10;action<=16;action++)u->document->GetElementById(game_button(action))->AddEventListener("click",u);}
   else{
   for(const char*id:{"apply","reset"})u->document->GetElementById(id)->AddEventListener("click",u);
   for(const char*id:{"player-name","volume"})u->document->GetElementById(id)->AddEventListener("change",u);
   }
  }
 }
 if(u->current){u->system.Clear();u->current->SetDimensions({w,h});u->current->SetDensityIndependentPixelRatio(SDL_GetWindowDisplayScale(u->window));u->current->Update();u->ime.RefreshGeometry(w,h);u->renderer->BeginFrame(cmd,target,w,h);u->current->Render();u->renderer->EndFrame();if(u->system.warnings){remember(u,u->system.diagnostic);return fail(e,u->diagnostic);}}
 return true;
}
