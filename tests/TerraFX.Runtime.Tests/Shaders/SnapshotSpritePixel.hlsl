#if defined(TERRAFX_SPIRV)
[[vk::binding(16, 1)]]
#endif
Texture2D<float4> AtlasPage : register(t0, space1);

#if defined(TERRAFX_SPIRV)
[[vk::binding(40, 1)]]
#endif
SamplerState AtlasSampler : register(s0, space1);

#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 0)]]
#endif
cbuffer FrameData : register(b0, space0)
{
    float4 Tint;
};

// Retain the SV_Position register so TEXCOORD uses the same hardware slot as the vertex output.
float4 PSMain(float4 position : SV_Position, float2 texCoord : TEXCOORD0) : SV_Target0
{
    float visibility = position.w > 0.0f ? 1.0f : 0.0f;
    return AtlasPage.Sample(AtlasSampler, texCoord) * Tint * visibility;
}
