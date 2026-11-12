#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 2)]]
// The pinned DXC SPIR-V path maps column_major to a RowMajor block decoration.
#define TERRAFX_MATRIX_MAJOR column_major
#else
#define TERRAFX_MATRIX_MAJOR row_major
#endif

cbuffer ObjectGpuData : register(b0, space2)
{
    TERRAFX_MATRIX_MAJOR float4x4 World;
    TERRAFX_MATRIX_MAJOR float4x4 PreviousWorld;
    float4 NormalRow0;
    float4 NormalRow1;
    float4 NormalRow2;
    uint4 Metadata;
};

float4 VSMain(uint vertexId : SV_VertexID) : SV_Position
{
    float2 positions[3] = {
        float2(-0.75f, -0.75f),
        float2(0.0f, 0.75f),
        float2(0.75f, -0.75f),
    };

    float4 position = float4(positions[vertexId], 0.5f, 1.0f);
    float4 current = mul(position, World);
    float4 previous = mul(position, PreviousWorld);
    float4 normal = NormalRow0 + NormalRow1 + NormalRow2;
    float4 metadata = asfloat(Metadata);
    return current + previous + normal + metadata;
}
