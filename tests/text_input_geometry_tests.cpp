#include "text_input_geometry.h"
#include <cstdio>
#include <limits>
#define R(x) do{if(!(x)){std::fprintf(stderr,"TEXT GEOMETRY FAIL %d %s\n",__LINE__,#x);return 1;}}while(0)
int main(){
 TextInputRect r{};R(text_input_rect(200,100,32,960,540,1920,1080,r));R(r.x==100&&r.y==50&&r.w==1&&r.h==16);
 R(text_input_rect(200,100,32,800,600,1600,900,r));R(r.x==100&&r.y==66&&r.h==22);
 R(text_input_rect(-50,-20,15,960,540,960,540,r));R(r.x==0&&r.y==0&&r.h==15);
 R(text_input_rect(1e30f,1e30f,1e30f,960,540,960,540,r));R(r.x==959&&r.y==539&&r.h==1);
 R(!text_input_rect(0,0,1,0,540,960,540,r));R(!text_input_rect(0,0,0,960,540,960,540,r));
 R(!text_input_rect(std::numeric_limits<float>::quiet_NaN(),0,1,960,540,960,540,r));R(r.x==959&&r.y==539);
 std::puts("PASS text caret window/framebuffer conversion, clamping and invalid dimensions");
}
