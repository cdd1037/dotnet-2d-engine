#include "backend.h"
#include "shaders_spv.h"
#include "resolve_spv.h"
#include "input_state.h"
#include "clip_rect.h"
#include "image_loader.h"
#include <SDL3/SDL.h>
#include <cmath>
#include <algorithm>
#include <cstring>
#include <cstdlib>
#include <memory>
#include <vector>
#ifdef GAL_ENABLE_RMLUI
#include "ui_rml.h"
#endif
struct Backend {
#ifdef GAL_ENABLE_RMLUI
 UiRml* ui=nullptr;
#endif
 SDL_Window* window=nullptr; SDL_GPUDevice* device=nullptr;
 SDL_GPUGraphicsPipeline* pipeline=nullptr; SDL_GPUGraphicsPipeline* target_pipeline=nullptr; SDL_GPUGraphicsPipeline* resolve_pipeline=nullptr; SDL_GPUBuffer* vertices=nullptr;
 SDL_GPUTransferBuffer* transfer=nullptr; SDL_GPUTexture* texture=nullptr; SDL_GPUSampler* sampler=nullptr;
 SDL_AudioStream* audio=nullptr; SDL_InitFlags init_flags=0; bool claimed=false; bool drawable=true; bool minimized=false; int projection_width=0,projection_height=0; InputState input;
 std::vector<float> tone;
 struct Texture { uint64_t first;SDL_GPUTexture* second;uint64_t rgba_bytes=0; };
 std::vector<Texture> textures;uint64_t texture_rgba_bytes=0;
 struct Material { uint64_t id; SDL_GPUGraphicsPipeline* window; SDL_GPUGraphicsPipeline* target; };
 struct Target { uint64_t id; SDL_GPUTexture* attachment; SDL_GPUTexture* sampled; int32_t width,height; };
 std::vector<Material> materials;
 std::vector<Target> targets;
 std::string capture_path; bool captured=false; SDL_GPUTexture* capture_texture=nullptr; SDL_GPUTransferBuffer* readback=nullptr;
};
static bool error(std::string& e) { e=SDL_GetError(); return false; }
void backend_destroy(Backend* b) {
 if(!b) return;
 #ifdef GAL_ENABLE_RMLUI
 ui_destroy(b->ui);b->ui=nullptr;
#endif
 if(b->audio) SDL_DestroyAudioStream(b->audio);
 if(b->device) {
  SDL_WaitForGPUIdle(b->device);
  for(auto&item:b->textures)SDL_ReleaseGPUTexture(b->device,item.second);
  for(auto&item:b->targets)SDL_ReleaseGPUTexture(b->device,item.attachment);
  for(auto&item:b->materials){SDL_ReleaseGPUGraphicsPipeline(b->device,item.window);SDL_ReleaseGPUGraphicsPipeline(b->device,item.target);}
  if(b->capture_texture) SDL_ReleaseGPUTexture(b->device,b->capture_texture);
  if(b->readback) SDL_ReleaseGPUTransferBuffer(b->device,b->readback);
  if(b->pipeline) SDL_ReleaseGPUGraphicsPipeline(b->device,b->pipeline);
  if(b->target_pipeline) SDL_ReleaseGPUGraphicsPipeline(b->device,b->target_pipeline);
  if(b->resolve_pipeline) SDL_ReleaseGPUGraphicsPipeline(b->device,b->resolve_pipeline);
  if(b->vertices) SDL_ReleaseGPUBuffer(b->device,b->vertices);
  if(b->transfer) SDL_ReleaseGPUTransferBuffer(b->device,b->transfer);
  if(b->texture) SDL_ReleaseGPUTexture(b->device,b->texture);
  if(b->sampler) SDL_ReleaseGPUSampler(b->device,b->sampler);
  if(b->claimed) SDL_ReleaseWindowFromGPUDevice(b->device,b->window);
  SDL_DestroyGPUDevice(b->device);
 }
 if(b->window) SDL_DestroyWindow(b->window);
 if(b->init_flags) SDL_QuitSubSystem(b->init_flags);
 delete b;
}
static SDL_GPUGraphicsPipeline* create_sprite_pipeline(Backend*b,const uint8_t*fragment,uint32_t fragment_bytes,uint32_t uniform_buffers,SDL_GPUTextureFormat format,std::string&e){
 // The byte-oriented C ABI does not require pointer alignment. SDL/Vulkan receive
 // an aligned copy for compilation, and all shader objects are released on exit.
 std::vector<uint32_t> aligned_fragment(fragment_bytes/4);
 std::memcpy(aligned_fragment.data(),fragment,fragment_bytes);
 struct Shaders {
  SDL_GPUDevice* device; SDL_GPUShader* vertex=nullptr; SDL_GPUShader* fragment=nullptr;
  ~Shaders(){if(fragment)SDL_ReleaseGPUShader(device,fragment);if(vertex)SDL_ReleaseGPUShader(device,vertex);}
 } shaders{b->device};
 SDL_GPUShaderCreateInfo si{}; si.code=sprite_vert_spv; si.code_size=sizeof(sprite_vert_spv); si.entrypoint="main"; si.format=SDL_GPU_SHADERFORMAT_SPIRV; si.stage=SDL_GPU_SHADERSTAGE_VERTEX;
 auto*vs=shaders.vertex=SDL_CreateGPUShader(b->device,&si); if(!vs){error(e);return nullptr;}
 si.code=reinterpret_cast<const Uint8*>(aligned_fragment.data()); si.code_size=fragment_bytes; si.stage=SDL_GPU_SHADERSTAGE_FRAGMENT; si.num_samplers=1; si.num_uniform_buffers=uniform_buffers;
 auto*fs=shaders.fragment=SDL_CreateGPUShader(b->device,&si); if(!fs){error(e);return nullptr;}
 SDL_GPUVertexBufferDescription binding{}; binding.slot=0;binding.pitch=sizeof(Vertex);binding.input_rate=SDL_GPU_VERTEXINPUTRATE_VERTEX;
 SDL_GPUVertexAttribute attrs[3]{};
 attrs[0].location=0; attrs[0].format=SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2; attrs[0].offset=0;
 attrs[1].location=1; attrs[1].format=SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2; attrs[1].offset=8;
 attrs[2].location=2; attrs[2].format=SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4; attrs[2].offset=16;
 SDL_GPUColorTargetDescription color{}; color.format=format;
 color.blend_state.enable_blend=true; color.blend_state.src_color_blendfactor=SDL_GPU_BLENDFACTOR_SRC_ALPHA; color.blend_state.dst_color_blendfactor=SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA; color.blend_state.color_blend_op=SDL_GPU_BLENDOP_ADD;
 color.blend_state.src_alpha_blendfactor=SDL_GPU_BLENDFACTOR_ONE; color.blend_state.dst_alpha_blendfactor=SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA; color.blend_state.alpha_blend_op=SDL_GPU_BLENDOP_ADD;
 SDL_GPUGraphicsPipelineCreateInfo pi{}; pi.vertex_shader=vs;pi.fragment_shader=fs;pi.vertex_input_state={&binding,1,attrs,3};pi.primitive_type=SDL_GPU_PRIMITIVETYPE_TRIANGLELIST;pi.target_info.color_target_descriptions=&color;pi.target_info.num_color_targets=1;
 auto*pipeline=SDL_CreateGPUGraphicsPipeline(b->device,&pi);if(!pipeline)error(e);return pipeline;
}
static SDL_GPUGraphicsPipeline* create_resolve_pipeline(Backend*b,std::string&e){
 struct Shaders {
  SDL_GPUDevice* device; SDL_GPUShader* vertex=nullptr; SDL_GPUShader* fragment=nullptr;
  ~Shaders(){if(fragment)SDL_ReleaseGPUShader(device,fragment);if(vertex)SDL_ReleaseGPUShader(device,vertex);}
 } shaders{b->device};
 SDL_GPUShaderCreateInfo si{};si.code=resolve_vert_spv;si.code_size=sizeof(resolve_vert_spv);si.entrypoint="main";si.format=SDL_GPU_SHADERFORMAT_SPIRV;si.stage=SDL_GPU_SHADERSTAGE_VERTEX;
 shaders.vertex=SDL_CreateGPUShader(b->device,&si);if(!shaders.vertex){error(e);return nullptr;}
 si.code=resolve_frag_spv;si.code_size=sizeof(resolve_frag_spv);si.stage=SDL_GPU_SHADERSTAGE_FRAGMENT;si.num_samplers=1;
 shaders.fragment=SDL_CreateGPUShader(b->device,&si);if(!shaders.fragment){error(e);return nullptr;}
 SDL_GPUColorTargetDescription color{};color.format=SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM;
 // The private attachment contains premultiplied RGB. This full-screen shader
 // publishes straight-alpha texels without blending with the old public image.
 SDL_GPUGraphicsPipelineCreateInfo pi{};pi.vertex_shader=shaders.vertex;pi.fragment_shader=shaders.fragment;pi.primitive_type=SDL_GPU_PRIMITIVETYPE_TRIANGLELIST;pi.target_info.color_target_descriptions=&color;pi.target_info.num_color_targets=1;
 auto*pipeline=SDL_CreateGPUGraphicsPipeline(b->device,&pi);if(!pipeline)error(e);return pipeline;
}
Backend* backend_create(gal_config& c,std::string& e) {
 const int runtime=SDL_GetVersion();
 if(runtime<SDL_VERSIONNUM(3,4,16)){e="SDL 3.4.16 or newer required; loaded "+std::to_string(SDL_VERSIONNUM_MAJOR(runtime))+"."+std::to_string(SDL_VERSIONNUM_MINOR(runtime))+"."+std::to_string(SDL_VERSIONNUM_MICRO(runtime));return nullptr;}
 std::unique_ptr<Backend,decltype(&backend_destroy)> ptr(new Backend,&backend_destroy); auto*b=ptr.get(); b->textures.reserve(256+GAL_TARGET_CAPACITY); b->targets.reserve(GAL_TARGET_CAPACITY); b->materials.reserve(GAL_MATERIAL_CAPACITY);
#ifdef GAL_ENABLE_RMLUI
 // Match upstream RmlUi's SDL GPU initialization: we render preedit, the OS renders candidates.
 // Preserve an explicit host/environment override; this is an application hint, not an OS setting.
 if(!SDL_GetHint(SDL_HINT_IME_IMPLEMENTED_UI))SDL_SetHint(SDL_HINT_IME_IMPLEMENTED_UI,"composition");
#endif
 const SDL_InitFlags flags=SDL_INIT_VIDEO | ((c.flags&GAL_AUDIO)?SDL_INIT_AUDIO:0);
 if(!SDL_InitSubSystem(flags)) { error(e); return nullptr; } b->init_flags=flags;
 b->window=SDL_CreateWindow("Game Authoring Lab | C# + SDL3 GPU",c.width,c.height,SDL_WINDOW_RESIZABLE);
 if(!b->window) {error(e);return nullptr;}
 // Vulkan is the verified shader format in this slice. D3D12 needs offline DXIL assets before enabling.
 b->device=SDL_CreateGPUDevice(SDL_GPU_SHADERFORMAT_SPIRV,true,"vulkan");
 if(!b->device || !SDL_ClaimWindowForGPUDevice(b->device,b->window)) {error(e);return nullptr;} b->claimed=true;
 b->pipeline=create_sprite_pipeline(b,sprite_frag_spv,sizeof(sprite_frag_spv),0,SDL_GetGPUSwapchainTextureFormat(b->device,b->window),e);if(!b->pipeline)return nullptr;
 SDL_GPUBufferCreateInfo bi{};bi.usage=SDL_GPU_BUFFERUSAGE_VERTEX;bi.size=c.max_sprites*6*sizeof(Vertex); b->vertices=SDL_CreateGPUBuffer(b->device,&bi);if(!b->vertices){error(e);return nullptr;}
 SDL_GPUTransferBufferCreateInfo ti{};ti.usage=SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD;ti.size=bi.size>4096?bi.size:4096;b->transfer=SDL_CreateGPUTransferBuffer(b->device,&ti);if(!b->transfer){error(e);return nullptr;}
 SDL_GPUTextureCreateInfo tex{};tex.type=SDL_GPU_TEXTURETYPE_2D;tex.format=SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM;tex.usage=SDL_GPU_TEXTUREUSAGE_SAMPLER;tex.width=32;tex.height=32;tex.layer_count_or_depth=1;tex.num_levels=1;b->texture=SDL_CreateGPUTexture(b->device,&tex);if(!b->texture){error(e);return nullptr;}
 SDL_GPUSamplerCreateInfo sam{};sam.min_filter=SDL_GPU_FILTER_LINEAR;sam.mag_filter=SDL_GPU_FILTER_LINEAR;sam.mipmap_mode=SDL_GPU_SAMPLERMIPMAPMODE_NEAREST;sam.address_mode_u=SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE;sam.address_mode_v=sam.address_mode_u;sam.address_mode_w=sam.address_mode_u;b->sampler=SDL_CreateGPUSampler(b->device,&sam);if(!b->sampler){error(e);return nullptr;}
 if(!b->vertices||!b->transfer||!b->texture||!b->sampler){error(e);return nullptr;}
 auto*p=static_cast<unsigned char*>(SDL_MapGPUTransferBuffer(b->device,b->transfer,false));if(!p){error(e);return nullptr;}
 for(int y=0;y<32;y++)for(int x=0;x<32;x++){int i=(y*32+x)*4;float dx=(x-15.5f)/15.5f,dy=(y-15.5f)/15.5f;float a=std::fmax(0.f,std::fmin(1.f,(1.f-std::sqrt(dx*dx+dy*dy))*12.f));p[i]=p[i+1]=p[i+2]=255;p[i+3]=static_cast<unsigned char>(a*255);}
 SDL_UnmapGPUTransferBuffer(b->device,b->transfer);
 auto*cmd=SDL_AcquireGPUCommandBuffer(b->device);if(!cmd){error(e);return nullptr;}
 auto*copy=SDL_BeginGPUCopyPass(cmd);SDL_GPUTextureTransferInfo source{};source.transfer_buffer=b->transfer;source.pixels_per_row=32;source.rows_per_layer=32;SDL_GPUTextureRegion dest{};dest.texture=b->texture;dest.w=32;dest.h=32;dest.d=1;SDL_UploadToGPUTexture(copy,&source,&dest,false);SDL_EndGPUCopyPass(copy);
 if(!SDL_SubmitGPUCommandBuffer(cmd)){error(e);return nullptr;}
 if(c.flags&GAL_AUDIO){SDL_AudioSpec spec{};spec.format=SDL_AUDIO_F32;spec.channels=1;spec.freq=48000;b->audio=SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK,&spec,nullptr,nullptr);if(!b->audio||!SDL_ResumeAudioStreamDevice(b->audio)){error(e);return nullptr;}
 b->tone.resize(9600);for(size_t i=0;i<b->tone.size();i++){float t=float(i)/48000.f;float envelope=std::fmin(1.f,t/0.005f)*std::fmin(1.f,(0.2f-t)/0.02f);b->tone[i]=0.15f*envelope*std::sin(6.283185307f*440.f*t);}}
 if(!SDL_GetWindowSizeInPixels(b->window,&c.width,&c.height)){error(e);return nullptr;}
 if(c.width<1||c.height<1||c.width>16384||c.height>16384){e="initial framebuffer size outside 1..16384";return nullptr;}
 b->projection_width=c.width;b->projection_height=c.height;
 if(const char*path=std::getenv("GAL_CAPTURE_BMP")) b->capture_path=path;
 return ptr.release();
}
const char* backend_name(Backend* b){return SDL_GetGPUDeviceDriver(b->device);}
bool backend_poll_v2(Backend*b,gal_input_v2& input,std::string&e){
 b->input.begin(input);SDL_GetMouseState(&input.mouse_x,&input.mouse_y);
 const auto window_id=SDL_GetWindowID(b->window);
 SDL_Event event;while(SDL_PollEvent(&event)){
  if(event.type==SDL_EVENT_QUIT){input.quit=1;continue;}
  SDL_WindowID id=0;
  if(event.type>=SDL_EVENT_WINDOW_FIRST&&event.type<=SDL_EVENT_WINDOW_LAST)id=event.window.windowID;
  else switch(event.type){
   case SDL_EVENT_KEY_DOWN:case SDL_EVENT_KEY_UP:id=event.key.windowID;break;
   case SDL_EVENT_MOUSE_BUTTON_DOWN:case SDL_EVENT_MOUSE_BUTTON_UP:id=event.button.windowID;break;
   case SDL_EVENT_MOUSE_MOTION:id=event.motion.windowID;break;
   case SDL_EVENT_MOUSE_WHEEL:id=event.wheel.windowID;break;
   case SDL_EVENT_TEXT_INPUT:id=event.text.windowID;break;
   case SDL_EVENT_TEXT_EDITING:id=event.edit.windowID;break;
   default:continue;
  }
  if(id!=window_id)continue;
  bool consumed=false;
#ifdef GAL_ENABLE_RMLUI
  if(b->ui)consumed=ui_input(b->ui,event);
#endif
  switch(event.type){
   case SDL_EVENT_WINDOW_CLOSE_REQUESTED:input.quit=1;break;
   case SDL_EVENT_WINDOW_MINIMIZED:b->minimized=true;break;
   case SDL_EVENT_WINDOW_RESTORED:case SDL_EVENT_WINDOW_MAXIMIZED:b->minimized=false;break;
   case SDL_EVENT_WINDOW_FOCUS_LOST:b->input.focus(input,false);break;
   case SDL_EVENT_WINDOW_FOCUS_GAINED:b->input.focus(input,true);break;
   case SDL_EVENT_KEY_DOWN:case SDL_EVENT_KEY_UP:{
    bool captured=consumed;
#ifdef GAL_ENABLE_RMLUI
    captured=captured||(b->ui&&ui_keyboard_focus(b->ui));
#endif
    b->input.key(input,event.key.scancode,event.type==SDL_EVENT_KEY_DOWN,event.key.repeat,captured);break;
   }
   case SDL_EVENT_MOUSE_BUTTON_DOWN:case SDL_EVENT_MOUSE_BUTTON_UP:
    input.mouse_x=event.button.x;input.mouse_y=event.button.y;b->input.button(input,event.button.button,event.type==SDL_EVENT_MOUSE_BUTTON_DOWN,consumed);break;
   case SDL_EVENT_MOUSE_MOTION:input.mouse_x=event.motion.x;input.mouse_y=event.motion.y;if(consumed)input.consumed|=GAL_CONSUMED_POINTER;break;
   case SDL_EVENT_MOUSE_WHEEL:{float direction=event.wheel.direction==SDL_MOUSEWHEEL_FLIPPED?-1.f:1.f;b->input.wheel(input,event.wheel.x*direction,event.wheel.y*direction,consumed);break;}
   case SDL_EVENT_TEXT_INPUT:case SDL_EVENT_TEXT_EDITING:if(consumed)input.consumed|=GAL_CONSUMED_TEXT;break;
  }
 }
#ifdef GAL_ENABLE_RMLUI
 if(b->ui&&ui_keyboard_focus(b->ui)){b->input.capture_keyboard(input);if(input.game_wheel_x!=0||input.game_wheel_y!=0)input.consumed|=GAL_CONSUMED_WHEEL;input.game_wheel_x=input.game_wheel_y=0;}
#endif
 int w=0,h=0,pw=0,ph=0;
 if(!SDL_GetWindowSize(b->window,&w,&h)||!SDL_GetWindowSizeInPixels(b->window,&pw,&ph))return error(e);
 InputState::viewport(input,w,h,pw,ph,b->minimized||(SDL_GetWindowFlags(b->window)&SDL_WINDOW_MINIMIZED)!=0);
 b->drawable=(input.flags&GAL_INPUT_DRAWABLE)!=0;if(b->drawable){b->projection_width=pw;b->projection_height=ph;}return true;
}
bool backend_poll(Backend*b,gal_input& input,std::string&e){
 gal_input_v2 snapshot{};snapshot.size=sizeof(snapshot);snapshot.version=GAL_INPUT_VERSION;
 if(!backend_poll_v2(b,snapshot,e))return false;
 auto key=[&](int code){return (snapshot.game_keys_down[code/64]&(uint64_t(1)<<(code%64)))!=0;};
 if(key(SDL_SCANCODE_LEFT)||key(SDL_SCANCODE_A))input.keys|=GAL_LEFT;
 if(key(SDL_SCANCODE_RIGHT)||key(SDL_SCANCODE_D))input.keys|=GAL_RIGHT;
 if(key(SDL_SCANCODE_UP)||key(SDL_SCANCODE_W))input.keys|=GAL_UP;
 if(key(SDL_SCANCODE_DOWN)||key(SDL_SCANCODE_S))input.keys|=GAL_DOWN;
 if(key(SDL_SCANCODE_SPACE))input.keys|=GAL_SPACE;
 if(snapshot.keys_down[SDL_SCANCODE_ESCAPE/64]&(uint64_t(1)<<(SDL_SCANCODE_ESCAPE%64)))input.keys|=GAL_ESCAPE;
 if(key(SDL_SCANCODE_E))input.keys|=GAL_INTERACT;
 if(key(SDL_SCANCODE_F))input.keys|=GAL_DROP;
 if(key(SDL_SCANCODE_T))input.keys|=GAL_TRANSITION;
 if(key(SDL_SCANCODE_F5))input.keys|=GAL_SAVE;
 if(key(SDL_SCANCODE_F9))input.keys|=GAL_LOAD;
 if(!(snapshot.flags&GAL_INPUT_FOCUSED))input.keys=GAL_FOCUS_LOST;
 input.quit=snapshot.quit;input.wheel=snapshot.game_wheel_y;input.mouse_x=snapshot.mouse_x;input.mouse_y=snapshot.mouse_y;
 input.width=std::max(1,snapshot.pixel_width);input.height=std::max(1,snapshot.pixel_height);return true;
}
bool backend_draw(Backend*b,const Vertex*data,uint32_t count,const DrawRun*runs,uint32_t run_count,uint32_t& drawn,std::string&e){
 const RenderPass pass{0,b->projection_width,b->projection_height,{0.035f,0.045f,0.08f,1},0,run_count};
 return backend_render_frame(b,data,count,runs,run_count,&pass,1,drawn,e);
}
bool backend_render_frame(Backend*b,const Vertex*data,uint32_t count,const DrawRun*runs,uint32_t run_count,const RenderPass*passes,uint32_t pass_count,uint32_t& drawn,std::string&e){
 drawn=0;
 if(!b->drawable||(SDL_GetWindowFlags(b->window)&SDL_WINDOW_MINIMIZED))return true;
 if(!pass_count||!passes||passes[pass_count-1].target){e="frame requires a final window pass";return false;}
 for(uint32_t i=0;i<run_count;i++)if(runs[i].material&&std::none_of(b->materials.begin(),b->materials.end(),[&](const auto&item){return item.id==runs[i].material;})){e="missing material pipeline";return false;}
 for(uint32_t i=0;i<pass_count;i++){
  const auto&part=passes[i];
  if(part.first_run>run_count||part.run_count>run_count-part.first_run){e="invalid render pass run range";return false;}
  if(!part.target){if(i+1!=pass_count){e="window pass must be last";return false;}continue;}
  const auto target=std::find_if(b->targets.begin(),b->targets.end(),[&](const auto&item){return item.id==part.target;});
  if(target==b->targets.end()||target->width!=part.width||target->height!=part.height){e="missing or mismatched render target";return false;}
 }
 auto*cmd=SDL_AcquireGPUCommandBuffer(b->device);if(!cmd)return error(e);
 SDL_GPUTexture*window=nullptr;Uint32 w=0,h=0;
 if(!SDL_WaitAndAcquireGPUSwapchainTexture(cmd,b->window,&window,&w,&h)){SDL_CancelGPUCommandBuffer(cmd);return error(e);}
 if(!window)return SDL_SubmitGPUCommandBuffer(cmd)||error(e);
 // Acquire the window before any pass: minimized/resize-skipped frames must not
 // change offscreen targets either. Projection is the snapshot established by poll.
 const auto&window_pass=passes[pass_count-1];
 if(int(w)!=b->projection_width||int(h)!=b->projection_height||int(w)!=window_pass.width||int(h)!=window_pass.height)return SDL_SubmitGPUCommandBuffer(cmd)||error(e);
 if(count){
  void*m=SDL_MapGPUTransferBuffer(b->device,b->transfer,true);
  if(!m){error(e);SDL_SubmitGPUCommandBuffer(cmd);return false;}
  std::memcpy(m,data,count*sizeof(Vertex));SDL_UnmapGPUTransferBuffer(b->device,b->transfer);
 }
 bool capture=!b->capture_path.empty()&&!b->captured;
 if(capture){
  // A prior capture failure may be retried; deferred releases are GPU-safe.
  if(b->capture_texture){SDL_ReleaseGPUTexture(b->device,b->capture_texture);b->capture_texture=nullptr;}
  if(b->readback){SDL_ReleaseGPUTransferBuffer(b->device,b->readback);b->readback=nullptr;}
  SDL_GPUTextureCreateInfo info{};info.type=SDL_GPU_TEXTURETYPE_2D;info.format=SDL_GetGPUSwapchainTextureFormat(b->device,b->window);info.usage=SDL_GPU_TEXTUREUSAGE_COLOR_TARGET|SDL_GPU_TEXTUREUSAGE_SAMPLER;info.width=w;info.height=h;info.layer_count_or_depth=1;info.num_levels=1;
  b->capture_texture=SDL_CreateGPUTexture(b->device,&info);
  if(!b->capture_texture){error(e);SDL_SubmitGPUCommandBuffer(cmd);return false;}
  SDL_GPUTransferBufferCreateInfo transfer{};transfer.usage=SDL_GPU_TRANSFERBUFFERUSAGE_DOWNLOAD;transfer.size=w*h*4;b->readback=SDL_CreateGPUTransferBuffer(b->device,&transfer);
  if(!b->readback){error(e);SDL_SubmitGPUCommandBuffer(cmd);return false;}
 }
 if(count){auto*copy=SDL_BeginGPUCopyPass(cmd);SDL_GPUTransferBufferLocation src{};src.transfer_buffer=b->transfer;SDL_GPUBufferRegion dst{};dst.buffer=b->vertices;dst.size=count*sizeof(Vertex);SDL_UploadToGPUBuffer(copy,&src,&dst,true);SDL_EndGPUCopyPass(copy);}
 uint32_t submitted_runs=0;
 for(uint32_t pass_index=0;pass_index<pass_count;pass_index++){
  const auto&part=passes[pass_index];
  const Backend::Target*target=nullptr;
  if(part.target)for(const auto&item:b->targets)if(item.id==part.target){target=&item;break;}
  SDL_GPUColorTargetInfo color{};color.texture=target?target->attachment:(capture?b->capture_texture:window);color.clear_color={part.clear[0],part.clear[1],part.clear[2],part.clear[3]};color.load_op=SDL_GPU_LOADOP_CLEAR;color.store_op=SDL_GPU_STOREOP_STORE;
  if(target){color.clear_color.r*=part.clear[3];color.clear_color.g*=part.clear[3];color.clear_color.b*=part.clear[3];}
  auto*pass=SDL_BeginGPURenderPass(cmd,&color,1,nullptr);
  if(count){
   SDL_GPUBufferBinding binding{};binding.buffer=b->vertices;SDL_BindGPUVertexBuffers(pass,0,&binding,1);
   for(uint32_t i=part.first_run;i<part.first_run+part.run_count;i++){
    if(!runs[i].count)continue;
    const auto scissor=intersect_clip(runs[i].clip,part.width,part.height);
    if(!scissor.width||!scissor.height)continue;
    // Set every run explicitly so old/new submissions never inherit a prior clip.
    const SDL_Rect rect{scissor.x,scissor.y,scissor.width,scissor.height};SDL_SetGPUScissor(pass,&rect);
    auto*pipeline=target?b->target_pipeline:b->pipeline;
    if(runs[i].material){for(const auto&item:b->materials)if(item.id==runs[i].material){pipeline=target?item.target:item.window;break;}}
    SDL_BindGPUGraphicsPipeline(pass,pipeline);
    if(runs[i].material)SDL_PushGPUFragmentUniformData(cmd,0,runs[i].parameters,sizeof(runs[i].parameters));
    SDL_GPUTexture*texture=b->texture;if(runs[i].texture){for(const auto&item:b->textures)if(item.first==runs[i].texture){texture=item.second;break;}}
    SDL_GPUTextureSamplerBinding sampler{texture,b->sampler};SDL_BindGPUFragmentSamplers(pass,0,&sampler,1);
    SDL_DrawGPUPrimitives(pass,runs[i].count,1,runs[i].first,0);++submitted_runs;
   }
  }
  SDL_EndGPURenderPass(pass);
  if(target){
   SDL_GPUColorTargetInfo resolved{};resolved.texture=target->sampled;resolved.load_op=SDL_GPU_LOADOP_DONT_CARE;resolved.store_op=SDL_GPU_STOREOP_STORE;
   auto*resolve=SDL_BeginGPURenderPass(cmd,&resolved,1,nullptr);
   SDL_BindGPUGraphicsPipeline(resolve,b->resolve_pipeline);
   const SDL_Rect rect{0,0,part.width,part.height};SDL_SetGPUScissor(resolve,&rect);
   SDL_GPUTextureSamplerBinding sampler{target->attachment,b->sampler};SDL_BindGPUFragmentSamplers(resolve,0,&sampler,1);
   SDL_DrawGPUPrimitives(resolve,3,1,0,0);++submitted_runs;
   SDL_EndGPURenderPass(resolve);
  }
#ifdef GAL_ENABLE_RMLUI
  else if(b->ui&&!ui_render(b->ui,cmd,color.texture,int(w),int(h),e)){SDL_SubmitGPUCommandBuffer(cmd);return false;}
#endif
 }
 if(capture){
  SDL_GPUBlitInfo blit{};blit.source.texture=b->capture_texture;blit.source.w=w;blit.source.h=h;blit.destination.texture=window;blit.destination.w=w;blit.destination.h=h;blit.load_op=SDL_GPU_LOADOP_DONT_CARE;blit.filter=SDL_GPU_FILTER_NEAREST;SDL_BlitGPUTexture(cmd,&blit);
  auto*copy=SDL_BeginGPUCopyPass(cmd);SDL_GPUTextureRegion source{};source.texture=b->capture_texture;source.w=w;source.h=h;source.d=1;SDL_GPUTextureTransferInfo destination{};destination.transfer_buffer=b->readback;destination.pixels_per_row=w;destination.rows_per_layer=h;SDL_DownloadFromGPUTexture(copy,&source,&destination);SDL_EndGPUCopyPass(copy);
  SDL_GPUFence*fence=SDL_SubmitGPUCommandBufferAndAcquireFence(cmd);if(!fence)return error(e);
  bool complete=SDL_WaitForGPUFences(b->device,true,&fence,1);SDL_ReleaseGPUFence(b->device,fence);if(!complete)return error(e);
  void*pixels=SDL_MapGPUTransferBuffer(b->device,b->readback,false);if(!pixels)return error(e);
  auto format=SDL_GetGPUSwapchainTextureFormat(b->device,b->window);
  if(format!=SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM&&format!=SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM){SDL_UnmapGPUTransferBuffer(b->device,b->readback);e="diagnostic capture requires RGBA8 or BGRA8 swapchain";return false;}
  SDL_PixelFormat pixel_format=format==SDL_GPU_TEXTUREFORMAT_B8G8R8A8_UNORM?SDL_PIXELFORMAT_BGRA32:SDL_PIXELFORMAT_RGBA32;
  SDL_Surface*surface=SDL_CreateSurfaceFrom(int(w),int(h),pixel_format,pixels,int(w*4));
  bool saved=surface&&SDL_SaveBMP(surface,b->capture_path.c_str());if(!saved)error(e);if(surface)SDL_DestroySurface(surface);SDL_UnmapGPUTransferBuffer(b->device,b->readback);if(!saved)return false;b->captured=true;
 }else if(!SDL_SubmitGPUCommandBuffer(cmd))return error(e);
 drawn=submitted_runs;return true;
}
bool backend_tone(Backend*b,std::string&e){if(!b->audio){e="audio disabled";return false;}int queued=SDL_GetAudioStreamQueued(b->audio);if(queued<0)return error(e);if(queued>48000*4){e="audio queue limit";return false;}return SDL_PutAudioStreamData(b->audio,b->tone.data(),int(b->tone.size()*sizeof(float)))||error(e);}

