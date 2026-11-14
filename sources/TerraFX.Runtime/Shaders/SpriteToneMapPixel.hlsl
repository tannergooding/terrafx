#if defined(TERRAFX_SPIRV)
[[vk::binding(16, 1)]]
#endif
Texture2D<float4> HdrColor : register(t0, space1);

#if defined(TERRAFX_SPIRV)
[[vk::binding(40, 1)]]
#endif
SamplerState HdrSampler : register(s0, space1);

struct PixelInput
{
    float4 Position : SV_Position;
#if defined(TERRAFX_SPIRV)
    [[vk::location(0)]]
#endif
    float2 TexCoord : TEXCOORD0;
};

float4 PSMain(PixelInput input) : SV_Target0
{
    float4 hdr = HdrColor.Sample(HdrSampler, input.TexCoord);
    float3 mapped = hdr.rgb / (1.0f + hdr.rgb);
    float visibility = input.Position.w > 0.0f ? 1.0f : 0.0f;
    return float4(mapped, hdr.a) * visibility;
}
