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

struct DepthVertexOutput
{
    float4 position : SV_Position;
    float4 color : COLOR0;
};

struct MultipleColorTargetsOutput
{
    float4 primary : SV_Target0;
    float4 secondary : SV_Target1;
    float4 tertiary : SV_Target2;
    float4 quaternary : SV_Target3;
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

[[vk::binding(3, 0)]]
cbuffer FrameThirdData : register(b3, space0)
{
    float4 thirdColor;
};

[[vk::binding(0, 1)]]
cbuffer MaterialData0 : register(b0, space1)
{
    float4 materialColor0;
};

[[vk::binding(1, 1)]]
cbuffer MaterialData1 : register(b1, space1)
{
    float4 materialColor1;
};

[[vk::binding(2, 1)]]
cbuffer MaterialData2 : register(b2, space1)
{
    float4 materialColor2;
};

[[vk::binding(3, 1)]]
cbuffer MaterialData3 : register(b3, space1)
{
    float4 materialColor3;
};

[[vk::binding(16, 0)]]
Texture2D<float4> FrameTexture : register(t0, space0);

[[vk::binding(17, 0)]]
Texture2D<float4> FrameSecondTexture : register(t1, space0);

[[vk::binding(18, 0)]]
Texture2DArray<float4> FrameArrayTexture : register(t2, space0);

[[vk::binding(40, 0)]]
SamplerState FrameSampler : register(s0, space0);

[[vk::binding(41, 0)]]
SamplerState FrameSecondSampler : register(s1, space0);

[[vk::binding(42, 0)]]
SamplerState FrameArraySampler : register(s2, space0);

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

[[vk::binding(19, 0)]]
Texture2D<float4> FrameManyTexture3 : register(t3, space0);

[[vk::binding(20, 0)]]
Texture2D<float4> FrameManyTexture4 : register(t4, space0);

[[vk::binding(21, 0)]]
Texture2D<float4> FrameManyTexture5 : register(t5, space0);

[[vk::binding(22, 0)]]
Texture2D<float4> FrameManyTexture6 : register(t6, space0);

[[vk::binding(23, 0)]]
Texture2D<float4> FrameManyTexture7 : register(t7, space0);

[[vk::binding(24, 0)]]
Texture2D<float4> FrameManyTexture8 : register(t8, space0);

[[vk::binding(25, 0)]]
Texture2D<float4> FrameManyTexture9 : register(t9, space0);

[[vk::binding(26, 0)]]
Texture2D<float4> FrameManyTexture10 : register(t10, space0);

[[vk::binding(27, 0)]]
Texture2D<float4> FrameManyTexture11 : register(t11, space0);

[[vk::binding(28, 0)]]
Texture2D<float4> FrameManyTexture12 : register(t12, space0);

[[vk::binding(29, 0)]]
Texture2D<float4> FrameManyTexture13 : register(t13, space0);

[[vk::binding(30, 0)]]
Texture2D<float4> FrameManyTexture14 : register(t14, space0);

[[vk::binding(31, 0)]]
Texture2D<float4> FrameManyTexture15 : register(t15, space0);

[[vk::binding(43, 0)]]
SamplerState FrameSampler3 : register(s3, space0);

[[vk::binding(44, 0)]]
SamplerState FrameSampler4 : register(s4, space0);

[[vk::binding(45, 0)]]
SamplerState FrameSampler5 : register(s5, space0);

[[vk::binding(46, 0)]]
SamplerState FrameSampler6 : register(s6, space0);

[[vk::binding(47, 0)]]
SamplerState FrameSampler7 : register(s7, space0);

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

DepthVertexOutput VSDepthMain(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    float2 positions[3] = {
        float2(-0.75, -0.75),
        float2(0.0, 0.75),
        float2(0.75, -0.75),
    };

    bool isNear = instanceId == 0;
    DepthVertexOutput output;
    output.position = float4(positions[vertexId], isNear ? 0.75 : 0.25, 1.0);
    output.color = isNear
        ? float4(1.0, 0.0, 1.0, 1.0)
        : float4(0.0, 1.0, 1.0, 1.0);
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

MultipleColorTargetsOutput PSMultipleColorTargetsMain(VertexOutput input)
{
    MultipleColorTargetsOutput output;
    output.primary = float4(1.0, 0.0, 1.0, 1.0);
    output.secondary = float4(0.0, 1.0, 1.0, 1.0);
    output.tertiary = float4(1.0, 1.0, 0.0, 1.0);
    output.quaternary = float4(1.0, 1.0, 1.0, 1.0);
    return output;
}

float4 PSDepthMain(DepthVertexOutput input) : SV_Target0
{
    return input.color;
}

float4 PSFrameLayoutMain(VertexOutput input) : SV_Target0
{
    return mul(transform, float4(samples[0] + samples[1], 1.0)) + tail;
}

float4 PSMultipleFrameMain(VertexOutput input) : SV_Target0
{
    return (color + secondColor) * 0.5;
}

float4 PSEightUniformsMain(VertexOutput input) : SV_Target0
{
    return (color + secondColor + tail + thirdColor
        + materialColor0 + materialColor1 + materialColor2 + materialColor3) / 8.0;
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

float4 PSSampledArrayMain(VertexOutput input) : SV_Target0
{
    return FrameArrayTexture.Sample(FrameArraySampler, float3(0.5, 0.5, 0.0));
}

float4 PSSampledMultipleMain(VertexOutput input) : SV_Target0
{
    return (FrameTexture.Sample(FrameSampler, float2(0.5, 0.5))
        + FrameSecondTexture.Sample(FrameSecondSampler, float2(0.5, 0.5))) * 0.5;
}

float4 PSSampledSixteenMain(VertexOutput input) : SV_Target0
{
    float2 coordinates = float2(0.5, 0.5);
    float4 color = FrameTexture.Sample(FrameSampler, coordinates)
        + FrameSecondTexture.Sample(FrameSampler, coordinates)
        + FrameArrayTexture.Sample(FrameSecondSampler, float3(coordinates, 0.0))
        + FrameManyTexture3.Sample(FrameSecondSampler, coordinates)
        + FrameManyTexture4.Sample(FrameArraySampler, coordinates)
        + FrameManyTexture5.Sample(FrameArraySampler, coordinates)
        + FrameManyTexture6.Sample(FrameSampler3, coordinates)
        + FrameManyTexture7.Sample(FrameSampler3, coordinates)
        + FrameManyTexture8.Sample(FrameSampler4, coordinates)
        + FrameManyTexture9.Sample(FrameSampler4, coordinates)
        + FrameManyTexture10.Sample(FrameSampler5, coordinates)
        + FrameManyTexture11.Sample(FrameSampler5, coordinates)
        + FrameManyTexture12.Sample(FrameSampler6, coordinates)
        + FrameManyTexture13.Sample(FrameSampler6, coordinates)
        + FrameManyTexture14.Sample(FrameSampler7, coordinates)
        + FrameManyTexture15.Sample(FrameSampler7, coordinates);
    return color * (1.0 / 16.0);
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
