#version 450
layout(set=2,binding=0) uniform sampler2D sprite_texture;
layout(location=0) in vec2 uv;
layout(location=1) in vec4 color;
layout(location=0) out vec4 result;
void main(){ result=texture(sprite_texture,uv)*color; }
