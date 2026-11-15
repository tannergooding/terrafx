#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 0)]]
#endif
cbuffer FrameData : register(b0, space0)
{
    // These rows match RenderDirectionalShadowCameraData's serialized matrix bytes.
    float4 ViewProjectionRows[4];
};

#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 2)]]
#endif
cbuffer ObjectData : register(b0, space2)
{
    float4 WorldRows[4];
    float4 PreviousWorldRows[4];
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
#if defined(TERRAFX_TEXTURED)
#if defined(TERRAFX_SPIRV)
    [[vk::location(1)]]
#endif
    float2 Uv0 : TEXCOORD0;
#if defined(TERRAFX_SPIRV)
    [[vk::location(2)]]
#endif
    float4 Color : COLOR0;
#if defined(TERRAFX_SKINNED)
#if defined(TERRAFX_SPIRV)
    [[vk::location(3)]]
#endif
    float4 JointIndices : BLENDINDICES0;
#if defined(TERRAFX_SPIRV)
    [[vk::location(4)]]
#endif
    float4 JointWeights : BLENDWEIGHT0;
#endif
#else
#if defined(TERRAFX_SPIRV)
    [[vk::location(1)]]
#endif
    float4 Color : COLOR0;
#if defined(TERRAFX_SKINNED)
#if defined(TERRAFX_SPIRV)
    [[vk::location(2)]]
#endif
    float4 JointIndices : BLENDINDICES0;
#if defined(TERRAFX_SPIRV)
    [[vk::location(3)]]
#endif
    float4 JointWeights : BLENDWEIGHT0;
#endif
#endif
};

struct VertexOutput
{
    float4 Position : SV_Position;
#if defined(TERRAFX_TEXTURED)
#if defined(TERRAFX_SPIRV)
    [[vk::location(0)]]
#endif
    float2 Uv0 : TEXCOORD0;
#if defined(TERRAFX_SPIRV)
    [[vk::location(1)]]
#endif
    float4 Color : COLOR0;
#else
#if defined(TERRAFX_SPIRV)
    [[vk::location(0)]]
#endif
    float4 Color : COLOR0;
#endif
};

float4 MultiplyByRows(float4 vector, float4 row0, float4 row1, float4 row2, float4 row3)
{
    return float4(
        dot(vector, float4(row0.x, row1.x, row2.x, row3.x)),
        dot(vector, float4(row0.y, row1.y, row2.y, row3.y)),
        dot(vector, float4(row0.z, row1.z, row2.z, row3.z)),
        dot(vector, float4(row0.w, row1.w, row2.w, row3.w)));
}

VertexOutput VSMain(VertexInput input)
{
    float4 localPosition = float4(input.Position, 1.0f);

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
    }
#endif

    VertexOutput output;
    float4 worldPosition = MultiplyByRows(
        localPosition,
        WorldRows[0],
        WorldRows[1],
        WorldRows[2],
        WorldRows[3]);
    output.Position = MultiplyByRows(
        worldPosition,
        ViewProjectionRows[0],
        ViewProjectionRows[1],
        ViewProjectionRows[2],
        ViewProjectionRows[3]);
#if defined(TERRAFX_TEXTURED)
    output.Uv0 = input.Uv0;
#endif
    output.Color = input.Color;
    return output;
}
