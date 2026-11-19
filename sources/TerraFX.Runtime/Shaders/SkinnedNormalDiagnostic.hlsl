#include "MeshC0Skinning.hlsl"

struct DiagnosticVertex
{
    float4 PositionNormalX;
    float4 NormalYzUv;
    float4 Tangent;
    float4 Color;
    float4 JointIndices;
    float4 JointWeights;
};

struct DiagnosticWorkElement
{
    uint4 Value;
};

struct DiagnosticResultElement
{
    uint4 Value;
};

#if defined(TERRAFX_SPIRV)
[[vk::binding(32, 0)]]
#endif
RWStructuredBuffer<DiagnosticVertex> DiagnosticVertices : register(u0, space0);

#if defined(TERRAFX_SPIRV)
[[vk::binding(33, 0)]]
#endif
RWStructuredBuffer<BonePaletteEntry> DiagnosticBonePalette : register(u1, space0);

#if defined(TERRAFX_SPIRV)
[[vk::binding(34, 0)]]
#endif
RWStructuredBuffer<DiagnosticWorkElement> DiagnosticWork : register(u2, space0);

#if defined(TERRAFX_SPIRV)
[[vk::binding(35, 0)]]
#endif
RWStructuredBuffer<DiagnosticResultElement> DiagnosticResults : register(u3, space0);

groupshared uint DegenerateCounts[64];
groupshared uint TestedCounts[64];
groupshared uint InvalidEvaluations[64];

[numthreads(64, 1, 1)]
void CSMain(uint3 groupThreadId : SV_GroupThreadID)
{
    uint lane = groupThreadId.x;
    uint4 parameters = DiagnosticWork[0].Value;
    uint degenerateCount = 0;
    uint testedCount = 0;
    uint invalidEvaluation = 0;

    for (uint index = lane; index < parameters.z; index += 64)
    {
        uint packedIndex = parameters.y + index;
        uint4 packedMembers = DiagnosticWork[1 + (packedIndex / 4)].Value;
        uint vertexIndex = packedMembers[packedIndex % 4];
        DiagnosticVertex vertex = DiagnosticVertices[vertexIndex];
        uint4 jointIndices = (uint4)vertex.JointIndices;
        float4 weights = vertex.JointWeights;
        BonePaletteEntry bone0 = DiagnosticBonePalette[parameters.w + jointIndices.x];
        BonePaletteEntry bone1 = DiagnosticBonePalette[parameters.w + jointIndices.y];
        BonePaletteEntry bone2 = DiagnosticBonePalette[parameters.w + jointIndices.z];
        BonePaletteEntry bone3 = DiagnosticBonePalette[parameters.w + jointIndices.w];
        float3 boneNormal0;
        float3 boneNormal1;
        float3 boneNormal2;
        float3 boneNormal3;
        float3 normal = EvaluateSkinnedNormalBlend(
            float3(vertex.PositionNormalX.w, vertex.NormalYzUv.xy),
            weights,
            bone0,
            bone1,
            bone2,
            bone3,
            boneNormal0,
            boneNormal1,
            boneNormal2,
            boneNormal3);

        if (IsInvalidSkinnedNormal(normal))
        {
            invalidEvaluation = 1;
        }
        else if (IsDegenerateSkinnedNormal(normal))
        {
            degenerateCount++;
        }

        testedCount++;
    }

    DegenerateCounts[lane] = degenerateCount;
    TestedCounts[lane] = testedCount;
    InvalidEvaluations[lane] = invalidEvaluation;
    GroupMemoryBarrierWithGroupSync();

    for (uint stride = 32; stride > 0; stride >>= 1)
    {
        if (lane < stride)
        {
            DegenerateCounts[lane] += DegenerateCounts[lane + stride];
            TestedCounts[lane] += TestedCounts[lane + stride];
            InvalidEvaluations[lane] |= InvalidEvaluations[lane + stride];
        }

        GroupMemoryBarrierWithGroupSync();
    }

    if (lane == 0)
    {
        DiagnosticResults[parameters.x].Value = uint4(
            DegenerateCounts[0],
            TestedCounts[0],
            InvalidEvaluations[0],
            0);
    }
}
