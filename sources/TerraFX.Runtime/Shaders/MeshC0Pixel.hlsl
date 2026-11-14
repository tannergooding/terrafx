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
    float4 LightDirection;
    float4 LightRadiance;
};

#if defined(TERRAFX_SPIRV)
[[vk::binding(0, 1)]]
#endif
cbuffer MaterialData : register(b0, space1)
{
    float4 BaseColor;
    float4 Factors;
    float4 Emissive;
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
    float3 baseColor = BaseColor.rgb;
    float metallic = Factors.x;
    float roughness = Factors.y;
    float3 n = normalize(input.WorldNormal);
    float3 v = normalize(CameraPosition.xyz - input.WorldPosition);
    float3 color = AmbientIrradiance.rgb * baseColor * (1.0f - metallic) / 3.14159265358979323846f;

    if (LightRadiance.w > 0.0f)
    {
        float3 l = normalize(LightDirection.xyz);
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
            color += LightRadiance.rgb * (specular + diffuse) * nDotL;
        }
    }

    color += Emissive.rgb;
    float visibility = input.Position.w > 0.0f ? 1.0f : 0.0f;
    return float4(color, 1.0f) * visibility;
}
