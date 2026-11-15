#if defined(TERRAFX_SPIRV)
#define TERRAFX_MATRIX_MAJOR column_major
#else
#define TERRAFX_MATRIX_MAJOR row_major
#endif

#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 0)]]
#endif
cbuffer FrameData : register(b0, space0)
{
    TERRAFX_MATRIX_MAJOR float4x4 ViewProjection;
    float4 RenderArea;
    float4 CameraPosition;
    float4 AmbientIrradiance;
    uint4 PointLightCount;
    float4 CameraForward;
    float4 CascadeEnds;
    float4 ShadowViewProjectionRows[16];
    float4 SpotShadowViewProjectionRows[16];
};

#if defined(TERRAFX_SPIRV)
[[vk::binding(1, 0)]]
#endif
cbuffer PointLightData : register(b1, space0)
{
    float4 PointLightPositionRange[256];
    float4 PointLightRadiance[256];
};

#if defined(TERRAFX_SPIRV)
[[vk::binding(2, 0)]]
#endif
cbuffer DirectionalLightData : register(b2, space0)
{
    uint4 DirectionalLightCount;
    float4 DirectionalLightDirectionAndShadow[256];
    float4 DirectionalLightRadiance[256];
};

#if defined(TERRAFX_SPIRV)
[[vk::binding(3, 0)]]
#endif
cbuffer SpotLightData : register(b3, space0)
{
    uint4 SpotLightCount;
    float4 SpotLightPositionRange[256];
    float4 SpotLightDirectionOuterCosine[256];
    float4 SpotLightRadiance[256];
    float4 SpotLightInnerCosineShadowSlot[256];
};

#if defined(TERRAFX_SPIRV)
[[vk::binding(16, 0)]]
#endif
Texture2D<float> ShadowAtlas : register(t0, space0);

#if defined(TERRAFX_SPIRV)
[[vk::binding(40, 0)]]
#endif
SamplerState ShadowAtlasSampler : register(s0, space0);

#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 1)]]
#endif
cbuffer MaterialData : register(b0, space1)
{
    float4 BaseColor;
    float4 Factors;
    float4 Emissive;
    float4 RenderOptions;
    float4 NormalOptions;
};

#if defined(TERRAFX_SPIRV)
[[vk::binding(16, 1)]]
#endif
Texture2D<float4> BaseColorTexture : register(t0, space1);

#if defined(TERRAFX_SPIRV)
[[vk::binding(17, 1)]]
#endif
Texture2D<float4> EmissiveTexture : register(t1, space1);

#if defined(TERRAFX_SPIRV)
[[vk::binding(18, 1)]]
#endif
Texture2D<float4> PackedMetalRoughOcclusionTexture : register(t2, space1);

#if defined(TERRAFX_SPIRV)
[[vk::binding(40, 1)]]
#endif
SamplerState BaseColorSampler : register(s0, space1);

#if defined(TERRAFX_SPIRV)
[[vk::binding(41, 1)]]
#endif
SamplerState EmissiveSampler : register(s1, space1);

#if defined(TERRAFX_SPIRV)
[[vk::binding(42, 1)]]
#endif
SamplerState PackedMetalRoughOcclusionSampler : register(s2, space1);

#if defined(TERRAFX_SPIRV)
[[vk::binding(19, 1)]]
#endif
Texture2D<float4> NormalTexture : register(t3, space1);

#if defined(TERRAFX_SPIRV)
[[vk::binding(43, 1)]]
#endif
SamplerState NormalSampler : register(s3, space1);

struct PixelInput
{
    float4 Position : SV_Position;
#if defined(TERRAFX_SPIRV)
    [[vk::location(0)]]
#endif
    float3 WorldPosition : TEXCOORD0;
#if defined(TERRAFX_SPIRV)
    [[vk::location(1)]]
#endif
    float3 WorldNormal : TEXCOORD1;
#if defined(TERRAFX_SPIRV)
    [[vk::location(2)]]
#endif
    float2 Uv0 : TEXCOORD2;
#if defined(TERRAFX_SPIRV)
    [[vk::location(3)]]
#endif
    float4 WorldTangent : TEXCOORD3;
#if defined(TERRAFX_SPIRV)
    [[vk::location(4)]]
#endif
    float4 Color : TEXCOORD4;
};

