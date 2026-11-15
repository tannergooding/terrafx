#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 1)]]
#endif
cbuffer MaterialData : register(b0, space1)
{
    float4 BaseColor;
    float4 Factors;
    float4 Emissive;
    float4 RenderOptions;
    float4 NormalOptions;
};

#if defined(TERRAFX_SPIRV)
[[vk::binding(16, 1)]]
#endif
Texture2D<float4> BaseColorTexture : register(t0, space1);

#if defined(TERRAFX_SPIRV)
[[vk::binding(40, 1)]]
#endif
SamplerState BaseColorSampler : register(s0, space1);

struct PixelInput
{
    float4 Position : SV_Position;
#if defined(TERRAFX_SPIRV)
    [[vk::location(0)]]
#endif
    float2 Uv0 : TEXCOORD0;
#if defined(TERRAFX_SPIRV)
    [[vk::location(1)]]
#endif
    float4 Color : COLOR0;
};

void PSMain(PixelInput input)
{
    float alpha = BaseColor.a * input.Color.a * BaseColorTexture.Sample(BaseColorSampler, input.Uv0).a;

    if ((input.Position.w <= 0.0f)
        || ((RenderOptions.y > 0.5f) && (alpha < RenderOptions.x)))
    {
        discard;
    }
}
