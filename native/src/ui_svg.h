#pragma once
#include <RmlUi/Core/Types.h>
#include <memory>
#include <cstdint>
#include <string>
#include <vector>
namespace Rml { class RenderManager; }
// Narrow host hooks used by the documented patch to the pinned official plugin.
// All sources come from copied, prevalidated manifests, never arbitrary file IO.
namespace gal_svg {
struct State;
class Resources {
 std::shared_ptr<State> state;
 public:
 Resources();
 ~Resources();
 Resources(const Resources&)=delete;
 Resources& operator=(const Resources&)=delete;
 bool Add(const Rml::String&,std::vector<unsigned char>,std::string&);
 void Attach(Rml::RenderManager&,uint64_t raster_bytes);
 bool Preload(std::string&);
 size_t SourceCount()const;
 size_t VariantCount()const;
 uint64_t RasterBytes()const;
};
bool LoadData(Rml::RenderManager&,const Rml::String&,Rml::String&);
Rml::String CacheKey(Rml::RenderManager&,const Rml::String&);
std::shared_ptr<void> Reserve(Rml::RenderManager&,const Rml::String&,Rml::Vector2i,bool);
}
