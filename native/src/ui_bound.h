#pragma once
#include "gal_ui.h"
#include <RmlUi/Core.h>
#include <array>
#include <string>

// One immutable registration set and one copied snapshot per published document.
class UiBoundDocument final : public Rml::EventListener {
 Rml::ElementDocument* document;
 std::array<gal_bound_ui_target,32> targets{};
 std::array<Rml::Element*,32> elements{};
 std::array<gal_bound_ui_value,32> values{},staged_values{};
 std::array<gal_bound_ui_row,64> rows{},staged_rows{};
 std::array<Rml::Element*,64> row_elements{};
 std::array<Rml::ElementPtr,32> staged_nodes{};
 std::array<bool,32> changed{};
 std::array<gal_bound_ui_action,64> queue{};
 uint32_t target_count=0,row_count=0,staged_count=0,generation=0,revision=0,first=0,count=0,overflow=0;
 char diagnostic[256]{};
 bool attached=false,applying=false;
 Rml::Element* Find(uint32_t,uint64_t)const;
 gal_bound_ui_action Read(uint32_t,uint64_t,Rml::Element*)const;
 public:
 explicit UiBoundDocument(Rml::ElementDocument*d):document(d){}
 ~UiBoundDocument();
 bool Configure(const gal_bound_ui_target*,uint32_t,std::string&);
 void Publish(uint32_t);
 bool Stage(const gal_bound_ui_snapshot&,const gal_bound_ui_value*,const gal_bound_ui_row*,std::string&);
 bool ReplacesInput(Rml::Element*)const;
 void Commit();
 void ProcessEvent(Rml::Event&)override;
 bool Poll(gal_bound_ui_action&,std::string&);
 bool Test(uint32_t,gal_bound_ui_action&,Rml::Context*,std::string&);
 const char* Diagnostic()const{return diagnostic;}
 uint32_t Count()const{return count;}
 uint32_t Overflow()const{return overflow;}
};
