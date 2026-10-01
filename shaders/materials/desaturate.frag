#version 450
#extension GL_GOOGLE_include_directive : require
#include "sprite-fragment-v1.glsl"

// first.x = desaturation amount [0, 1]; first.y = RGB brightness (identity: 1).
// first.zw and second are reserved. Alpha remains sampled alpha * vertex alpha.
void main() {
    vec4 sampled = texture(sprite_texture, uv) * color;
    float luminance = dot(sampled.rgb, vec3(0.2126, 0.7152, 0.0722));
    vec3 rgb = mix(sampled.rgb, vec3(luminance), clamp(material.first.x, 0.0, 1.0));
    result = vec4(rgb * material.first.y, sampled.a);
}
