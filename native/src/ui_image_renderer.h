#pragma once
#include "image_loader.h"
#include "RmlUi_Renderer_SDL_GPU.h"
#include <RmlUi/Core.h>
#include <RmlUi/Core/RenderManager.h>
#include <RmlUi/Core/Texture.h>
#include <algorithm>
#ifdef GAL_ENABLE_SVG
#include "ui_svg.h"
#endif

// File images have explicit per-document ownership, independent of world textures.
// Each document receives its own interface and render manager. Context teardown
// is followed by ReleaseRenderManagers, releasing both GPU images and cache keys.
class UiImageRenderer final: public RenderInterface_SDL_GPU {
 struct Image { Rml::String source,relative;std::vector<unsigned char> bytes;ImageMetadata metadata; };
 SDL_GPUDevice* device;
 std::vector<Image> images;
 uint64_t raster_bytes=0;
#ifdef GAL_ENABLE_SVG
 gal_svg::Resources svg;
#endif
 static bool SvgPath(const Rml::String&p){return p.size()>=4&&p[p.size()-4]=='.'&&(p[p.size()-3]=='s'||p[p.size()-3]=='S')&&(p[p.size()-2]=='v'||p[p.size()-2]=='V')&&(p.back()=='g'||p.back()=='G');}
 Rml::String document_path,last_failure;
 std::vector<Rml::TextureHandle> file_textures;
 static const Image* Find(const std::vector<Image>&images,const Rml::String&source){
  for(const auto&image:images)if(image.source==source)return &image;
  return nullptr;
 }
 static bool RelativePath(const char*path){
  if(!path||!*path)return false;
  size_t total=0,segment=0;
  for(const unsigned char*p=reinterpret_cast<const unsigned char*>(path);;p++){
   const unsigned char c=*p;
   if(!c||c=='/'){
    if(!segment||p[-1]=='.')return false;
    if(!c)return true;
    segment=0;
   }else{
    if(!((c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9')||c=='_'||c=='-'||c=='.'))return false;
    segment++;
   }
   if(++total>255)return false;
  }
 }
 Rml::TextureHandle Failure(const char*operation,const char*detail){
  last_failure=Rml::String("UI image ")+operation+": "+detail;
  Rml::Log::Message(Rml::Log::LT_ERROR,"%s",last_failure.c_str());return 0;
 }
 public:
 UiImageRenderer(SDL_GPUDevice*d,SDL_Window*w):RenderInterface_SDL_GPU(d,w),device(d){file_textures.reserve(32);}
 bool StageImages(const char*document,const char*const*paths,uint32_t count,std::string&e){
  if(count>32||(count&&!paths)){e="UI image manifest requires 0..32 paths";return false;}
  document_path=document;images.reserve(count);uint64_t encoded=0,decoded=0;
  for(uint32_t i=0;i<count;i++){
   if(!RelativePath(paths[i])){e="UI image manifest requires safe relative ASCII paths";return false;}
   Image image;image.relative=paths[i];Rml::GetSystemInterface()->JoinPath(image.source,document_path,image.relative);
   if(Find(images,image.source)){e="duplicate UI image manifest source";return false;}
   if(SvgPath(image.source)){
#ifdef GAL_ENABLE_SVG
    std::unique_ptr<SDL_IOStream,decltype(&SDL_CloseIO)> input(SDL_IOFromFile(image.source.c_str(),"rb"),SDL_CloseIO);
    if(!input){e=SDL_GetError();return false;}
    const auto size=SDL_GetIOSize(input.get());
    if(size<1||size>256*1024){e="SVG encoded size must be 1 byte..256 KiB";return false;}
    image.bytes.resize(size_t(size));
    if(SDL_ReadIO(input.get(),image.bytes.data(),image.bytes.size())!=image.bytes.size()){e="could not read complete SVG snapshot";return false;}
    unsigned char extra=0;if(SDL_ReadIO(input.get(),&extra,1)){e="SVG changed while capturing snapshot";return false;}
    encoded+=image.bytes.size();
    if(encoded>image_max_encoded_bytes){e="UI image manifest exceeds 16 MiB encoded total";return false;}
    if(!svg.Add(image.source,std::move(image.bytes),e))return false;
    continue;
#else
    e="SVG support is disabled; rebuild with GAL_ENABLE_SVG=ON";return false;
#endif
   }
   if(!image_read(image.source.c_str(),image.bytes,image.metadata,e))return false;
   encoded+=image.bytes.size();decoded+=image.metadata.RgbaBytes();
   if(encoded>image_max_encoded_bytes||decoded>image_max_rgba_bytes){e="UI image manifest exceeds 16 MiB encoded or 64 MiB RGBA total";return false;}
   images.push_back(std::move(image));
  }
  raster_bytes=decoded;return true;
 }
 bool Preload(Rml::Context*context,std::string&e){
#ifdef GAL_ENABLE_SVG
  svg.Attach(context->GetRenderManager(),raster_bytes);
  if(!svg.Preload(e))return false;
#endif
  for(const auto&image:images){
   // Use the same relative source/document pair as RML/RCSS. Upstream
   // JoinPath strips the leading slash if an already-absolute source is reused.
   last_failure.clear();
   const auto dimensions=context->GetRenderManager().LoadTexture(image.relative,document_path).GetDimensions();
   if(dimensions.x!=int(image.metadata.width)||dimensions.y!=int(image.metadata.height)){e="UI image preload failed: "+image.source;if(!last_failure.empty())e+=" ("+last_failure+")";return false;}
  }
  return true;
 }
 Rml::TextureHandle LoadTexture(Rml::Vector2i&dimensions,const Rml::String&source)override{
  dimensions={};const auto*image=Find(images,source);
  if(!image)return Failure("load","source is not in the validated document manifest");
  std::string error;auto surface=image_decode(image->bytes,image->metadata,error);
  if(!surface)return Failure("decode",error.c_str());
  const size_t row_bytes=size_t(surface->w)*4;
  std::vector<Rml::byte>pixels(row_bytes*surface->h);
  for(int y=0;y<surface->h;y++){
   auto*dst=pixels.data()+size_t(y)*row_bytes;
   std::memcpy(dst,static_cast<const unsigned char*>(surface->pixels)+size_t(y)*surface->pitch,row_bytes);
   // Rml uses premultiplied pixels; the world renderer keeps straight alpha.
   for(size_t x=0;x<row_bytes;x+=4)for(size_t c=0;c<3;c++)dst[x+c]=Rml::byte(unsigned(dst[x+c])*unsigned(dst[x+3])/255);
  }
  const Rml::Vector2i size{surface->w,surface->h};
  const auto handle=GenerateTexture({pixels.data(),pixels.size()},size);
  if(handle){dimensions=size;file_textures.push_back(handle);}
  return handle;
 }
 size_t FileTextureCount()const{return file_textures.size();}
 size_t SvgSourceCount()const{
#ifdef GAL_ENABLE_SVG
  return svg.SourceCount();
#else
  return 0;
#endif
 }
 size_t SvgVariantCount()const{
#ifdef GAL_ENABLE_SVG
  return svg.VariantCount();
#else
  return 0;
#endif
 }
 uint64_t SvgRasterBytes()const{
#ifdef GAL_ENABLE_SVG
  return svg.RasterBytes();
#else
  return 0;
#endif
 }
 void ReleaseTexture(Rml::TextureHandle texture)override{
  const auto found=std::find(file_textures.begin(),file_textures.end(),texture);
  if(found!=file_textures.end()){
   // File images are released only after their last document context is removed.
   // There are no unsubmitted Rml draw commands referencing them at that point;
   // SDL retains any already-submitted GPU use. Avoid accumulating deferred
   // releases when several rejected candidates occur without another frame.
   SDL_ReleaseGPUTexture(device,reinterpret_cast<SDL_GPUTexture*>(texture));
   file_textures.erase(found);
  }else RenderInterface_SDL_GPU::ReleaseTexture(texture);
 }
 Rml::TextureHandle GenerateTexture(Rml::Span<const Rml::byte>source,Rml::Vector2i dimensions)override{
  // Includes Rml's generated font atlases. The image manifest counts and budgets
  // apply only to LoadTexture, not these generated resources.
  if(dimensions.x<1||dimensions.y<1||dimensions.x>int(image_max_dimension)||dimensions.y>int(image_max_dimension))return Failure("upload","invalid generated texture dimensions");
  const uint64_t size=uint64_t(dimensions.x)*uint64_t(dimensions.y)*4;
  if(size>image_max_rgba_bytes||size>source.size()||!source.data())return Failure("upload","invalid generated pixel buffer");
  auto release_transfer=[&](SDL_GPUTransferBuffer*p){if(p)SDL_ReleaseGPUTransferBuffer(device,p);};
  SDL_GPUTransferBufferCreateInfo transfer_info{};transfer_info.usage=SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD;transfer_info.size=uint32_t(size);
  std::unique_ptr<SDL_GPUTransferBuffer,decltype(release_transfer)>transfer(SDL_CreateGPUTransferBuffer(device,&transfer_info),release_transfer);
  if(!transfer)return Failure("create transfer",SDL_GetError());
  void*pixels=SDL_MapGPUTransferBuffer(device,transfer.get(),false);if(!pixels)return Failure("map transfer",SDL_GetError());
  std::memcpy(pixels,source.data(),size_t(size));SDL_UnmapGPUTransferBuffer(device,transfer.get());
  auto release_texture=[&](SDL_GPUTexture*p){if(p)SDL_ReleaseGPUTexture(device,p);};
  SDL_GPUTextureCreateInfo info{};info.type=SDL_GPU_TEXTURETYPE_2D;info.usage=SDL_GPU_TEXTUREUSAGE_SAMPLER;info.format=SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM;info.width=dimensions.x;info.height=dimensions.y;info.layer_count_or_depth=1;info.num_levels=1;
  std::unique_ptr<SDL_GPUTexture,decltype(release_texture)>texture(SDL_CreateGPUTexture(device,&info),release_texture);
  if(!texture)return Failure("create texture",SDL_GetError());
  SDL_GPUCommandBuffer*command=SDL_AcquireGPUCommandBuffer(device);if(!command)return Failure("acquire command",SDL_GetError());
  SDL_GPUCopyPass*copy=SDL_BeginGPUCopyPass(command);
  if(!copy){SDL_CancelGPUCommandBuffer(command);return Failure("begin copy",SDL_GetError());}
  SDL_GPUTextureTransferInfo from{};from.transfer_buffer=transfer.get();from.pixels_per_row=dimensions.x;from.rows_per_layer=dimensions.y;
  SDL_GPUTextureRegion to{};to.texture=texture.get();to.w=dimensions.x;to.h=dimensions.y;to.d=1;
  SDL_UploadToGPUTexture(copy,&from,&to,false);SDL_EndGPUCopyPass(copy);
  if(!SDL_SubmitGPUCommandBuffer(command))return Failure("submit upload",SDL_GetError());
  return reinterpret_cast<Rml::TextureHandle>(texture.release());
 }
};
