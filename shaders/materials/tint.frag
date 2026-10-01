#version 450
#extension GL_GOOGLE_include_directive : require
#include "sprite-fragment-v1.glsl"

// first = RGBA multiplier (identity: 1, 1, 1, 1); second is reserved.
void main() {
    result = texture(sprite_texture, uv) * color * material.first;
}
