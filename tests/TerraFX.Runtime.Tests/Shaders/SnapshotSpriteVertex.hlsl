#if defined(TERRAFX_SPIRV)
// The pinned DXC SPIR-V path maps column_major to a RowMajor block decoration.
#define TERRAFX_MATRIX_MAJOR column_major
#else
#define TERRAFX_MATRIX_MAJOR row_major
#endif

#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 2)]]
#endif
cbuffer ObjectData : register(b0, space2)
{
    TERRAFX_MATRIX_MAJOR float4x4 World;
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
};

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    output.Position = mul(float4(input.Position, 1.0f), World);
    output.TexCoord = input.TexCoord;
    return output;
}
