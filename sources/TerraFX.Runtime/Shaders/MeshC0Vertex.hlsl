#if defined(TERRAFX_SPIRV)
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
    float4 CameraPosition;
    float4 AmbientIrradiance;
    uint4 PointLightCount;
};

#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 2)]]
#endif
cbuffer ObjectData : register(b0, space2)
{
    TERRAFX_MATRIX_MAJOR float4x4 World;
    TERRAFX_MATRIX_MAJOR float4x4 PreviousWorld;
    float4 NormalRow0;
    float4 NormalRow1;
    float4 NormalRow2;
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
    float3 Normal : NORMAL0;
#if defined(TERRAFX_SPIRV)
    [[vk::location(2)]]
#endif
    float2 Uv0 : TEXCOORD0;
};

struct VertexOutput
{
    float4 Position : SV_Position;
#if defined(TERRAFX_SPIRV)
    [[vk::location(0)]]
#endif
    float3 WorldPosition : TEXCOORD0;
#if defined(TERRAFX_SPIRV)
    [[vk::location(1)]]
#endif
    float3 WorldNormal : TEXCOORD1;
#if defined(TERRAFX_SPIRV)
    [[vk::location(2)]]
#endif
    float2 Uv0 : TEXCOORD2;
};

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    float4 worldPosition = mul(float4(input.Position, 1.0f), World);
    output.Position = mul(worldPosition, ViewProjection);
    output.WorldPosition = worldPosition.xyz;
    output.WorldNormal = normalize(mul(input.Normal, float3x3(NormalRow0.xyz, NormalRow1.xyz, NormalRow2.xyz)));
    output.Uv0 = input.Uv0;
    return output;
}
