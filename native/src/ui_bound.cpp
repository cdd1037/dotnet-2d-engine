#include "ui_bound.h"
#include "ui_rml.h"
#include <RmlUi/Core/Elements/ElementFormControlInput.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <limits>

static bool bad(std::string&e,const char*why){e=why;return false;}
static bool valid_id(const char*id){
 size_t n=0;while(n<48&&id[n])n++;if(!n||n==48||id[0]<'a'||id[0]>'z')return false;
 for(size_t i=1;i<n;i++)if(!((id[i]>='a'&&id[i]<='z')||(id[i]>='0'&&id[i]<='9')||id[i]=='-'))return false;
 return true;
}
static void disable_button(Rml::Element* element,bool disabled){
 element->SetPseudoClass("disabled",disabled);
 if(disabled){element->SetAttribute("disabled","");element->SetProperty(Rml::PropertyId::Focus,Rml::Property(Rml::Style::Focus::None));element->Blur();}
 else{element->RemoveAttribute("disabled");element->RemoveProperty(Rml::PropertyId::Focus);}
}
UiBoundDocument::~UiBoundDocument(){if(attached){document->RemoveEventListener("click",this);document->RemoveEventListener("change",this);}}
bool UiBoundDocument::Configure(const gal_bound_ui_target*source,uint32_t n,std::string&e){
 if(!source||!n||n>targets.size())return bad(e,"binding targets must contain 1..32 entries");
 for(uint32_t i=0;i<n;i++){
  const auto&t=source[i];if(t.size!=sizeof(t)||t.reserved||t.kind<1||t.kind>6||!valid_id(t.id))return bad(e,"invalid binding target size/kind/ID");
  for(uint32_t j=0;j<i;j++)if(!std::strcmp(t.id,source[j].id))return bad(e,"duplicate binding target ID");
  if((t.kind==GAL_BOUND_TEXT&&t.action)||(t.kind>=GAL_BOUND_ACTION&&!t.action))return bad(e,"binding action ID required for action/list and forbidden for text");
 }
 for(uint32_t i=0;i<n;i++){
  const auto&t=source[i];
  auto*element=document->GetElementById(t.id);if(!element)return bad(e,"registered binding target missing from document");
  const auto&tag=element->GetTagName();bool valid=false;
  if(t.kind==GAL_BOUND_TEXT)valid=tag=="p"||tag=="h1"||tag=="h2"||tag=="div";
  else if(t.kind==GAL_BOUND_ACTION)valid=tag=="button";
  else if(t.kind==GAL_BOUND_LIST)valid=tag=="div"&&element->GetNumChildren()==0;
  else if(auto*input=dynamic_cast<Rml::ElementFormControlInput*>(element))valid=input->GetAttribute<Rml::String>("type","")==(t.kind==GAL_BOUND_TEXT_INPUT?"text":t.kind==GAL_BOUND_BOOLEAN?"checkbox":"range");
  if(!valid)return bad(e,"binding kind and target element type disagree");
  if(t.kind==GAL_BOUND_TEXT_INPUT){int maximum=element->GetAttribute<int>("maxlength",0);if(maximum<1||maximum>64)return bad(e,"text input maxlength must be 1..64");}
  if(t.kind==GAL_BOUND_NUMBER&&(element->GetAttribute<Rml::String>("min","")!="0"||element->GetAttribute<Rml::String>("max","")!="100"||element->GetAttribute<Rml::String>("step","")!="1"))return bad(e,"number input range must be 0..100 step 1");
  // Replacing a target's children must not invalidate another registered target.
  for(uint32_t j=0;j<n;j++)if(i!=j){auto*other=document->GetElementById(source[j].id);for(auto*p=other?other->GetParentNode():nullptr;p;p=p->GetParentNode())if(p==element)return bad(e,"binding targets cannot nest");}
  targets[i]=t;elements[i]=element;
 }
 target_count=n;return true;
}
void UiBoundDocument::Publish(uint32_t g){generation=g;document->AddEventListener("click",this);document->AddEventListener("change",this);attached=true;}
bool UiBoundDocument::Stage(const gal_bound_ui_snapshot&s,const gal_bound_ui_value*input,const gal_bound_ui_row*input_rows,std::string&e){
 for(auto&node:staged_nodes)node.reset();
 if(s.size!=sizeof(s)||s.version!=1||s.generation!=generation||revision==std::numeric_limits<uint32_t>::max()||s.revision!=revision+1||s.value_count!=target_count||s.row_count>rows.size()||!input||(s.row_count&&!input_rows))return bad(e,"invalid binding snapshot size/version/generation/revision/count");
 uint32_t cursor=0;
 for(uint32_t i=0;i<target_count;i++){
  const auto&v=input[i];const auto&t=targets[i];
  if(v.size!=sizeof(v)||v.target!=i||v.reserved||v.flags>3||!std::isfinite(v.number)||!ui_valid_utf8(v.text,sizeof(v.text),t.kind==GAL_BOUND_TEXT_INPUT?static_cast<size_t>(elements[i]->GetAttribute<int>("maxlength",64)):255))return bad(e,"invalid binding value fields or UTF8");
  if((t.kind!=GAL_BOUND_BOOLEAN&&(v.flags&2))||(t.kind!=GAL_BOUND_NUMBER&&v.number!=0)||(t.kind!=GAL_BOUND_TEXT&&t.kind!=GAL_BOUND_TEXT_INPUT&&v.text[0]))return bad(e,"binding value has payload for a different kind");
  if(t.kind==GAL_BOUND_NUMBER&&(v.number<0||v.number>100||std::floor(v.number)!=v.number))return bad(e,"number binding requires integer 0..100");
  if(t.kind==GAL_BOUND_LIST){
   if(v.row_first!=cursor||v.row_count>s.row_count-cursor)return bad(e,"binding rows must form a complete ordered partition");
   for(uint32_t r=cursor;r<cursor+v.row_count;r++){
    const auto&row=input_rows[r];if(!row.id||row.reserved||row.flags>3||!ui_valid_utf8(row.text,sizeof(row.text),255))return bad(e,"invalid binding row ID/flags/UTF8");
    for(uint32_t prior=cursor;prior<r;prior++)if(row.id==input_rows[prior].id)return bad(e,"duplicate row ID within list");
   }
   cursor+=v.row_count;
  }else if(v.row_first||v.row_count)return bad(e,"only list bindings accept rows");
 }
 if(cursor!=s.row_count)return bad(e,"unreferenced rows in binding snapshot");
 // Complete validation and detached node allocation before mutating the live document.
 for(uint32_t i=0;i<target_count;i++){
  const auto&v=input[i];const auto&old=values[i];
  changed[i]=!revision||v.flags!=old.flags||v.number!=old.number||std::strcmp(v.text,old.text)||v.row_count!=old.row_count;
  if(targets[i].kind==GAL_BOUND_LIST&&!changed[i])for(uint32_t r=0;r<v.row_count;r++){
   const auto&a=input_rows[v.row_first+r];const auto&b=rows[old.row_first+r];
   if(a.id!=b.id||a.flags!=b.flags||std::strcmp(a.text,b.text)){changed[i]=true;break;}
  }
  if(changed[i]&&(targets[i].kind==GAL_BOUND_TEXT||targets[i].kind==GAL_BOUND_LIST)){
   if(targets[i].kind==GAL_BOUND_TEXT)staged_nodes[i]=document->CreateTextNode(v.text);
   else{
    auto container=document->CreateElement("div");container->SetClass("bound-items",true);
    for(uint32_t r=v.row_first;r<v.row_first+v.row_count;r++){
     auto element=document->CreateElement("button");element->SetClass("bound-row",true);element->SetPseudoClass("selected",(input_rows[r].flags&2)!=0);
     disable_button(element.get(),!(v.flags&1)||!(input_rows[r].flags&1));
     element->AppendChild(document->CreateTextNode(input_rows[r].text));container->AppendChild(std::move(element));
    }
    staged_nodes[i]=std::move(container);
   }
   if(!staged_nodes[i])return bad(e,"could not allocate binding nodes");
  }
  staged_values[i]=v;
 }
 for(uint32_t r=0;r<s.row_count;r++)staged_rows[r]=input_rows[r];
 staged_count=s.row_count;return true;
}
bool UiBoundDocument::ReplacesInput(Rml::Element* focused)const{for(uint32_t i=0;i<target_count;i++)if(elements[i]==focused&&changed[i]&&targets[i].kind==GAL_BOUND_TEXT_INPUT)return true;return false;}
void UiBoundDocument::Commit(){
 struct ResetFlag{bool& flag;~ResetFlag(){flag=false;}} guard{applying};applying=true;
 for(uint32_t i=0;i<target_count;i++)if(changed[i]){
  auto*element=elements[i];const auto&v=staged_values[i];const auto kind=targets[i].kind;
  if(kind==GAL_BOUND_TEXT||kind==GAL_BOUND_LIST){
   while(element->GetNumChildren()>0)element->RemoveChild(element->GetChild(0));
   element->AppendChild(std::move(staged_nodes[i]));
  }else{
   if(auto*input=dynamic_cast<Rml::ElementFormControlInput*>(element)){
    input->SetDisabled(!(v.flags&1));
    if(kind==GAL_BOUND_TEXT_INPUT)input->SetValue(v.text);
    else if(kind==GAL_BOUND_NUMBER)input->SetValue(std::to_string(static_cast<int>(v.number)));
    else if(v.flags&2)input->SetAttribute("checked","");else input->RemoveAttribute("checked");
   }else disable_button(element,!(v.flags&1));
  }
 }
 values=staged_values;rows=staged_rows;row_count=staged_count;row_elements.fill(nullptr);
 for(uint32_t i=0;i<target_count;i++)if(targets[i].kind==GAL_BOUND_LIST){auto*container=elements[i]->GetChild(0);for(uint32_t r=0;r<values[i].row_count;r++)row_elements[values[i].row_first+r]=container->GetChild(static_cast<int>(r));}
 revision++;first=count=0;diagnostic[0]=0;applying=false;
}
Rml::Element* UiBoundDocument::Find(uint32_t target,uint64_t row)const{
 if(target>=target_count||!revision)return nullptr;
 if(targets[target].kind!=GAL_BOUND_LIST)return row?nullptr:elements[target];
 const auto&v=values[target];for(uint32_t r=v.row_first;r<v.row_first+v.row_count;r++)if(rows[r].id==row)return row_elements[r];return nullptr;
}
gal_bound_ui_action UiBoundDocument::Read(uint32_t i,uint64_t row,Rml::Element*element)const{
 gal_bound_ui_action a{};a.size=sizeof(a);a.generation=generation;a.revision=revision;a.target=i;a.action=targets[i].action;a.kind=targets[i].kind;a.row=row;
 a.flags=values[i].flags;
 if(row){const auto&v=values[i];for(uint32_t r=v.row_first;r<v.row_first+v.row_count;r++)if(rows[r].id==row){a.flags=rows[r].flags;std::snprintf(a.text,sizeof(a.text),"%s",rows[r].text);}}
 else if(auto*input=dynamic_cast<Rml::ElementFormControlInput*>(element)){
  if(a.kind==GAL_BOUND_BOOLEAN)a.flags=(values[i].flags&1)|(input->HasAttribute("checked")?2u:0u);
  else if(a.kind==GAL_BOUND_NUMBER)a.number=std::strtod(input->GetValue().c_str(),nullptr);
  else{auto text=input->GetValue();if(!ui_valid_utf8(text.c_str(),256,64)){a.action=0;return a;}std::snprintf(a.text,sizeof(a.text),"%s",text.c_str());}
 }else if(a.kind==GAL_BOUND_TEXT)std::snprintf(a.text,sizeof(a.text),"%s",values[i].text);
 return a;
}
void UiBoundDocument::ProcessEvent(Rml::Event&event){
 if(applying||!revision||!event.GetTargetElement()||event.GetTargetElement()->GetOwnerDocument()!=document)return;
 auto*element=event.GetTargetElement();
 for(uint32_t i=0;i<target_count;i++){
  if(!targets[i].action||!(values[i].flags&1))continue;
  const auto kind=targets[i].kind;const bool click=kind==GAL_BOUND_ACTION||kind==GAL_BOUND_LIST;
  if(event.GetType()!=(click?"click":"change"))continue;
  uint64_t row=0;Rml::Element*matched=nullptr;
  for(auto*p=element;p&&p!=document;p=p->GetParentNode()){
   if(kind==GAL_BOUND_LIST){const auto&v=values[i];for(uint32_t r=v.row_first;r<v.row_first+v.row_count;r++)if(p==row_elements[r]&&(rows[r].flags&1)){row=rows[r].id;matched=p;break;}}
   else if(p==elements[i])matched=p;
   if(matched)break;
  }
  if(!matched)continue;
  auto a=Read(i,row,matched);if(!a.action){overflow++;std::snprintf(diagnostic,sizeof(diagnostic),"Binding %s input action exceeds 255 UTF8 bytes / 64 scalars or contains invalid text",targets[i].id);return;}
  if(count==queue.size()){overflow++;return;}
  queue[(first+count)%queue.size()]=a;count++;return;
 }
}
bool UiBoundDocument::Poll(gal_bound_ui_action&a,std::string&e){
 if(a.size!=sizeof(a))return bad(e,"invalid binding action size");
 a={};a.size=sizeof(a);a.generation=generation;a.revision=revision;
 if(count){a=queue[first];first=(first+1)%queue.size();count--;}return true;
}
bool UiBoundDocument::Test(uint32_t command,gal_bound_ui_action&a,Rml::Context*context,std::string&e){
 if(a.size!=sizeof(a)||a.generation!=generation||a.revision!=revision)return bad(e,"stale binding probe generation/revision");
 auto*element=Find(a.target,a.row);if(!element)return bad(e,"binding probe target/row is absent");
 if(command==5){a=Read(a.target,a.row,element);return true;}
 if(!(values[a.target].flags&1))return bad(e,"binding probe target disabled");
 if(command==1)element->DispatchEvent("click",{});
 else if(command==2||command==6||command==7){auto xy=element->GetAbsoluteOffset();context->ProcessMouseMove(int(xy.x+8),int(xy.y+8),0);if(command!=7)context->ProcessMouseButtonDown(0,0);if(command!=6)context->ProcessMouseButtonUp(0,0);}
 else if(command==3)element->Focus();
 else if(command==4){auto*input=dynamic_cast<Rml::ElementFormControlInput*>(element);if(!input)return bad(e,"binding change probe requires input");
  if(a.kind==GAL_BOUND_TEXT_INPUT){if(!ui_valid_utf8(a.text,256,64))return bad(e,"invalid probe text");applying=true;input->SetValue(a.text);}
  else if(a.kind==GAL_BOUND_BOOLEAN){applying=true;if(a.flags&2)input->SetAttribute("checked","");else input->RemoveAttribute("checked");}
  else if(a.kind==GAL_BOUND_NUMBER){if(!std::isfinite(a.number)||a.number<0||a.number>100||std::floor(a.number)!=a.number)return bad(e,"invalid probe number");applying=true;input->SetValue(std::to_string(static_cast<int>(a.number)));}
  else return bad(e,"invalid probe kind");
  applying=false;element->DispatchEvent("change",{});
 }else return bad(e,"unknown binding probe command");
 return true;
}
