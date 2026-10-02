#pragma once
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <string>

// The format gate inspects the same immutable bytes later passed to SDL_image.
// These are encoded-file / image-dimension budgets, not an allocator sandbox for
// the third-party decoder's temporary workspace on malformed compressed input.
constexpr size_t image_max_encoded_bytes = 16u * 1024u * 1024u;
constexpr uint64_t image_max_rgba_bytes = 64u * 1024u * 1024u;
constexpr uint32_t image_max_dimension = 4096;
enum class ImageKind { Bmp, Png, Jpeg };
struct ImageMetadata { ImageKind kind{}; uint32_t width=0,height=0; uint64_t RgbaBytes()const{return uint64_t(width)*height*4;} };
inline uint32_t image_u32le(const unsigned char*p){return uint32_t(p[0])|(uint32_t(p[1])<<8)|(uint32_t(p[2])<<16)|(uint32_t(p[3])<<24);}
inline uint32_t image_u32be(const unsigned char*p){return (uint32_t(p[0])<<24)|(uint32_t(p[1])<<16)|(uint32_t(p[2])<<8)|uint32_t(p[3]);}
inline uint32_t image_u16be(const unsigned char*p){return (uint32_t(p[0])<<8)|uint32_t(p[1]);}
inline bool image_metadata(const unsigned char*data,size_t bytes,ImageMetadata&out,std::string&e){
 auto reject=[&](const char*message){e=message;return false;};
 if(!data||bytes<2||bytes>image_max_encoded_bytes)return reject("image encoded size must be 2 bytes..16 MiB");
 if(data[0]=='B'&&data[1]=='M'){
  if(bytes<54||image_u32le(data+14)<40||uint64_t(image_u32le(data+14))+14>bytes)return reject("BMP requires a complete DIB header >=40 bytes");
  const uint32_t raw_height=image_u32le(data+22);
  const int64_t signed_height=raw_height&0x80000000u?int64_t(raw_height)-0x100000000LL:raw_height;
  out={ImageKind::Bmp,image_u32le(data+18),uint32_t(signed_height<0?-signed_height:signed_height)};
 }else if(bytes>=8&&std::memcmp(data,"\x89PNG\r\n\x1a\n",8)==0){
  if(bytes<33||image_u32be(data+8)!=13||std::memcmp(data+12,"IHDR",4)!=0)return reject("PNG requires a leading 13-byte IHDR");
  out={ImageKind::Png,image_u32be(data+16),image_u32be(data+20)};
  if(data[24]!=1&&data[24]!=2&&data[24]!=4&&data[24]!=8)return reject("PNG bit depth must be 1, 2, 4 or 8");
  const unsigned depth=data[24],color=data[25];
  if(!((color==0||color==3)||((color==2||color==4||color==6)&&depth==8)))return reject("unsupported PNG color/bit-depth combination");
  if(data[26]||data[27]||data[28]>1)return reject("unsupported PNG compression/filter/interlace");
  bool ended=false,has_data=false;
  for(size_t at=8;at<bytes;){
   if(bytes-at<12)return reject("truncated PNG chunk");
   const size_t length=image_u32be(data+at);
   if(length>bytes-at-12)return reject("truncated PNG chunk payload");
   const auto*tag=data+at+4;
   if(at!=8&&std::memcmp(tag,"IHDR",4)==0)return reject("duplicate PNG IHDR");
   if(std::memcmp(tag,"acTL",4)==0||std::memcmp(tag,"CgBI",4)==0)return reject("animated/vendor PNG is outside the static image profile");
   if(std::memcmp(tag,"IDAT",4)==0)has_data=true;
   if(std::memcmp(tag,"IEND",4)==0){if(length||at+12!=bytes)return reject("invalid PNG end/trailing data");ended=true;break;}
   at+=length+12;
  }
  if(!ended||!has_data)return reject("PNG requires IDAT and IEND");
 }else if(data[0]==0xff&&data[1]==0xd8){
  bool found=false,scan=false;unsigned frame_components=0;size_t at=2;
  while(at<bytes){
   if(data[at++]!=0xff)return reject("invalid JPEG marker");
   while(at<bytes&&data[at]==0xff)at++;
   if(at==bytes)return reject("truncated JPEG marker");
   const unsigned char marker=data[at++];
   if(marker==0xd9)return reject("JPEG scan not found before end");
   if(marker==0x00||marker==0xd8||(marker>=0xd0&&marker<=0xd7))return reject("unexpected JPEG marker before scan");
   if(marker==0x01)continue;
   if(bytes-at<2)return reject("truncated JPEG segment");
   const size_t length=image_u16be(data+at);
   if(length<2||length>bytes-at)return reject("invalid JPEG segment length");
   if(marker==0xda){
    if(!found||length<6||data[at+2]<1||data[at+2]>frame_components||length!=6u+2u*data[at+2])return reject("invalid JPEG scan header");
    scan=true;break;
   }
   const bool sof=marker>=0xc0&&marker<=0xcf&&marker!=0xc4&&marker!=0xc8&&marker!=0xcc;
   if(sof){
    if(found||!(marker==0xc0||marker==0xc1||marker==0xc2)||length<8||data[at+2]!=8)return reject("JPEG requires a single 8-bit baseline/progressive frame");
    const unsigned components=data[at+7];
    if((components!=1&&components!=3&&components!=4)||length!=8+3*components)return reject("invalid JPEG frame components");
    out={ImageKind::Jpeg,image_u16be(data+at+5),image_u16be(data+at+3)};found=true;frame_components=components;
   }
   at+=length;
  }
  if(!found||!scan)return reject("JPEG frame/scan dimensions not found");
 }else return reject("image must be BMP, PNG or JPEG");
 if(out.width<1||out.height<1||out.width>image_max_dimension||out.height>image_max_dimension||out.RgbaBytes()>image_max_rgba_bytes)return reject("image dimensions must be 1..4096 and RGBA pixels <=64 MiB");
 return true;
}
