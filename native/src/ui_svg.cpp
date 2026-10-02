#include "ui_svg.h"
#include "svg_validation.h"
#include <RmlUi/Core.h>
#include <RmlUi/Core/RenderManager.h>
#include <cstdint>
#include <limits>
#include <unordered_map>
namespace Rml { namespace SVG { bool GalPreload(RenderManager&,const String&); } }
namespace gal_svg {
struct State : std::enable_shared_from_this<State> {
 struct Source { Rml::String path;std::vector<unsigned char> bytes; };
 std::vector<Source> sources;
 Rml::RenderManager* manager=nullptr;
 uint64_t identity=0,raster_bytes=0,svg_bytes=0;
 size_t variants=0;
};
static std::unordered_map<Rml::RenderManager*,State*> registry;
static uint64_t next_identity=1;
static bool Failure(const char*message){Rml::Log::Message(Rml::Log::LT_ERROR,"UI SVG: %s",message);return false;}
static State* Find(Rml::RenderManager&manager){const auto i=registry.find(&manager);return i==registry.end()?nullptr:i->second;}
static const State::Source* FindSource(const State*state,const Rml::String&source){if(state)for(const auto&s:state->sources)if(s.path==source)return &s;return nullptr;}
Resources::Resources():state(std::make_shared<State>()){if(next_identity<std::numeric_limits<uint64_t>::max())state->identity=next_identity++;}
Resources::~Resources(){if(state->manager)registry.erase(state->manager);}
bool Resources::Add(const Rml::String&source,std::vector<unsigned char>bytes,std::string&e){
 SvgMetadata metadata;
 if(!svg_validate(bytes,metadata,e))return false;
 if(!state->identity){e="UI SVG document identity exhausted";return false;}
 if(FindSource(state.get(),source)){e="duplicate SVG manifest source";return false;}
 state->sources.push_back({source,std::move(bytes)});return true;
}
void Resources::Attach(Rml::RenderManager&manager,uint64_t raster_bytes){state->manager=&manager;state->raster_bytes=raster_bytes;registry[&manager]=state.get();}
bool Resources::Preload(std::string&e){
 for(const auto&source:state->sources)if(!Rml::SVG::GalPreload(*state->manager,source.path)){e="SVG preload failed: "+source.path;return false;}
 return true;
}
size_t Resources::SourceCount()const{return state->sources.size();}
size_t Resources::VariantCount()const{return state->variants;}
uint64_t Resources::RasterBytes()const{return state->svg_bytes;}
bool LoadData(Rml::RenderManager&manager,const Rml::String&source,Rml::String&data){
 const auto*item=FindSource(Find(manager),source);
 if(!item)return Failure("source is not in the validated document manifest; inline SVG is unsupported");
 data.assign(reinterpret_cast<const char*>(item->bytes.data()),item->bytes.size());return true;
}
Rml::String CacheKey(Rml::RenderManager&manager,const Rml::String&source){
 const auto*state=Find(manager);
 if(!FindSource(state,source)){Failure("source is not in the validated document manifest");return {};}
 return std::to_string(state->identity)+":"+source;
}
std::shared_ptr<void> Reserve(Rml::RenderManager&manager,const Rml::String&source,Rml::Vector2i dimensions,bool crop){
 auto*state=Find(manager);
 if(!FindSource(state,source)){Failure("unregistered raster source");return {};}
 if(crop||dimensions.x<0||dimensions.y<0||dimensions.x>4096||dimensions.y>4096){Failure("raster dimensions exceed 4096 or unsupported crop");return {};}
 const uint64_t bytes=uint64_t(dimensions.x)*uint64_t(dimensions.y)*4;
 if(!bytes)return std::shared_ptr<void>(state,[](void*){});
 constexpr uint64_t budget=64u*1024u*1024u;
 if(state->variants>=128||bytes>budget-state->raster_bytes-state->svg_bytes){Failure("retained raster variants exceed 128 or document RGBA exceeds 64 MiB");return {};}
 state->variants++;state->svg_bytes+=bytes;
 // SVG cache entries are released before their owner renderer. The token releases
 // accounting together with that entry, including raster/upload failure paths.
 return std::shared_ptr<void>(state,[bytes,owner=state->shared_from_this()](void*){owner->variants--;owner->svg_bytes-=bytes;});
}
}
