// Fixed sprite-fragment-v1 interface. Include this from a complete GLSL fragment.
layout(set = 2, binding = 0) uniform sampler2D sprite_texture;
layout(location = 0) in vec2 uv;
layout(location = 1) in vec4 color;
layout(location = 0) out vec4 result;
layout(std140, set = 3, binding = 0) uniform MaterialParameters {
    vec4 first;
    vec4 second;
} material;
