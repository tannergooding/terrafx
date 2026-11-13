#if defined(TERRAFX_SPIRV)
// The pinned DXC SPIR-V path maps column_major to a RowMajor block decoration.
#define TERRAFX_MATRIX_MAJOR column_major
#else
#define TERRAFX_MATRIX_MAJOR row_major
#endif

#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 0)]]
#endif
cbuffer FrameData : register(b0, space0)
{
    TERRAFX_MATRIX_MAJOR float4x4 ViewProjection;
    float4 RenderArea;
};

#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 2)]]
#endif
cbuffer ObjectData : register(b0, space2)
{
    TERRAFX_MATRIX_MAJOR float4x4 World;
    float4 Tint;
    uint4 Metadata;
};

struct VertexInput
{
#if defined(TERRAFX_SPIRV)
    [[vk::location(0)]]
#endif
    float3 Position : POSITION0;
#if defined(TERRAFX_SPIRV)
    [[vk::location(1)]]
#endif
    float2 TexCoord : TEXCOORD0;
};

struct VertexOutput
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

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    float4 worldPosition = mul(float4(input.Position, 1.0f), World);
    output.Position = mul(worldPosition, ViewProjection);

    if (((Metadata.x & 1u) != 0u) && (output.Position.w > 0.0f))
    {
        float2 screenPosition = RenderArea.xy
            + ((float2(output.Position.x, -output.Position.y) / output.Position.w * 0.5f) + 0.5f) * RenderArea.zw;
        screenPosition = floor(screenPosition + 0.5f);
        float2 ndc = float2(
            (2.0f * (screenPosition.x - RenderArea.x) / RenderArea.z) - 1.0f,
            1.0f - (2.0f * (screenPosition.y - RenderArea.y) / RenderArea.w));
        output.Position.xy = ndc * output.Position.w;
    }

    output.TexCoord = input.TexCoord;
    output.Tint = Tint;
    return output;
}
