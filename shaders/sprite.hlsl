// Future D3D12 offline compilation: dxc -T vs_6_0 -E VSMain / -T ps_6_0 -E PSMain.
// Shader output not wired until DXIL is compiled and the backend is validated.
struct VSInput { float2 position:TEXCOORD0; float2 uv:TEXCOORD1; float4 color:TEXCOORD2; };
struct VSOutput { float4 position:SV_Position; float2 uv:TEXCOORD0; float4 color:TEXCOORD1; };
VSOutput VSMain(VSInput input){ VSOutput o; o.position=float4(input.position,0,1);o.uv=input.uv;o.color=input.color;return o; }
Texture2D<float4> sprite_texture:register(t0,space2);
SamplerState sprite_sampler:register(s0,space2);
float4 PSMain(VSOutput input):SV_Target0 { return sprite_texture.Sample(sprite_sampler,input.uv)*input.color; }
