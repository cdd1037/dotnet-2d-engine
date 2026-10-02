#pragma once
#include "gal_ui_model.h"
#include <RmlUi/Core.h>
#include <array>
#include <memory>
#include <string>
#include <vector>

class UiModelDocument final: public Rml::EventListener {
 struct Node { gal_ui_data_value value{};std::vector<Node> children; };
 struct Definition final: Rml::VariableDefinition {
  UiModelDocument& owner;uint32_t schema;
  Definition(UiModelDocument&,uint32_t);
  bool Get(void*,Rml::Variant&)override;
  bool Set(void*,const Rml::Variant&)override{return false;}
  int Size(void*)override;
  Rml::DataVariable Child(void*,const Rml::DataAddressEntry&)override;
 };
 Rml::Context* context;Rml::ElementDocument* document=nullptr;Rml::DataModelHandle handle;
 std::vector<gal_ui_data_schema> schema;
 std::vector<std::vector<uint32_t>> members;
 std::vector<std::unique_ptr<Definition>> definitions;
 std::vector<gal_ui_command> commands;
 Node root,staged;std::vector<uint64_t> keys,staged_keys;
 std::array<gal_ui_event,64> events{};
 uint32_t generation=0,revision=0,pending_revision=0,first=0,count=0,overflow=0;
 bool registered=false,pressed=false,suppress_events=false,ready=false;
 char diagnostic[256]{};
 Node Default(uint32_t);
 bool ReadNode(uint32_t,const gal_ui_data_value*,uint32_t,uint32_t&,Node&,std::string&);
 void Event(uint32_t,Rml::Event&,const Rml::VariantList&);
 void Drop(const char*);
 public:
 explicit UiModelDocument(Rml::Context*c):context(c){}
 ~UiModelDocument();
 bool Configure(const gal_ui_data_schema*,uint32_t,const gal_ui_command*,uint32_t,std::string&);
 void Attach(Rml::ElementDocument*);
 void Publish(uint32_t);
 bool Stage(const gal_ui_data_snapshot&,const gal_ui_data_value*,std::string&);
 void Commit();
 void Updating(){ready=false;}
 void Updated(){ready=true;}
 bool Poll(gal_ui_event&,std::string&);
 bool Test(uint32_t,const char*,uint32_t,gal_ui_event&,std::string&);
 void ProcessEvent(Rml::Event&)override;
 uint32_t Count()const{return count;}
 uint32_t Overflow()const{return overflow;}
 const char* Diagnostic()const{return diagnostic;}
};
