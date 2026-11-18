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

float3 EvaluateSkinnedNormalBlend(
    float3 normal,
    float4 weights,
    BonePaletteEntry bone0,
    BonePaletteEntry bone1,
    BonePaletteEntry bone2,
    BonePaletteEntry bone3,
    out float3 boneNormal0,
    out float3 boneNormal1,
    out float3 boneNormal2,
    out float3 boneNormal3)
{
    boneNormal0 = mul(normal, float3x3(bone0.NormalRow0.xyz, bone0.NormalRow1.xyz, bone0.NormalRow2.xyz));
    boneNormal1 = mul(normal, float3x3(bone1.NormalRow0.xyz, bone1.NormalRow1.xyz, bone1.NormalRow2.xyz));
    boneNormal2 = mul(normal, float3x3(bone2.NormalRow0.xyz, bone2.NormalRow1.xyz, bone2.NormalRow2.xyz));
    boneNormal3 = mul(normal, float3x3(bone3.NormalRow0.xyz, bone3.NormalRow1.xyz, bone3.NormalRow2.xyz));
    return (weights.x * boneNormal0)
        + (weights.y * boneNormal1)
        + (weights.z * boneNormal2)
        + (weights.w * boneNormal3);
}

bool IsInvalidSkinnedNormal(float3 normal)
{
    return !all(isfinite(normal));
}

bool IsDegenerateSkinnedNormal(float3 normal)
{
    return all(isfinite(normal)) && (dot(normal, normal) < 1.0e-12f);
}

float3 SelectDominantSkinnedNormal(
    uint4 jointIndices,
    float4 weights,
    float3 boneNormal0,
    float3 boneNormal1,
    float3 boneNormal2,
    float3 boneNormal3)
{
    float dominantWeight = weights.x;
    uint dominantJoint = jointIndices.x;
    float3 normal = boneNormal0;

    if ((weights.y > dominantWeight)
        || ((weights.y == dominantWeight) && (jointIndices.y < dominantJoint)))
    {
        dominantWeight = weights.y;
        dominantJoint = jointIndices.y;
        normal = boneNormal1;
    }

    if ((weights.z > dominantWeight)
        || ((weights.z == dominantWeight) && (jointIndices.z < dominantJoint)))
    {
        dominantWeight = weights.z;
        dominantJoint = jointIndices.z;
        normal = boneNormal2;
    }

    if ((weights.w > dominantWeight)
        || ((weights.w == dominantWeight) && (jointIndices.w < dominantJoint)))
    {
        normal = boneNormal3;
    }

    return normal;
}