bool backend_texture_load(Backend*b,const char*path,uint64_t id,int32_t&width,int32_t&height,std::string&e){
 std::vector<unsigned char>bytes;ImageMetadata metadata;
 if(!image_read(path,bytes,metadata,e))return false;
 if(b->texture_rgba_bytes+metadata.RgbaBytes()>256u*1024u*1024u){e="world texture RGBA residency exceeds 256 MiB";return false;}
 auto surface=image_decode(bytes,metadata,e);if(!surface)return false;
 SDL_GPUTextureCreateInfo info{};info.type=SDL_GPU_TEXTURETYPE_2D;info.format=SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM;info.usage=SDL_GPU_TEXTUREUSAGE_SAMPLER;info.width=surface->w;info.height=surface->h;info.layer_count_or_depth=1;info.num_levels=1;
 SDL_GPUTexture*texture=SDL_CreateGPUTexture(b->device,&info);if(!texture)return error(e);
 // Register ownership before any error-string allocation can throw.
 b->textures.push_back({id,texture,metadata.RgbaBytes()});b->texture_rgba_bytes+=metadata.RgbaBytes();
 SDL_GPUTransferBufferCreateInfo ti{};ti.usage=SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD;ti.size=info.width*info.height*4;
 SDL_GPUTransferBuffer*transfer=SDL_CreateGPUTransferBuffer(b->device,&ti);if(!transfer){backend_texture_release(b,id);return error(e);}
 void*pixels=SDL_MapGPUTransferBuffer(b->device,transfer,false);if(!pixels){SDL_ReleaseGPUTransferBuffer(b->device,transfer);backend_texture_release(b,id);return error(e);}
 for(int y=0;y<surface->h;y++)std::memcpy(static_cast<char*>(pixels)+y*surface->w*4,static_cast<char*>(surface->pixels)+y*surface->pitch,surface->w*4);
 SDL_UnmapGPUTransferBuffer(b->device,transfer);
 auto*cmd=SDL_AcquireGPUCommandBuffer(b->device);if(!cmd){SDL_ReleaseGPUTransferBuffer(b->device,transfer);backend_texture_release(b,id);return error(e);}
 auto*copy=SDL_BeginGPUCopyPass(cmd);if(!copy){SDL_CancelGPUCommandBuffer(cmd);SDL_ReleaseGPUTransferBuffer(b->device,transfer);backend_texture_release(b,id);return error(e);}
 SDL_GPUTextureTransferInfo source{};source.transfer_buffer=transfer;source.pixels_per_row=info.width;source.rows_per_layer=info.height;SDL_GPUTextureRegion dest{};dest.texture=texture;dest.w=info.width;dest.h=info.height;dest.d=1;SDL_UploadToGPUTexture(copy,&source,&dest,false);SDL_EndGPUCopyPass(copy);
 bool submitted=SDL_SubmitGPUCommandBuffer(cmd);SDL_ReleaseGPUTransferBuffer(b->device,transfer);if(!submitted){backend_texture_release(b,id);return error(e);}width=surface->w;height=surface->h;return true;
}
void backend_texture_release(Backend*b,uint64_t id){if(!b)return;for(auto it=b->textures.begin();it!=b->textures.end();++it)if(it->first==id){SDL_ReleaseGPUTexture(b->device,it->second);b->texture_rgba_bytes-=it->rgba_bytes;b->textures.erase(it);return;}}

