// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

#include "AllStages.shared.hlsli"

struct VertexOutput
{
    float4 Position : SV_Position;
};

VertexOutput VSMain(uint vertexId : SV_VertexID)
{
    VertexOutput output;
    output.Position = float4((float)vertexId, SHARED_OFFSET, 0.0f, 1.0f);
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target0
{
    return input.Position;
}

[numthreads(8, 4, 1)]
void CSMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
}

[numthreads(257, 1, 1)]
void OversizedCSMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
}