float4 TransformShadowPosition(float3 worldPosition, uint cascadeIndex)
{
    uint rowOffset = cascadeIndex * 4;
    float4 position = float4(worldPosition, 1.0f);
    float4 row0 = ShadowViewProjectionRows[rowOffset];
    float4 row1 = ShadowViewProjectionRows[rowOffset + 1];
    float4 row2 = ShadowViewProjectionRows[rowOffset + 2];
    float4 row3 = ShadowViewProjectionRows[rowOffset + 3];
    return float4(
        dot(position, float4(row0.x, row1.x, row2.x, row3.x)),
        dot(position, float4(row0.y, row1.y, row2.y, row3.y)),
        dot(position, float4(row0.z, row1.z, row2.z, row3.z)),
        dot(position, float4(row0.w, row1.w, row2.w, row3.w)));
}

float4 TransformSpotShadowPosition(float3 worldPosition, uint spotIndex)
{
    uint rowOffset = 16 + (spotIndex * 4);
    float4 position = float4(worldPosition, 1.0f);
    float4 row0 = SpotShadowViewProjectionRows[rowOffset];
    float4 row1 = SpotShadowViewProjectionRows[rowOffset + 1];
    float4 row2 = SpotShadowViewProjectionRows[rowOffset + 2];
    float4 row3 = SpotShadowViewProjectionRows[rowOffset + 3];
    return float4(
        dot(position, float4(row0.x, row1.x, row2.x, row3.x)),
        dot(position, float4(row0.y, row1.y, row2.y, row3.y)),
        dot(position, float4(row0.z, row1.z, row2.z, row3.z)),
        dot(position, float4(row0.w, row1.w, row2.w, row3.w)));
}

float SampleShadowCell(
    float4 lightPosition,
    uint cellIndex,
    float depth,
    float depthDerivativeX,
    float depthDerivativeY)
{
    if (lightPosition.w <= 0.0f)
    {
        return 1.0f;
    }

    float3 lightNdc = lightPosition.xyz / lightPosition.w;

    if ((lightNdc.x < -1.0f)
        || (lightNdc.x > 1.0f)
        || (lightNdc.y < -1.0f)
        || (lightNdc.y > 1.0f)
        || (lightNdc.z < 0.0f)
        || (lightNdc.z > 1.0f))
    {
        return 1.0f;
    }

    float receiverBias = clamp(
        0.001f + max(abs(depthDerivativeX), abs(depthDerivativeY)),
        0.0f,
        0.05f);
    float2 localUv = float2((lightNdc.x * 0.5f) + 0.5f, 0.5f - (lightNdc.y * 0.5f));
    int2 cellOrigin = int2((cellIndex % 4) * 1024, (cellIndex / 4) * 1024);
    int2 centerPixel = cellOrigin + (int2)floor(localUv * 1024.0f);
    int2 minimumPixel = cellOrigin;
    int2 maximumPixel = cellOrigin + 1023;
    float visibility = 0.0f;

    for (int offsetY = -1; offsetY <= 1; offsetY++)
    {
        for (int offsetX = -1; offsetX <= 1; offsetX++)
        {
            int2 samplePixel = clamp(centerPixel + int2(offsetX, offsetY), minimumPixel, maximumPixel);
            float2 sampleUv = (float2(samplePixel) + 0.5f) / 4096.0f;
            float storedDepth = ShadowAtlas.SampleLevel(ShadowAtlasSampler, sampleUv, 0.0f);
            visibility += (depth + receiverBias) >= storedDepth ? 1.0f : 0.0f;
        }
    }

    return visibility / 9.0f;
}

