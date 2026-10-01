#version 450
layout(set=2,binding=0) uniform sampler2D attachment;
layout(location=0) in vec2 uv;
layout(location=0) out vec4 result;
void main() {
    vec4 sample_color = texture(attachment, uv);
    // RGBA8 alpha is either zero or at least 1/255; zero is defined transparent black.
    result = sample_color.a > 0.0
        ? vec4(sample_color.rgb / sample_color.a, sample_color.a)
        : vec4(0.0);
}
