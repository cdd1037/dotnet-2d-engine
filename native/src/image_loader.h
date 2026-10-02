#pragma once
#include "image_metadata.h"
#include <SDL3/SDL.h>
#include <SDL3_image/SDL_image.h>
#include <memory>
#include <vector>

using ImageSurface=std::unique_ptr<SDL_Surface,decltype(&SDL_DestroySurface)>;
inline bool image_read(const char*path,std::vector<unsigned char>&bytes,ImageMetadata&metadata,std::string&e){
 std::unique_ptr<SDL_IOStream,decltype(&SDL_CloseIO)> input(SDL_IOFromFile(path,"rb"),SDL_CloseIO);
 if(!input){e=SDL_GetError();return false;}
 const auto size=SDL_GetIOSize(input.get());
 if(size<2||uint64_t(size)>image_max_encoded_bytes){e="image encoded size must be 2 bytes..16 MiB";return false;}
 bytes.resize(size_t(size));
 if(SDL_ReadIO(input.get(),bytes.data(),bytes.size())!=bytes.size()){e="could not read complete image snapshot";return false;}
 unsigned char extra=0;
 if(SDL_ReadIO(input.get(),&extra,1)!=0){e="image file grew while capturing snapshot";return false;}
 return image_metadata(bytes.data(),bytes.size(),metadata,e);
}
inline ImageSurface image_decode(const std::vector<unsigned char>&bytes,const ImageMetadata&metadata,std::string&e){
 std::unique_ptr<SDL_IOStream,decltype(&SDL_CloseIO)> input(SDL_IOFromConstMem(bytes.data(),bytes.size()),SDL_CloseIO);
 ImageSurface loaded(nullptr,SDL_DestroySurface);
 if(!input){e=SDL_GetError();return loaded;}
 // Call exactly the validated decoder; extension hints never enable other formats.
 if(metadata.kind==ImageKind::Bmp)loaded.reset(IMG_LoadBMP_IO(input.get()));
 else if(metadata.kind==ImageKind::Png)loaded.reset(IMG_LoadPNG_IO(input.get()));
 else loaded.reset(IMG_LoadJPG_IO(input.get()));
 if(!loaded){e=SDL_GetError();if(e.empty())e="image decoder rejected the validated snapshot";return loaded;}
 if(loaded->w!=int(metadata.width)||loaded->h!=int(metadata.height)){e="decoded image dimensions differ from validated header";return ImageSurface(nullptr,SDL_DestroySurface);}
 ImageSurface surface(SDL_ConvertSurface(loaded.get(),SDL_PIXELFORMAT_RGBA32),SDL_DestroySurface);
 if(!surface)e=SDL_GetError();
 return surface;
}
inline ImageSurface image_load(const char*path,std::string&e){
 std::vector<unsigned char>bytes;ImageMetadata metadata;
 if(!image_read(path,bytes,metadata,e))return ImageSurface(nullptr,SDL_DestroySurface);
 return image_decode(bytes,metadata,e);
}
