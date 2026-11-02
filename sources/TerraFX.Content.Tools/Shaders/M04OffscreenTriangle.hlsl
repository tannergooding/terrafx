struct VertexOutput
{
    float4 position : SV_Position;
};

[[vk::binding(0, 0)]]
cbuffer FrameData : register(b0, space0)
{
    float4 color;
};

[[vk::binding(16, 0)]]
Texture2D<float4> FrameTexture : register(t0, space0);

[[vk::binding(40, 0)]]
SamplerState FrameSampler : register(s0, space0);

[[vk::binding(32, 0)]]
RWStructuredBuffer<uint> FrameStorageBuffer : register(u0, space0);

[[vk::binding(33, 0)]]
RWTexture2D<uint> FrameStorageImage : register(u1, space0);

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
    return color;
}

float4 PSSampledMain(VertexOutput input) : SV_Target0
{
    return FrameTexture.Sample(FrameSampler, float2(0.5, 0.5));
}

float4 PSSampledFrameMain(VertexOutput input) : SV_Target0
{
    return FrameTexture.Sample(FrameSampler, float2(0.5, 0.5)) * color;
}

float4 PSSampledFrameStorageMain(VertexOutput input) : SV_Target0
{
    FrameStorageBuffer[0] = asuint(color.x);
    FrameStorageImage[uint2(0, 0)] = asuint(FrameTexture.Sample(FrameSampler, float2(0.5, 0.5)).x * color.x);
    return color;
}