bool backend_target_create(Backend*b,uint64_t id,int32_t width,int32_t height,std::string&e){
 if(b->targets.size()>=GAL_TARGET_CAPACITY||b->textures.size()>=256+GAL_TARGET_CAPACITY){e="render target capacity exhausted";return false;}
 auto release_pipeline=[&](SDL_GPUGraphicsPipeline*pipeline){if(pipeline)SDL_ReleaseGPUGraphicsPipeline(b->device,pipeline);};
 std::unique_ptr<SDL_GPUGraphicsPipeline,decltype(release_pipeline)> target_pipeline(nullptr,release_pipeline),resolve_pipeline(nullptr,release_pipeline);
 // Sprite-only hosts never compile target shaders. First target creation stages
 // both pipelines together; any failure leaves the backend's prior state intact.
 if(!b->target_pipeline){
  target_pipeline.reset(create_sprite_pipeline(b,sprite_frag_spv,sizeof(sprite_frag_spv),0,SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,e));if(!target_pipeline)return false;
  resolve_pipeline.reset(create_resolve_pipeline(b,e));if(!resolve_pipeline)return false;
 }
 auto release=[&](SDL_GPUTexture*texture){if(texture)SDL_ReleaseGPUTexture(b->device,texture);};
 SDL_GPUTextureCreateInfo info{};info.type=SDL_GPU_TEXTURETYPE_2D;info.format=SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM;info.usage=SDL_GPU_TEXTUREUSAGE_COLOR_TARGET|SDL_GPU_TEXTUREUSAGE_SAMPLER;info.width=width;info.height=height;info.layer_count_or_depth=1;info.num_levels=1;
 std::unique_ptr<SDL_GPUTexture,decltype(release)> attachment(SDL_CreateGPUTexture(b->device,&info),release);if(!attachment)return error(e);
 std::unique_ptr<SDL_GPUTexture,decltype(release)> sampled(SDL_CreateGPUTexture(b->device,&info),release);if(!sampled)return error(e);
 auto*cmd=SDL_AcquireGPUCommandBuffer(b->device);if(!cmd)return error(e);
 // Sampling a target before its first authored pass is defined transparent.
 // Clear both images on the same queue before publishing either resource.
 for(auto*texture:{attachment.get(),sampled.get()}){
  SDL_GPUColorTargetInfo clear{};clear.texture=texture;clear.load_op=SDL_GPU_LOADOP_CLEAR;clear.store_op=SDL_GPU_STOREOP_STORE;
  auto*pass=SDL_BeginGPURenderPass(cmd,&clear,1,nullptr);SDL_EndGPURenderPass(pass);
 }
 if(!SDL_SubmitGPUCommandBuffer(cmd))return error(e);
 // Both vectors have their bounded capacity reserved when the backend is made.
 // The texture table owns the public sampled image; targets own private images.
 b->targets.push_back({id,attachment.get(),sampled.get(),width,height});
 b->textures.push_back({id,sampled.get()});attachment.release();sampled.release();
 if(target_pipeline){b->target_pipeline=target_pipeline.release();b->resolve_pipeline=resolve_pipeline.release();}
 return true;
}
void backend_target_release(Backend*b,uint64_t id){
 if(!b)return;
 for(auto it=b->targets.begin();it!=b->targets.end();++it)if(it->id==id){SDL_ReleaseGPUTexture(b->device,it->attachment);backend_texture_release(b,id);b->targets.erase(it);return;}
}