float4 PSMain(PixelInput input) : SV_Target0
{
    float3 baseColor = BaseColor.rgb * input.Color.rgb;
    float alpha = BaseColor.a * input.Color.a;

    if (Factors.z > 0.0f)
    {
        float4 baseColorSample = BaseColorTexture.Sample(BaseColorSampler, input.Uv0);
        baseColor *= baseColorSample.rgb;
        alpha *= baseColorSample.a;
    }

#if defined(TERRAFX_ALPHA_MASK)
    if (alpha < RenderOptions.x)
    {
        discard;
    }
#endif

    float3 packedMetalRoughOcclusion = float3(1.0f, 1.0f, 1.0f);

    if (Emissive.w > 0.0f)
    {
        packedMetalRoughOcclusion = PackedMetalRoughOcclusionTexture.Sample(
            PackedMetalRoughOcclusionSampler,
            input.Uv0).rgb;
    }

    float metallic = Factors.x * packedMetalRoughOcclusion.b;
    float roughness = max(Factors.y * packedMetalRoughOcclusion.g, 0.045f);
    float occlusion = packedMetalRoughOcclusion.r;
    float3 n = normalize(input.WorldNormal);

    if (NormalOptions.y > 0.5f)
    {
        float3 t = normalize(input.WorldTangent.xyz - (n * dot(n, input.WorldTangent.xyz)));
        float3 b = normalize(cross(n, t)) * input.WorldTangent.w;
        float3 tangentNormal = (NormalTexture.Sample(NormalSampler, input.Uv0).xyz * 2.0f) - 1.0f;
        tangentNormal.xy *= NormalOptions.x;
        n = normalize((t * tangentNormal.x) + (b * tangentNormal.y) + (n * tangentNormal.z));
    }

    float3 v = normalize(CameraPosition.xyz - input.WorldPosition);
    float3 color = AmbientIrradiance.rgb * baseColor * (1.0f - metallic) * occlusion / 3.14159265358979323846f;
    float4 shadowPosition0 = TransformShadowPosition(input.WorldPosition, 0);
    float4 shadowPosition1 = TransformShadowPosition(input.WorldPosition, 1);
    float4 shadowPosition2 = TransformShadowPosition(input.WorldPosition, 2);
    float4 shadowPosition3 = TransformShadowPosition(input.WorldPosition, 3);
    float shadowDepth0 = shadowPosition0.z / shadowPosition0.w;
    float shadowDepth1 = shadowPosition1.z / shadowPosition1.w;
    float shadowDepth2 = shadowPosition2.z / shadowPosition2.w;
    float shadowDepth3 = shadowPosition3.z / shadowPosition3.w;
    float shadowDepthDerivativeX0 = ddx(shadowDepth0);
    float shadowDepthDerivativeY0 = ddy(shadowDepth0);
    float shadowDepthDerivativeX1 = ddx(shadowDepth1);
    float shadowDepthDerivativeY1 = ddy(shadowDepth1);
    float shadowDepthDerivativeX2 = ddx(shadowDepth2);
    float shadowDepthDerivativeY2 = ddy(shadowDepth2);
    float shadowDepthDerivativeX3 = ddx(shadowDepth3);
    float shadowDepthDerivativeY3 = ddy(shadowDepth3);
    float directionalShadowVisibility = 1.0f;

    if ((DirectionalLightCount.x > 0) && (DirectionalLightDirectionAndShadow[0].w > 0.5f))
    {
        float viewDepth = dot(input.WorldPosition - CameraPosition.xyz, CameraForward.xyz);

        if (viewDepth <= CascadeEnds.w)
        {
            uint cascadeIndex = viewDepth <= CascadeEnds.x
                ? 0
                : viewDepth <= CascadeEnds.y
                    ? 1
                    : viewDepth <= CascadeEnds.z
                        ? 2
                        : 3;
            directionalShadowVisibility = cascadeIndex == 0
                ? SampleShadowCell(
                    shadowPosition0,
                    cascadeIndex,
                    shadowDepth0,
                    shadowDepthDerivativeX0,
                    shadowDepthDerivativeY0)
                : cascadeIndex == 1
                    ? SampleShadowCell(
                        shadowPosition1,
                        cascadeIndex,
                        shadowDepth1,
                        shadowDepthDerivativeX1,
                        shadowDepthDerivativeY1)
                    : cascadeIndex == 2
                        ? SampleShadowCell(
                            shadowPosition2,
                            cascadeIndex,
                            shadowDepth2,
                            shadowDepthDerivativeX2,
                            shadowDepthDerivativeY2)
                        : SampleShadowCell(
                            shadowPosition3,
                            cascadeIndex,
                            shadowDepth3,
                            shadowDepthDerivativeX3,
                            shadowDepthDerivativeY3);
        }
    }

    for (uint lightIndex = 0; lightIndex < PointLightCount.x; lightIndex++)
    {
        float4 positionRange = PointLightPositionRange[lightIndex];
        float3 toLight = positionRange.xyz - input.WorldPosition;
        float distanceSquared = dot(toLight, toLight);
        float lightDistance = sqrt(distanceSquared);
        float3 l = toLight / max(lightDistance, 1.0e-5f);
        float rangeRatio = lightDistance / positionRange.w;
        float rangeFactor = max(0.0f, 1.0f - pow(rangeRatio, 4.0f));
        float attenuation = (rangeFactor * rangeFactor) / max(distanceSquared, 0.0001f);
        float nDotL = saturate(dot(n, l));
        float nDotV = saturate(dot(n, v));

        if ((nDotL > 0.0f) && (nDotV > 0.0f))
        {
            float3 h = normalize(l + v);
            float nDotH = saturate(dot(n, h));
            float vDotH = saturate(dot(v, h));
            float alpha = roughness * roughness;
            float alphaSquared = alpha * alpha;
            float distributionDenominator = nDotH * nDotH * (alphaSquared - 1.0f) + 1.0f;
            float distribution = alphaSquared / (3.14159265358979323846f * distributionDenominator * distributionDenominator);
            float nDotLRoot = sqrt(nDotV * nDotV * (1.0f - alphaSquared) + alphaSquared);
            float nDotVRoot = sqrt(nDotL * nDotL * (1.0f - alphaSquared) + alphaSquared);
            float visibilityDenominator = nDotL * nDotLRoot + nDotV * nDotVRoot;
            float visibility = 0.5f / max(visibilityDenominator, 1.0e-5f);
            float3 f0 = lerp(float3(0.04f, 0.04f, 0.04f), baseColor, metallic);
            float3 fresnel = f0 + (1.0f - f0) * pow(1.0f - vDotH, 5.0f);
            float3 specular = distribution * fresnel * visibility;
            float3 diffuse = (1.0f - metallic) * (1.0f - fresnel) * baseColor / 3.14159265358979323846f;
            color += PointLightRadiance[lightIndex].rgb * (specular + diffuse) * nDotL * attenuation;
        }
    }

    for (uint lightIndex = 0; lightIndex < SpotLightCount.x; lightIndex++)
    {
        float4 positionRange = SpotLightPositionRange[lightIndex];
        float4 directionOuterCosine = SpotLightDirectionOuterCosine[lightIndex];
        float4 innerCosineShadowSlot = SpotLightInnerCosineShadowSlot[lightIndex];
        float3 toLight = positionRange.xyz - input.WorldPosition;
        float distanceSquared = dot(toLight, toLight);
        float lightDistance = sqrt(distanceSquared);
        float3 l = toLight / max(lightDistance, 1.0e-5f);
        float rangeRatio = lightDistance / positionRange.w;
        float rangeFactor = max(0.0f, 1.0f - pow(rangeRatio, 4.0f));
        float attenuation = (rangeFactor * rangeFactor) / max(distanceSquared, 0.0001f);
        float cosine = dot(-l, directionOuterCosine.xyz);
        float coneFactor = innerCosineShadowSlot.x == directionOuterCosine.w
            ? cosine >= innerCosineShadowSlot.x ? 1.0f : 0.0f
            : saturate((cosine - directionOuterCosine.w)
                / (innerCosineShadowSlot.x - directionOuterCosine.w));
        coneFactor = coneFactor * coneFactor * (3.0f - (2.0f * coneFactor));
        uint spotShadowIndex = (uint)max(innerCosineShadowSlot.y - 1.0f, 0.0f);
        float4 spotShadowPosition = TransformSpotShadowPosition(input.WorldPosition, spotShadowIndex);
        float spotShadowDepth = spotShadowPosition.z / spotShadowPosition.w;
        float spotShadowDepthDerivativeX = ddx(spotShadowDepth);
        float spotShadowDepthDerivativeY = ddy(spotShadowDepth);
        float spotShadowVisibility = (innerCosineShadowSlot.y > 0.5f)
            ? SampleShadowCell(
                spotShadowPosition,
                4 + spotShadowIndex,
                spotShadowDepth,
                spotShadowDepthDerivativeX,
                spotShadowDepthDerivativeY)
            : 1.0f;
        float nDotL = saturate(dot(n, l));
        float nDotV = saturate(dot(n, v));

        if ((coneFactor > 0.0f) && (nDotL > 0.0f) && (nDotV > 0.0f))
        {
            float3 h = normalize(l + v);
            float nDotH = saturate(dot(n, h));
            float vDotH = saturate(dot(v, h));
            float alpha = roughness * roughness;
            float alphaSquared = alpha * alpha;
            float distributionDenominator = nDotH * nDotH * (alphaSquared - 1.0f) + 1.0f;
            float distribution = alphaSquared / (3.14159265358979323846f * distributionDenominator * distributionDenominator);
            float nDotLRoot = sqrt(nDotV * nDotV * (1.0f - alphaSquared) + alphaSquared);
            float nDotVRoot = sqrt(nDotL * nDotL * (1.0f - alphaSquared) + alphaSquared);
            float visibilityDenominator = nDotL * nDotLRoot + nDotV * nDotVRoot;
            float visibility = 0.5f / max(visibilityDenominator, 1.0e-5f);
            float3 f0 = lerp(float3(0.04f, 0.04f, 0.04f), baseColor, metallic);
            float3 fresnel = f0 + (1.0f - f0) * pow(1.0f - vDotH, 5.0f);
            float3 specular = distribution * fresnel * visibility;
            float3 diffuse = (1.0f - metallic) * (1.0f - fresnel) * baseColor / 3.14159265358979323846f;
            color += SpotLightRadiance[lightIndex].rgb
                * (specular + diffuse)
                * nDotL
                * attenuation
                * coneFactor
                * spotShadowVisibility;
        }
    }

    for (uint lightIndex = 0; lightIndex < DirectionalLightCount.x; lightIndex++)
    {
        float4 directionAndShadow = DirectionalLightDirectionAndShadow[lightIndex];
        float3 l = normalize(-directionAndShadow.xyz);
        float nDotL = saturate(dot(n, l));
        float nDotV = saturate(dot(n, v));

        if ((nDotL > 0.0f) && (nDotV > 0.0f))
        {
            float3 h = normalize(l + v);
            float nDotH = saturate(dot(n, h));
            float vDotH = saturate(dot(v, h));
            float alpha = roughness * roughness;
            float alphaSquared = alpha * alpha;
            float distributionDenominator = nDotH * nDotH * (alphaSquared - 1.0f) + 1.0f;
            float distribution = alphaSquared / (3.14159265358979323846f * distributionDenominator * distributionDenominator);
            float nDotLRoot = sqrt(nDotV * nDotV * (1.0f - alphaSquared) + alphaSquared);
            float nDotVRoot = sqrt(nDotL * nDotL * (1.0f - alphaSquared) + alphaSquared);
            float visibilityDenominator = nDotL * nDotLRoot + nDotV * nDotVRoot;
            float brdfVisibility = 0.5f / max(visibilityDenominator, 1.0e-5f);
            float3 f0 = lerp(float3(0.04f, 0.04f, 0.04f), baseColor, metallic);
            float3 fresnel = f0 + (1.0f - f0) * pow(1.0f - vDotH, 5.0f);
            float3 specular = distribution * fresnel * brdfVisibility;
            float3 diffuse = (1.0f - metallic) * (1.0f - fresnel) * baseColor / 3.14159265358979323846f;
            float shadowVisibility = (lightIndex == 0) ? directionalShadowVisibility : 1.0f;
            color += DirectionalLightRadiance[lightIndex].rgb
                * (specular + diffuse)
                * nDotL
                * shadowVisibility;
        }
    }

    float3 emissive = Emissive.rgb;

    if (Factors.w > 0.0f)
    {
        emissive *= EmissiveTexture.Sample(EmissiveSampler, input.Uv0).rgb;
    }

    color += emissive;
    float visibility = input.Position.w > 0.0f ? 1.0f : 0.0f;

    if (RenderOptions.z > 0.5f)
    {
        return float4(color * alpha, alpha) * visibility;
    }

    return float4(color, 1.0f) * visibility;
}
