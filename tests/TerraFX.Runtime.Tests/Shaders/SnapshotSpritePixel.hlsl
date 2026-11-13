#if defined(TERRAFX_SPIRV)
[[vk::binding(16, 1)]]
#endif
Texture2D<float4> AtlasPage : register(t0, space1);

#if defined(TERRAFX_SPIRV)
[[vk::binding(40, 1)]]
#endif
SamplerState AtlasSampler : register(s0, space1);

struct PixelInput
{
    float4 Position : SV_Position;
#if defined(TERRAFX_SPIRV)
    [[vk::location(0)]]
#endif
    float2 TexCoord : TEXCOORD0;
#if defined(TERRAFX_SPIRV)
    [[vk::location(1)]]
#endif
    float4 Tint : TEXCOORD1;
};

// Retain the SV_Position register so TEXCOORD uses the same hardware slot as the vertex output.
float4 PSMain(PixelInput input) : SV_Target0
{
    float visibility = input.Position.w > 0.0f ? 1.0f : 0.0f;
    float4 texel = AtlasPage.Sample(AtlasSampler, input.TexCoord);
    float alpha = texel.a * input.Tint.a;
    float3 premultipliedColor = texel.rgb * input.Tint.rgb * input.Tint.a;
    return float4(premultipliedColor, alpha) * visibility;
}