bool backend_material_create(Backend*b,const uint8_t*fragment,uint32_t bytes,uint64_t id,std::string&e){
 if(b->materials.size()>=GAL_MATERIAL_CAPACITY){e="material pipeline capacity exhausted";return false;}
 auto release=[&](SDL_GPUGraphicsPipeline*p){if(p)SDL_ReleaseGPUGraphicsPipeline(b->device,p);};
 std::unique_ptr<SDL_GPUGraphicsPipeline,decltype(release)> window(create_sprite_pipeline(b,fragment,bytes,1,SDL_GetGPUSwapchainTextureFormat(b->device,b->window),e),release);
 if(!window){e="window material pipeline: "+e;return false;}
 std::unique_ptr<SDL_GPUGraphicsPipeline,decltype(release)> target(create_sprite_pipeline(b,fragment,bytes,1,SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,e),release);
 if(!target){e="target material pipeline: "+e;return false;}
 b->materials.push_back({id,window.get(),target.get()});window.release();target.release();return true;
}
void backend_material_release(Backend*b,uint64_t id){
 if(!b)return;
 for(auto it=b->materials.begin();it!=b->materials.end();++it)if(it->id==id){SDL_ReleaseGPUGraphicsPipeline(b->device,it->window);SDL_ReleaseGPUGraphicsPipeline(b->device,it->target);b->materials.erase(it);return;}
}

