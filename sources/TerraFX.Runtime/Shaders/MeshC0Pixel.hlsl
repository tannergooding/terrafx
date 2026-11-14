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
};

float4 PSMain(PixelInput input) : SV_Target0
{
#if defined(TERRAFX_ALPHA_MASK)
    if (BaseColor.a < RenderOptions.x)
    {
        discard;
    }
#endif

    float3 baseColor = BaseColor.rgb;
    float metallic = Factors.x;
    float roughness = Factors.y;
    float3 n = normalize(input.WorldNormal);
    float3 v = normalize(CameraPosition.xyz - input.WorldPosition);
    float3 color = AmbientIrradiance.rgb * baseColor * (1.0f - metallic) / 3.14159265358979323846f;

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

    color += Emissive.rgb;
    float visibility = input.Position.w > 0.0f ? 1.0f : 0.0f;

    if (RenderOptions.z > 0.5f)
    {
        return float4(color * BaseColor.a, BaseColor.a) * visibility;
    }

    return float4(color, 1.0f) * visibility;
}
