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

[[vk::binding(1, 0)]]
cbuffer FrameSecondData : register(b1, space0)
{
    float4 secondColor;
};

[[vk::binding(2, 0)]]
cbuffer FrameLayoutData : register(b2, space0)
{
    row_major float4x4 transform;
    float3 samples[2];
    float4 tail;
};

[[vk::binding(16, 0)]]
Texture2D<float4> FrameTexture : register(t0, space0);

[[vk::binding(17, 0)]]
Texture2D<float4> FrameSecondTexture : register(t1, space0);

[[vk::binding(40, 0)]]
SamplerState FrameSampler : register(s0, space0);

[[vk::binding(41, 0)]]
SamplerState FrameSecondSampler : register(s1, space0);

[[vk::binding(32, 0)]]
RWStructuredBuffer<uint> FrameStorageBuffer : register(u0, space0);

[[vk::binding(34, 0)]]
RWStructuredBuffer<uint> FrameSecondStorageBuffer : register(u2, space0);

[[vk::binding(33, 0)]]
RWTexture2D<uint> FrameStorageImage : register(u1, space0);

[[vk::binding(35, 0)]]
RWTexture2D<float> FrameFloatStorageImage : register(u3, space0);

[[vk::binding(36, 0)]]
RWTexture2D<int> FrameSignedStorageImage : register(u4, space0);

[[vk::image_format("rgba16f")]]
[[vk::binding(37, 0)]]
RWTexture2D<float4> FrameRgba16FloatStorageImage : register(u5, space0);

[[vk::binding(38, 0)]]
RWTexture2D<uint> FrameSecondStorageImage : register(u6, space0);

[[vk::binding(39, 0)]]
RWTexture3D<uint> Frame3DStorageImage : register(u7, space0);

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

float4 PSFrameLayoutMain(VertexOutput input) : SV_Target0
{
    return mul(transform, float4(samples[0] + samples[1], 1.0)) + tail;
}

float4 PSMultipleFrameMain(VertexOutput input) : SV_Target0
{
    return (color + secondColor) * 0.5;
}

float4 PSGraphMain(VertexOutput input) : SV_Target0
{
    return float4(1.0, 0.0, 1.0, 1.0);
}

uint PSR32UIntMain(VertexOutput input) : SV_Target0
{
    return 0x12345678;
}

float4 PSSampledMain(VertexOutput input) : SV_Target0
{
    return FrameTexture.Sample(FrameSampler, float2(0.5, 0.5));
}

float4 PSSampledMultipleMain(VertexOutput input) : SV_Target0
{
    return (FrameTexture.Sample(FrameSampler, float2(0.5, 0.5))
        + FrameSecondTexture.Sample(FrameSecondSampler, float2(0.5, 0.5))) * 0.5;
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
void CSStorageSignedImageMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    if (any(dispatchThreadId != uint3(0, 0, 0)))
    {
        return;
    }

    FrameStorageBuffer[0] = 0;
    FrameSignedStorageImage[uint2(0, 0)] = -1;
}

[numthreads(1, 1, 1)]
void CSStorageRgba16ImageMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    if (any(dispatchThreadId != uint3(0, 0, 0)))
    {
        return;
    }

    FrameStorageBuffer[0] = 0;
    FrameRgba16FloatStorageImage[uint2(0, 0)] = float4(1.0, 2.0, 3.0, 4.0);
}

[numthreads(1, 1, 1)]
void CSStorageImagesMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    if (any(dispatchThreadId != uint3(0, 0, 0)))
    {
        return;
    }

    FrameStorageBuffer[0] = 0x10203040;
    FrameStorageImage[uint2(0, 0)] = 0x11223344;
    FrameSecondStorageImage[uint2(0, 0)] = 0x55667788;
}

[numthreads(2, 2, 1)]
void CSStorageBuffersMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    if (any(dispatchThreadId != uint3(0, 0, 0)))
    {
        return;
    }

    FrameStorageBuffer[0] = FrameStorageBuffer[0] + 1;
    FrameSecondStorageBuffer[0] = FrameSecondStorageBuffer[0] + 2;
}

[numthreads(1, 1, 1)]
void CSStorage3DImageMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    if (any(dispatchThreadId != uint3(0, 0, 0)))
    {
        return;
    }

    FrameStorageBuffer[0] = FrameStorageBuffer[0] + 1;
    Frame3DStorageImage[uint3(0, 0, 1)] = Frame3DStorageImage[uint3(0, 0, 1)] + 1;
}
