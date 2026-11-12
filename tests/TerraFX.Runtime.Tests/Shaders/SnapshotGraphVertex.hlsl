#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 2)]]
#endif
cbuffer ObjectData : register(b0, space2)
{
    row_major float4x4 world;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float2 texCoord : TEXCOORD0;
};

VertexOutput VSMain(uint vertexId : SV_VertexID)
{
    float2 positions[3] = {
        float2(-0.75f, -0.75f),
        float2(0.0f, 0.75f),
        float2(0.75f, -0.75f),
    };
    float2 texCoords[3] = {
        float2(0.0f, 1.0f),
        float2(0.5f, 0.0f),
        float2(1.0f, 1.0f),
    };

    VertexOutput output;
    output.position = mul(float4(positions[vertexId], 0.5f, 1.0f), world);
    output.texCoord = texCoords[vertexId];
    return output;
}
