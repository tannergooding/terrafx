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
    float4 CameraForward;
    float4 CascadeEnds;
    // Keep FrameData identical to the pixel-stage CPU layout.
    float4 ShadowViewProjectionRows[16];
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

#if defined(TERRAFX_SKINNED)
struct BonePaletteEntry
{
    float4 SkinRow0;
    float4 SkinRow1;
    float4 SkinRow2;
    float4 SkinRow3;
    float4 NormalRow0;
    float4 NormalRow1;
    float4 NormalRow2;
};

#if defined(TERRAFX_SPIRV)
[[vk::binding(32, 2)]]
#endif
RWStructuredBuffer<BonePaletteEntry> BonePalette : register(u0, space2);
#endif

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
#if defined(TERRAFX_TEXTURED)
#if defined(TERRAFX_SPIRV)
    [[vk::location(2)]]
#endif
    float2 Uv0 : TEXCOORD0;
#if defined(TERRAFX_SPIRV)
    [[vk::location(3)]]
#endif
    float4 Tangent : TANGENT0;
#endif
#if defined(TERRAFX_SPIRV)
#if defined(TERRAFX_TEXTURED)
    [[vk::location(4)]]
#else
    [[vk::location(2)]]
#endif
#endif
    float4 Color : COLOR0;
#if defined(TERRAFX_SKINNED)
#if defined(TERRAFX_SPIRV)
#if defined(TERRAFX_TEXTURED)
    [[vk::location(5)]]
#else
    [[vk::location(3)]]
#endif
#endif
    float4 JointIndices : BLENDINDICES0;
#if defined(TERRAFX_SPIRV)
#if defined(TERRAFX_TEXTURED)
    [[vk::location(6)]]
#else
    [[vk::location(4)]]
#endif
#endif
    float4 JointWeights : BLENDWEIGHT0;
#endif
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
#if defined(TERRAFX_TEXTURED)
#if defined(TERRAFX_SPIRV)
    [[vk::location(2)]]
#endif
    float2 Uv0 : TEXCOORD2;
#if defined(TERRAFX_SPIRV)
    [[vk::location(3)]]
#endif
    float4 WorldTangent : TEXCOORD3;
#if defined(TERRAFX_SPIRV)
    [[vk::location(4)]]
#endif
    float4 Color : TEXCOORD4;
#else
#if defined(TERRAFX_SPIRV)
    [[vk::location(2)]]
#endif
    float4 Color : TEXCOORD2;
#endif
};

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    float4 localPosition = float4(input.Position, 1.0f);
    float3 localNormal = input.Normal;
#if defined(TERRAFX_TEXTURED)
    float3 localTangent = input.Tangent.xyz;
#endif

#if defined(TERRAFX_SKINNED)
    if (Metadata.x != 0)
    {
        uint paletteOffset = Metadata.x - 1;
        uint4 jointIndices = (uint4)input.JointIndices;
        float4 weights = input.JointWeights;
        BonePaletteEntry bone0 = BonePalette[paletteOffset + jointIndices.x];
        BonePaletteEntry bone1 = BonePalette[paletteOffset + jointIndices.y];
        BonePaletteEntry bone2 = BonePalette[paletteOffset + jointIndices.z];
        BonePaletteEntry bone3 = BonePalette[paletteOffset + jointIndices.w];
        float4 skinnedPosition0 =
            (input.Position.x * bone0.SkinRow0)
            + (input.Position.y * bone0.SkinRow1)
            + (input.Position.z * bone0.SkinRow2)
            + bone0.SkinRow3;
        float4 skinnedPosition1 =
            (input.Position.x * bone1.SkinRow0)
            + (input.Position.y * bone1.SkinRow1)
            + (input.Position.z * bone1.SkinRow2)
            + bone1.SkinRow3;
        float4 skinnedPosition2 =
            (input.Position.x * bone2.SkinRow0)
            + (input.Position.y * bone2.SkinRow1)
            + (input.Position.z * bone2.SkinRow2)
            + bone2.SkinRow3;
        float4 skinnedPosition3 =
            (input.Position.x * bone3.SkinRow0)
            + (input.Position.y * bone3.SkinRow1)
            + (input.Position.z * bone3.SkinRow2)
            + bone3.SkinRow3;
        localPosition = (weights.x * skinnedPosition0)
            + (weights.y * skinnedPosition1)
            + (weights.z * skinnedPosition2)
            + (weights.w * skinnedPosition3);
        localNormal =
            (weights.x * mul(input.Normal, float3x3(bone0.NormalRow0.xyz, bone0.NormalRow1.xyz, bone0.NormalRow2.xyz)))
            + (weights.y * mul(input.Normal, float3x3(bone1.NormalRow0.xyz, bone1.NormalRow1.xyz, bone1.NormalRow2.xyz)))
            + (weights.z * mul(input.Normal, float3x3(bone2.NormalRow0.xyz, bone2.NormalRow1.xyz, bone2.NormalRow2.xyz)))
            + (weights.w * mul(input.Normal, float3x3(bone3.NormalRow0.xyz, bone3.NormalRow1.xyz, bone3.NormalRow2.xyz)));
#if defined(TERRAFX_TEXTURED)
        localTangent =
            (weights.x * mul(input.Tangent.xyz, float3x3(bone0.SkinRow0.xyz, bone0.SkinRow1.xyz, bone0.SkinRow2.xyz)))
            + (weights.y * mul(input.Tangent.xyz, float3x3(bone1.SkinRow0.xyz, bone1.SkinRow1.xyz, bone1.SkinRow2.xyz)))
            + (weights.z * mul(input.Tangent.xyz, float3x3(bone2.SkinRow0.xyz, bone2.SkinRow1.xyz, bone2.SkinRow2.xyz)))
            + (weights.w * mul(input.Tangent.xyz, float3x3(bone3.SkinRow0.xyz, bone3.SkinRow1.xyz, bone3.SkinRow2.xyz)));
#endif
    }
#endif

    float4 worldPosition = mul(localPosition, World);
    output.Position = mul(worldPosition, ViewProjection);
    output.WorldPosition = worldPosition.xyz;
    output.WorldNormal = normalize(mul(localNormal, float3x3(NormalRow0.xyz, NormalRow1.xyz, NormalRow2.xyz)));
#if defined(TERRAFX_TEXTURED)
    output.Uv0 = input.Uv0;
    output.WorldTangent = float4(mul(localTangent, (float3x3)World), input.Tangent.w);
#endif
    output.Color = input.Color;
    return output;
}
