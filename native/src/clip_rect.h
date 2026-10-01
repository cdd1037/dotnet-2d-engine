#pragma once
#include "gal.h"
#include <algorithm>

// Shared by the SDL backend and CPU contract tests. Never add signed int32
// endpoints before promoting them; public rectangles can span the full range.
struct ScissorRect { int32_t x,y,width,height; };
inline ScissorRect intersect_clip(const gal_clip_rect& clip,int32_t width,int32_t height) {
 if(width<=0||height<=0)return {};
 if(!(clip.flags&GAL_CLIP_ENABLED))return {0,0,width,height};
 const int64_t left=std::clamp<int64_t>(clip.x,0,width);
 const int64_t top=std::clamp<int64_t>(clip.y,0,height);
 const int64_t right=std::clamp<int64_t>(int64_t(clip.x)+clip.width,0,width);
 const int64_t bottom=std::clamp<int64_t>(int64_t(clip.y)+clip.height,0,height);
 return {int32_t(left),int32_t(top),int32_t(std::max<int64_t>(0,right-left)),int32_t(std::max<int64_t>(0,bottom-top))};
}
