#pragma once
#include <RmlUi/Core/FileInterface.h>
#include <RmlUi/Core/Log.h>
#include <algorithm>
#include <cstdio>
#include <filesystem>
#include <vector>

// One shared RmlUi file interface. Legacy validated profiles preserve their old
// I/O behavior; generic documents restrict RmlUi source reads to the two copied
// source files. Images still use UiImageRenderer's separate finite byte manifest.
class UiFileGate final : public Rml::FileInterface {
 std::vector<Rml::String> live, pending;
 bool live_restricted=false, pending_restricted=false, staging=false;
 static Rml::String Normalize(const Rml::String& path) {
  return std::filesystem::absolute(std::filesystem::path(path)).lexically_normal().string();
 }
public:
 void Stage(const char* document, const char* stylesheet) {
  pending.clear();staging=true;pending_restricted=stylesheet!=nullptr;
  if (stylesheet) {pending.push_back(Normalize(document));pending.push_back(Normalize(stylesheet));}
 }
 void Drop() {pending.clear();staging=false;pending_restricted=false;}
 void Publish() {live=std::move(pending);live_restricted=pending_restricted;staging=false;pending_restricted=false;}
 Rml::FileHandle Open(const Rml::String& path) override {
  const bool restricted=staging?pending_restricted:live_restricted;
  if(restricted) {
   const auto normalized=Normalize(path);
   if(std::find(live.begin(),live.end(),normalized)==live.end()&&std::find(pending.begin(),pending.end(),normalized)==pending.end()) {
    Rml::Log::Message(Rml::Log::LT_ERROR,"UI source read denied outside the copied document/stylesheet snapshot");return 0;
   }
  }
  return reinterpret_cast<Rml::FileHandle>(std::fopen(path.c_str(),"rb"));
 }
 void Close(Rml::FileHandle file) override {std::fclose(reinterpret_cast<FILE*>(file));}
 size_t Read(void* buffer,size_t size,Rml::FileHandle file) override {return std::fread(buffer,1,size,reinterpret_cast<FILE*>(file));}
 bool Seek(Rml::FileHandle file,long offset,int origin) override {return std::fseek(reinterpret_cast<FILE*>(file),offset,origin)==0;}
 size_t Tell(Rml::FileHandle file) override {return size_t(std::ftell(reinterpret_cast<FILE*>(file)));}
};