#ifdef GAL_ENABLE_RMLUI
bool backend_ui(Backend*b,int op,const void*in,void*out,std::string&e){
 if(op==1||op==10||op==20||op==30){
  const auto*model=op==30?static_cast<const ModelUiOpen*>(in):nullptr;
  const auto*bound=op==20?static_cast<const BoundUiOpen*>(in):nullptr;
  const char*bound_paths[]={model?model->path:bound?bound->path:nullptr,model?model->font:bound?bound->font:nullptr};
  auto paths=(op==20||op==30)?bound_paths:static_cast<const char*const*>(in);if(!paths||!paths[0]||!paths[1]||!paths[0][0]||!paths[1][0]){e="UI paths required";return false;}
  if(op==30&&(!model||!model->stylesheet||!model->stylesheet[0]||!model->schema||!model->schema_count||model->schema_count>128||model->command_count>32||(model->command_count&&!model->commands))){e="generic schema/command pointers or counts invalid";return false;}
  const bool created=!b->ui;
  if(created){b->ui=ui_create(b->device,b->window,paths[1],e);if(!b->ui)return false;ui_window_state(b->ui,b->input.focused,!b->minimized);}
  if(op==20&&(!bound||!bound->targets||!bound->count||bound->count>32)){e="binding targets required (1..32)";if(created){ui_destroy(b->ui);b->ui=nullptr;}return false;}
  if(ui_load(b->ui,paths[0],e,op==10,bound?bound->targets:nullptr,bound?bound->count:0,model?model->images:bound?bound->images:nullptr,model?model->image_count:bound?bound->image_count:0,model?model->schema:nullptr,model?model->schema_count:0,model?model->commands:nullptr,model?model->command_count:0,model?model->stylesheet:nullptr))return true;
  if(created){ui_destroy(b->ui);b->ui=nullptr;}
  return false;
 }
 if(op==2){ui_destroy(b->ui);b->ui=nullptr;return true;}
 if(op==7){auto path=static_cast<const char*>(in);if(!path||!path[0]||std::strlen(path)>4096){e="invalid capture path";return false;}b->capture_path=path;b->captured=false;return true;}
 if(!b->ui){e="UI is not open";return false;}
 if(op==31){auto*r=static_cast<const ModelUiApply*>(in);if(!r||!r->snapshot){e="generic snapshot required";return false;}return ui_apply_model(b->ui,*r->snapshot,r->values,e);}
 if(op==32){auto*r=static_cast<gal_ui_event*>(out);if(!r){e="generic packet required";return false;}return ui_poll_model(b->ui,*r,e);}
 if(op==33){auto*r=static_cast<const ModelUiTest*>(in);if(!r||!r->packet){e="generic probe required";return false;}return ui_test_model(b->ui,r->command,r->id,r->occurrence,*r->packet,e);}
 if(op==21){auto*r=static_cast<const BoundUiApply*>(in);if(!r||!r->snapshot){e="binding snapshot required";return false;}return ui_apply_bound(b->ui,*r->snapshot,r->values,r->rows,e);}
 if(op==22){auto*a=static_cast<gal_bound_ui_action*>(out);if(!a){e="binding action required";return false;}return ui_poll_bound(b->ui,*a,e);}
 if(op==23){auto*r=static_cast<const BoundUiTest*>(in);if(!r||!r->value){e="binding probe required";return false;}return ui_test_bound(b->ui,r->command,*r->value,e);}
 if(op==11){auto*m=static_cast<const gal_game_ui_model*>(in);if(!m||m->size!=sizeof(*m)){e="invalid game UI model size";return false;}return ui_set_game_model(b->ui,*m,e);}
 if(op==12){auto*a=static_cast<gal_game_ui_action*>(out);if(!a||a->size!=sizeof(*a)){e="invalid game UI action size";return false;}return ui_poll_game_action(b->ui,*a,e);}
 if(op==13){auto args=static_cast<const uint32_t*>(in);return ui_game_test_command(b->ui,args[0],args[1],e);}
 if(op==3){auto*m=static_cast<const gal_ui_model*>(in);if(!m||m->size!=sizeof(*m)){e="invalid UI model size";return false;}return ui_set_model(b->ui,*m,e);}
 if(op==4){auto*a=static_cast<gal_ui_action*>(out);if(!a||a->size!=sizeof(*a)){e="invalid UI action size";return false;}return ui_poll_action(b->ui,*a,e);}
 if(op==5){auto*s=static_cast<gal_ui_state*>(out);if(!s||s->size!=sizeof(*s)){e="invalid UI state size";return false;}ui_state(b->ui,*s);return true;}
 if(op==8){auto*s=static_cast<gal_ui_text_state*>(out);if(!s||s->size!=sizeof(*s)||s->version!=1||s->reserved){e="invalid UI text state size/version/reserved";return false;}ui_text_state(b->ui,*s);return true;}
 if(op==6){auto args=static_cast<const uint32_t*>(in);return ui_test_command(b->ui,args[0],args[1],e);}
 e="unknown UI operation";return false;
}
#endif
