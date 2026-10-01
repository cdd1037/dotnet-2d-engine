#pragma once
#include <algorithm>
#include <cmath>
struct TextInputRect {int x=0,y=0,w=0,h=0;};
// Rml uses framebuffer pixels; SDL text-input rectangles use logical window coordinates.
inline bool text_input_rect(float x,float y,float line_height,int ww,int wh,int pw,int ph,TextInputRect& out){
 if(!std::isfinite(x)||!std::isfinite(y)||!std::isfinite(line_height)||line_height<=0||ww<=0||wh<=0||pw<=0||ph<=0||ww>16384||wh>16384||pw>16384||ph>16384)return false;
 const double sx=double(ww)/pw,sy=double(wh)/ph;
 const int left=int(std::clamp(std::floor(double(x)*sx),0.0,double(ww-1)));
 const int top=int(std::clamp(std::floor(double(y)*sy),0.0,double(wh-1)));
 const int height=int(std::clamp(std::ceil(double(line_height)*sy),1.0,double(wh-top)));
 out={left,top,1,height};return true;
}
