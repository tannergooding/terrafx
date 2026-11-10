struct VertexOutput
{
    float4 position : SV_Position;
};

struct SpriteVertexInput
{
    float3 position : POSITION0;
    float2 textureCoordinate : TEXCOORD0;
};

struct SpriteVertexOutput
{
    float4 position : SV_Position;
    float2 textureCoordinate : TEXCOORD0;
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

[[vk::binding(34, 0)]]
RWStructuredBuffer<uint> FrameSecondStorageBuffer : register(u2, space0);

[[vk::binding(33, 0)]]
RWTexture2D<uint> FrameStorageImage : register(u1, space0);

[[vk::binding(35, 0)]]
RWTexture2D<float> FrameFloatStorageImage : register(u3, space0);

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

SpriteVertexOutput VSSpriteMain(SpriteVertexInput input)
{
    SpriteVertexOutput output;
    output.position = float4(input.position, 1.0);
    output.textureCoordinate = input.textureCoordinate;
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target0
{
    return color;
}

float4 PSGraphMain(VertexOutput input) : SV_Target0
{
    return float4(1.0, 0.0, 1.0, 1.0);
}

float4 PSSampledMain(VertexOutput input) : SV_Target0
{
    return FrameTexture.Sample(FrameSampler, float2(0.5, 0.5));
}

float4 PSSpriteMain(SpriteVertexOutput input) : SV_Target0
{
    return FrameTexture.Sample(FrameSampler, input.textureCoordinate);
}

float4 PSSampledFrameMain(VertexOutput input) : SV_Target0
{
    return FrameTexture.Sample(FrameSampler, float2(0.5, 0.5)) * color;
}

float4 PSSampledFrameStorageMain(VertexOutput input) : SV_Target0
{
    if (all(uint2(input.position.xy) == uint2(16, 16))
        && (FrameTexture.Sample(FrameSampler, float2(0.5, 0.5)).x > 0.0))
    {
        FrameStorageBuffer[0] = FrameStorageBuffer[0] + 1;
        FrameStorageImage[uint2(0, 0)] = FrameStorageImage[uint2(0, 0)] + 1;
    }

    return color;
}

[numthreads(1, 1, 1)]
void CSStorageMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    if (any(dispatchThreadId != uint3(0, 0, 0)))
    {
        return;
    }

    FrameStorageBuffer[0] = FrameStorageBuffer[0] + 1;
    FrameStorageImage[uint2(0, 0)] = FrameStorageImage[uint2(0, 0)] + 1;
}

[numthreads(1, 1, 1)]
void CSStorageWriteMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    if (any(dispatchThreadId != uint3(0, 0, 0)))
    {
        return;
    }

    FrameStorageBuffer[0] = 0x10203040;
}

[numthreads(1, 1, 1)]
void CSStorageFloatImageMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    if (any(dispatchThreadId != uint3(0, 0, 0)))
    {
        return;
    }

    FrameStorageBuffer[0] = 0;
    FrameFloatStorageImage[uint2(0, 0)] = 1.0;
}

[numthreads(1, 1, 1)]
void CSStorageBuffersMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    if (any(dispatchThreadId != uint3(0, 0, 0)))
    {
        return;
    }

    FrameStorageBuffer[0] = FrameStorageBuffer[0] + 1;
    FrameSecondStorageBuffer[0] = FrameSecondStorageBuffer[0] + 2;
}
