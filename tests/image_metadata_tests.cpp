#include "image_metadata.h"
#include <cstdio>
#include <vector>
#define CHECK(x) do{if(!(x)){std::fprintf(stderr,"FAIL image metadata line %d: %s (%s)\n",__LINE__,#x,error.c_str());return 1;}}while(0)
static void le(std::vector<unsigned char>&b,size_t at,uint32_t n){for(unsigned i=0;i<4;i++)b[at+i]=static_cast<unsigned char>(n>>(i*8));}
static void be(std::vector<unsigned char>&b,size_t at,uint32_t n){for(unsigned i=0;i<4;i++)b[at+i]=static_cast<unsigned char>(n>>((3-i)*8));}
static void chunk(std::vector<unsigned char>&b,const char*tag,const std::vector<unsigned char>&payload={}){
 const size_t at=b.size();b.resize(at+12+payload.size());be(b,at,uint32_t(payload.size()));std::memcpy(b.data()+at+4,tag,4);if(!payload.empty())std::memcpy(b.data()+at+8,payload.data(),payload.size());
}
static std::vector<unsigned char> png(){
 std::vector<unsigned char>b={0x89,'P','N','G','\r','\n','\x1a','\n'};
 std::vector<unsigned char>header(13);be(header,0,2);be(header,4,3);header[8]=8;header[9]=6;
 chunk(b,"IHDR",header);chunk(b,"IDAT",{1});chunk(b,"IEND");return b;
}
int main(){
 ImageMetadata metadata;std::string error;auto valid=[&](const std::vector<unsigned char>&b){return image_metadata(b.data(),b.size(),metadata,error);};
 CHECK(!image_metadata(nullptr,0,metadata,error));
 std::vector<unsigned char>bmp(54);bmp[0]='B';bmp[1]='M';le(bmp,14,40);le(bmp,18,2);le(bmp,22,3);
 CHECK(valid(bmp)&&metadata.kind==ImageKind::Bmp&&metadata.width==2&&metadata.height==3&&metadata.RgbaBytes()==24);
 auto changed=bmp;le(changed,22,uint32_t(-3));CHECK(valid(changed)&&metadata.height==3);
 for(auto offset:{18u,22u})for(auto value:{0u,4097u,0x80000000u}){changed=bmp;le(changed,offset,value);CHECK(!valid(changed));}
 changed=bmp;le(changed,14,41);CHECK(!valid(changed));changed=bmp;changed.resize(26);CHECK(!valid(changed));
 auto bytes=png();CHECK(valid(bytes)&&metadata.kind==ImageKind::Png&&metadata.width==2&&metadata.height==3);
 changed=bytes;changed[24]=16;CHECK(!valid(changed));changed=bytes;changed[26]=1;CHECK(!valid(changed));changed=bytes;changed[28]=2;CHECK(!valid(changed));
 changed=bytes;be(changed,16,4097);CHECK(!valid(changed));changed=bytes;be(changed,8,12);CHECK(!valid(changed));changed=bytes;be(changed,33,UINT32_MAX);CHECK(!valid(changed));
 changed=bytes;changed.pop_back();CHECK(!valid(changed));changed=bytes;changed.push_back(0);CHECK(!valid(changed));
 for(const char*tag:{"IHDR","acTL","CgBI"}){changed=bytes;std::memcpy(changed.data()+37,tag,4);CHECK(!valid(changed));}
 changed=bytes;std::memcpy(changed.data()+37,"tEXt",4);CHECK(!valid(changed));
 // Complete frame metadata with intentionally invalid/missing pixel payload:
 // preflight accepts it, while the existing image decoder remains authoritative.
 std::vector<unsigned char>jpeg={0xff,0xd8,0xff,0xc0,0,11,8,0,3,0,2,1,1,0x11,0,0xff,0xda,0,8,1,1,0,0,63,0};
 CHECK(valid(jpeg)&&metadata.kind==ImageKind::Jpeg&&metadata.width==2&&metadata.height==3);
 changed=jpeg;changed[3]=0xc2;CHECK(valid(changed));changed=jpeg;changed[3]=0xc3;CHECK(!valid(changed));changed=jpeg;changed[6]=12;CHECK(!valid(changed));
 changed=jpeg;changed[5]=12;CHECK(!valid(changed));changed=jpeg;changed[11]=3;CHECK(!valid(changed));changed=jpeg;changed[8]=changed[7]=0;CHECK(!valid(changed));
 changed=jpeg;changed[9]=0x10;changed[10]=1;CHECK(!valid(changed));changed=jpeg;changed[3]=0xd8;CHECK(!valid(changed));changed=jpeg;changed.resize(14);CHECK(!valid(changed));
 std::vector<unsigned char>oversized(image_max_encoded_bytes+1);CHECK(!valid(oversized));
 changed=bmp;le(changed,18,4096);le(changed,22,4096);CHECK(valid(changed)&&metadata.RgbaBytes()==image_max_rgba_bytes);
 std::puts("PASS image metadata: BMP/PNG/JPEG bounds, chunk/marker structure, unsupported formats and decoded-size budgets");
}
