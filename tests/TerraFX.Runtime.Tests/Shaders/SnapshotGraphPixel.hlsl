#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 0)]]
#endif
cbuffer FrameData : register(b0, space0)
{
    float4 color;
};

#if defined(TERRAFX_SPIRV)
[[vk::binding(16, 1)]]
#endif
Texture2D<float4> atlasPage : register(t0, space1);

#if defined(TERRAFX_SPIRV)
[[vk::binding(40, 1)]]
#endif
SamplerState atlasSampler : register(s0, space1);

// Retain the SV_Position register so TEXCOORD uses the same hardware slot as the vertex output.
float4 PSMain(float4 position : SV_Position, float2 texCoord : TEXCOORD0) : SV_Target0
{
    float visibility = position.w > 0.0f ? 1.0f : 0.0f;
    return atlasPage.Sample(atlasSampler, texCoord) * color * visibility;
}
