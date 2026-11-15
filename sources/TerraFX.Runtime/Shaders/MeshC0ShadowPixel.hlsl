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

struct PixelInput
{
    float4 Position : SV_Position;
#if defined(TERRAFX_SPIRV)
    [[vk::location(0)]]
#endif
    float4 Color : COLOR0;
};

void PSMain(PixelInput input)
{
    if ((input.Position.w <= 0.0f)
        || ((RenderOptions.y > 0.5f) && ((BaseColor.a * input.Color.a) < RenderOptions.x)))
    {
        discard;
    }
}
