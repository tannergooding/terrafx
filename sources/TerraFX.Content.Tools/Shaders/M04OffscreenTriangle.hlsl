struct VertexOutput
{
    float4 position : SV_Position;
};

VertexOutput VSMain(uint vertexId : SV_VertexID)
{
    float2 positions[3] = {
        float2(-0.75, -0.75),
        float2(0.0, 0.75),
        float2(0.75, -0.75),
    };

    VertexOutput output;
    output.position = float4(positions[vertexId], 0.5, 1.0);
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target0
{
    return float4(1.0, 0.0, 1.0, 1.0);
}
