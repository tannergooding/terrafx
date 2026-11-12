// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal static class RenderingKernels
{
    private const float MinimumRoughness = 0.045f;
    private const int ShadowAtlasCellSize = 1024;
    private const int ShadowAtlasResolution = 4096;

    internal static void ComputeCascadeSplits(float near, float end, bool perspective, Span<float> splits)
    {
        if (splits.Length < 5)
        {
            throw new ArgumentException("Five split values are required.", nameof(splits));
        }

        if (!float.IsFinite(near) || (perspective && (near <= 0.0f)))
        {
            throw new ArgumentOutOfRangeException(nameof(near));
        }

        if (!float.IsFinite(end) || (end <= near))
        {
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        Span<float> computed = stackalloc float[5];
        computed[0] = near;

        for (var splitIndex = 1; splitIndex < 4; splitIndex++)
        {
            var fraction = splitIndex * 0.25f;

            if (perspective)
            {
                var logarithmic = near * MathF.Pow(end / near, fraction);
                var linear = near + ((end - near) * fraction);
                computed[splitIndex] = (0.5f * logarithmic) + (0.5f * linear);
            }
            else
            {
                computed[splitIndex] = near + ((end - near) * fraction);
            }
        }

        computed[4] = end;

        for (var splitIndex = 0; splitIndex < 5; splitIndex++)
        {
            if (!float.IsFinite(computed[splitIndex])
                || ((splitIndex != 0) && (computed[splitIndex] <= computed[splitIndex - 1])))
            {
                throw new ArgumentOutOfRangeException(nameof(end));
            }
        }

        computed.CopyTo(splits);
    }

    internal static float ComputeReceiverBias(
        float constantDepthBias,
        float slopeScale,
        float maxDepthBias,
        float depthDerivativeX,
        float depthDerivativeY)
    {
        if (!float.IsFinite(constantDepthBias) || (constantDepthBias < 0.0f) || (constantDepthBias > 0.05f))
        {
            throw new ArgumentOutOfRangeException(nameof(constantDepthBias));
        }

        if (!float.IsFinite(slopeScale) || (slopeScale < 0.0f) || (slopeScale > 4.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(slopeScale));
        }

        if (!float.IsFinite(maxDepthBias) || (maxDepthBias < constantDepthBias) || (maxDepthBias > 0.1f))
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepthBias));
        }

        if (!float.IsFinite(depthDerivativeX) || !float.IsFinite(depthDerivativeY))
        {
            throw new ArgumentOutOfRangeException(nameof(depthDerivativeX));
        }

        var maximumDerivative = MathF.Max(MathF.Abs(depthDerivativeX), MathF.Abs(depthDerivativeY));
        return Math.Clamp(constantDepthBias + (slopeScale * maximumDerivative), 0.0f, maxDepthBias);
    }

    internal static Vector3 DecodeSrgb(Vector3 encoded)
    {
        EnsureFinite(encoded, nameof(encoded));
        return new Vector3(DecodeSrgb(encoded.X), DecodeSrgb(encoded.Y), DecodeSrgb(encoded.Z));
    }

    internal static float DecodeSrgb(float encoded)
    {
        if (!float.IsFinite(encoded) || (encoded < 0.0f) || (encoded > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(encoded));
        }

        return encoded <= 0.04045f
            ? encoded / 12.92f
            : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);
    }

    internal static float EncodeSrgb(float linear)
    {
        if (!float.IsFinite(linear) || (linear < 0.0f) || (linear > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(linear));
        }

        return linear <= 0.0031308f
            ? linear * 12.92f
            : (1.055f * MathF.Pow(linear, 1.0f / 2.4f)) - 0.055f;
    }

    internal static Vector3 EvaluateAmbientDiffuse(Vector3 baseColor, float metallic, float occlusion, Vector3 irradiance)
    {
        EnsureNonnegativeFinite(baseColor, nameof(baseColor));
        EnsureNonnegativeFinite(irradiance, nameof(irradiance));
        EnsureUnitInterval(metallic, nameof(metallic));
        EnsureUnitInterval(occlusion, nameof(occlusion));
        var result = irradiance * baseColor * ((1.0f - metallic) * occlusion / MathF.PI);

        if (!result.IsFinite().All())
        {
            throw new ArgumentException("The ambient contribution is not representable.");
        }

        return result;
    }

    internal static Vector3 EvaluateBrdf(
        Vector3 normal,
        Vector3 towardLight,
        Vector3 towardViewer,
        Vector3 baseColor,
        float metallic,
        float roughness,
        Vector3 incomingRadiance)
    {
        EnsureNonnegativeFinite(baseColor, nameof(baseColor));
        EnsureNonnegativeFinite(incomingRadiance, nameof(incomingRadiance));
        EnsureUnitInterval(metallic, nameof(metallic));

        if (!float.IsFinite(roughness) || (roughness < MinimumRoughness) || (roughness > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(roughness));
        }

        normal = NormalizeDirection(normal, nameof(normal));
        towardLight = NormalizeDirection(towardLight, nameof(towardLight));
        towardViewer = NormalizeDirection(towardViewer, nameof(towardViewer));

        var normalDotLight = Math.Clamp(Vector3.Dot(normal, towardLight), 0.0f, 1.0f);
        var normalDotViewer = Math.Clamp(Vector3.Dot(normal, towardViewer), 0.0f, 1.0f);

        if ((normalDotLight == 0.0f) || (normalDotViewer == 0.0f))
        {
            return Vector3.Zero;
        }

        var halfVector = NormalizeDirection(towardLight + towardViewer, nameof(towardViewer));
        var normalDotHalf = Math.Clamp(Vector3.Dot(normal, halfVector), 0.0f, 1.0f);
        var viewerDotHalf = Math.Clamp(Vector3.Dot(towardViewer, halfVector), 0.0f, 1.0f);
        var alpha = roughness * roughness;
        var alphaSquared = alpha * alpha;
        var normalDotHalfSquared = normalDotHalf * normalDotHalf;
        var distributionDenominator = (normalDotHalfSquared * (alphaSquared - 1.0f)) + 1.0f;
        var distribution = alphaSquared / (MathF.PI * distributionDenominator * distributionDenominator);
        var lightRoot = MathF.Sqrt((normalDotViewer * normalDotViewer * (1.0f - alphaSquared)) + alphaSquared);
        var viewerRoot = MathF.Sqrt((normalDotLight * normalDotLight * (1.0f - alphaSquared)) + alphaSquared);
        var visibilityDenominator = MathF.Max(
            (normalDotLight * lightRoot) + (normalDotViewer * viewerRoot),
            1.0e-5f);
        var visibility = 0.5f / visibilityDenominator;
        var fresnelFactor = Pow5(1.0f - viewerDotHalf);
        var f0 = new Vector3(0.04f) + ((baseColor - new Vector3(0.04f)) * metallic);
        var fresnel = f0 + ((Vector3.One - f0) * fresnelFactor);
        var specular = fresnel * (distribution * visibility);
        var diffuse = (Vector3.One - fresnel) * baseColor * ((1.0f - metallic) / MathF.PI);
        var result = incomingRadiance * (specular + diffuse) * normalDotLight;

        if (!result.IsFinite().All() || (result.X < 0.0f) || (result.Y < 0.0f) || (result.Z < 0.0f))
        {
            throw new ArgumentException("The selected BRDF inputs produced a nonfinite or negative contribution.");
        }

        return result;
    }

    internal static float EvaluatePointIntensity(float fluxLumens)
    {
        if (!float.IsFinite(fluxLumens) || (fluxLumens < 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(fluxLumens));
        }

        return fluxLumens / (4.0f * MathF.PI);
    }

    internal static float EvaluatePointRangeAttenuation(float distance, float range)
    {
        if (!float.IsFinite(distance) || (distance < 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(distance));
        }

        if (!float.IsFinite(range) || (range <= 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }

        var distanceOverRange = distance / range;
        var fourthPower = distanceOverRange * distanceOverRange;
        fourthPower *= fourthPower;
        var rangeTerm = MathF.Max(0.0f, 1.0f - fourthPower);
        var rangeFactor = rangeTerm * rangeTerm;
        var distanceSquared = distance * distance;
        var inverseSquare = 1.0f / MathF.Max(distanceSquared, 0.0001f);
        return inverseSquare * rangeFactor;
    }

    internal static float EvaluateSpotFactor(float cosine, float innerHalfAngle, float outerHalfAngle)
    {
        if (!float.IsFinite(cosine) || (cosine < -1.0f) || (cosine > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(cosine));
        }

        if (!float.IsFinite(innerHalfAngle) || (innerHalfAngle <= 0.0f) || (innerHalfAngle > outerHalfAngle))
        {
            throw new ArgumentOutOfRangeException(nameof(innerHalfAngle));
        }

        if (!float.IsFinite(outerHalfAngle) || (outerHalfAngle >= (MathF.PI * 0.5f)))
        {
            throw new ArgumentOutOfRangeException(nameof(outerHalfAngle));
        }

        var innerCosine = MathF.Cos(innerHalfAngle);

        if (innerHalfAngle == outerHalfAngle)
        {
            return cosine >= innerCosine ? 1.0f : 0.0f;
        }

        var outerCosine = MathF.Cos(outerHalfAngle);

        if (innerCosine == outerCosine)
        {
            throw new ArgumentException("Distinct spot angles have no representable cosine interval.", nameof(outerHalfAngle));
        }

        var interpolation = Math.Clamp((cosine - outerCosine) / (innerCosine - outerCosine), 0.0f, 1.0f);
        return interpolation * interpolation * (3.0f - (2.0f * interpolation));
    }

    internal static (Matrix4x4 View, Vector2 Center, float Extent, float Near, float Far) FitDirectionalShadow(ReadOnlySpan<Vector3> worldCorners, Vector3 lightDirection)
    {
        if (worldCorners.Length != 8)
        {
            throw new ArgumentException("A directional cascade requires exactly eight world-space corners.", nameof(worldCorners));
        }

        GetShadowViewBasis(lightDirection, out _, out var up, out var forward);
        var view = Matrix4x4.CreateLookTo(Vector3.Zero, forward, up);
        var firstCorner = worldCorners[0];
        EnsureFinite(firstCorner, nameof(worldCorners));
        var firstLightCorner = Vector3.TransformPoint(firstCorner, view);
        var minimumX = firstLightCorner.X;
        var maximumX = firstLightCorner.X;
        var minimumY = firstLightCorner.Y;
        var maximumY = firstLightCorner.Y;
        var minimumZ = firstLightCorner.Z;
        var maximumZ = firstLightCorner.Z;

        for (var cornerIndex = 1; cornerIndex < worldCorners.Length; cornerIndex++)
        {
            var corner = worldCorners[cornerIndex];
            EnsureFinite(corner, nameof(worldCorners));
            var lightCorner = Vector3.TransformPoint(corner, view);

            if (!lightCorner.IsFinite().All())
            {
                throw new ArgumentOutOfRangeException(nameof(worldCorners));
            }

            minimumX = MathF.Min(minimumX, lightCorner.X);
            maximumX = MathF.Max(maximumX, lightCorner.X);
            minimumY = MathF.Min(minimumY, lightCorner.Y);
            maximumY = MathF.Max(maximumY, lightCorner.Y);
            minimumZ = MathF.Min(minimumZ, lightCorner.Z);
            maximumZ = MathF.Max(maximumZ, lightCorner.Z);
        }

        if (!firstLightCorner.IsFinite().All())
        {
            throw new ArgumentOutOfRangeException(nameof(worldCorners));
        }

        var spanX = maximumX - minimumX;
        var spanY = maximumY - minimumY;
        var maximumSpan = MathF.Max(spanX, spanY);

        if (!float.IsFinite(maximumSpan) || (maximumSpan <= 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(worldCorners));
        }

        // Reserve one texel per edge and up to half a texel for center snapping.
        var extent = maximumSpan / (1.0f - (3.0f / ShadowAtlasCellSize));
        var texelWorldSize = extent / ShadowAtlasCellSize;
        var centerX = MathF.Round((minimumX + (spanX * 0.5f)) / texelWorldSize, MidpointRounding.ToEven) * texelWorldSize;
        var centerY = MathF.Round((minimumY + (spanY * 0.5f)) / texelWorldSize, MidpointRounding.ToEven) * texelWorldSize;
        var near = minimumZ - 10.0f;

        if (!float.IsFinite(extent)
            || (extent <= 0.0f)
            || !float.IsFinite(centerX)
            || !float.IsFinite(centerY)
            || !float.IsFinite(near)
            || !float.IsFinite(maximumZ)
            || (maximumZ <= near))
        {
            throw new ArgumentOutOfRangeException(nameof(worldCorners));
        }

        return (view, new Vector2(centerX, centerY), extent, near, maximumZ);
    }

    internal static void GetShadowAtlasCellOrigin(int cellIndex, out int x, out int y)
    {
        if ((uint)cellIndex >= 16u)
        {
            throw new ArgumentOutOfRangeException(nameof(cellIndex));
        }

        x = cellIndex % 4 * ShadowAtlasCellSize;
        y = cellIndex / 4 * ShadowAtlasCellSize;
    }

    internal static Vector2 GetShadowPcfSampleUv(Vector2 centerUv, int cellIndex, int offsetX, int offsetY)
    {
        if (!centerUv.IsFinite().All())
        {
            throw new ArgumentOutOfRangeException(nameof(centerUv));
        }

        if (offsetX is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetX));
        }

        if (offsetY is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetY));
        }

        GetShadowAtlasCellOrigin(cellIndex, out var cellX, out var cellY);
        var minimumX = (cellX + 0.5f) / ShadowAtlasResolution;
        var minimumY = (cellY + 0.5f) / ShadowAtlasResolution;
        var maximumX = (cellX + ShadowAtlasCellSize - 0.5f) / ShadowAtlasResolution;
        var maximumY = (cellY + ShadowAtlasCellSize - 0.5f) / ShadowAtlasResolution;
        var texel = 1.0f / ShadowAtlasResolution;
        return new Vector2(
            Math.Clamp(centerUv.X + (offsetX * texel), minimumX, maximumX),
            Math.Clamp(centerUv.Y + (offsetY * texel), minimumY, maximumY));
    }

    internal static void GetShadowViewBasis(Vector3 lightDirection, out Vector3 right, out Vector3 up, out Vector3 forward)
    {
        forward = NormalizeDirection(lightDirection, nameof(lightDirection));
        var xAlignment = MathF.Abs(forward.X);
        var yAlignment = MathF.Abs(forward.Y);
        var zAlignment = MathF.Abs(forward.Z);
        var cardinalUp = (xAlignment <= yAlignment) && (xAlignment <= zAlignment)
            ? Vector3.UnitX
            : (yAlignment <= zAlignment ? Vector3.UnitY : Vector3.UnitZ);
        right = Vector3.Normalize(Vector3.Cross(cardinalUp, forward));
        up = Vector3.Normalize(Vector3.Cross(forward, right));
    }

    internal static Vector2 GetSpriteUv(int vertexIndex, bool flipX, bool flipY)
    {
        var uv = vertexIndex switch {
            0 => new Vector2(0.0f, 1.0f),
            1 => new Vector2(0.0f, 0.0f),
            2 => new Vector2(1.0f, 0.0f),
            3 => new Vector2(1.0f, 1.0f),
            _ => throw new ArgumentOutOfRangeException(nameof(vertexIndex))
        };
        return new Vector2(flipX ? 1.0f - uv.X : uv.X, flipY ? 1.0f - uv.Y : uv.Y);
    }

    internal static Vector3 GetSpriteVertex(int vertexIndex, Vector2 pivot, Vector2 size)
    {
        if (!pivot.IsFinite().All() || (pivot.X < 0.0f) || (pivot.X > 1.0f) || (pivot.Y < 0.0f) || (pivot.Y > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(pivot));
        }

        if (!size.IsFinite().All() || (size.X <= 0.0f) || (size.Y <= 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        var left = -pivot.X * size.X;
        var right = (1.0f - pivot.X) * size.X;
        var bottom = -pivot.Y * size.Y;
        var top = (1.0f - pivot.Y) * size.Y;
        return vertexIndex switch {
            0 => new Vector3(left, bottom, 0.0f),
            1 => new Vector3(left, top, 0.0f),
            2 => new Vector3(right, top, 0.0f),
            3 => new Vector3(right, bottom, 0.0f),
            _ => throw new ArgumentOutOfRangeException(nameof(vertexIndex))
        };
    }

    internal static int GetTransformParity(Matrix4x4 world)
    {
        if (!world.IsFinite().All())
        {
            throw new ArgumentOutOfRangeException(nameof(world));
        }

        var row1 = world.Row1;
        var row2 = world.Row2;
        var row3 = world.Row3;
        var scale1 = MathF.Max(MathF.Abs(row1.X), MathF.Max(MathF.Abs(row1.Y), MathF.Abs(row1.Z)));
        var scale2 = MathF.Max(MathF.Abs(row2.X), MathF.Max(MathF.Abs(row2.Y), MathF.Abs(row2.Z)));
        var scale3 = MathF.Max(MathF.Abs(row3.X), MathF.Max(MathF.Abs(row3.Y), MathF.Abs(row3.Z)));

        if ((scale1 == 0.0f) || (scale2 == 0.0f) || (scale3 == 0.0f))
        {
            throw new ArgumentException("The linear transform is singular.", nameof(world));
        }

        var m11 = row1.X / scale1;
        var m12 = row1.Y / scale1;
        var m13 = row1.Z / scale1;
        var m21 = row2.X / scale2;
        var m22 = row2.Y / scale2;
        var m23 = row2.Z / scale2;
        var m31 = row3.X / scale3;
        var m32 = row3.Y / scale3;
        var m33 = row3.Z / scale3;
        var determinant = (m11 * ((m22 * m33) - (m23 * m32)))
            - (m12 * ((m21 * m33) - (m23 * m31)))
            + (m13 * ((m21 * m32) - (m22 * m31)));

        if (!float.IsFinite(determinant) || (determinant == 0.0f))
        {
            throw new ArgumentException("The linear transform has no representable orientation.", nameof(world));
        }

        return determinant < 0.0f ? -1 : 1;
    }

    internal static bool IsShadowTapLit(float receiverDepth, float receiverBias, float storedDepth)
    {
        if (!float.IsFinite(receiverDepth) || (receiverDepth < 0.0f) || (receiverDepth > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(receiverDepth));
        }

        if (!float.IsFinite(receiverBias) || (receiverBias < 0.0f) || (receiverBias > 0.1f))
        {
            throw new ArgumentOutOfRangeException(nameof(receiverBias));
        }

        if (!float.IsFinite(storedDepth) || (storedDepth < 0.0f) || (storedDepth > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(storedDepth));
        }

        return receiverDepth + receiverBias >= storedDepth;
    }

    internal static Vector4 PremultiplyLinear(Vector4 color)
    {
        if (!color.IsFinite().All() || (color.W < 0.0f) || (color.W > 1.0f)
            || (color.X < 0.0f) || (color.Y < 0.0f) || (color.Z < 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(color));
        }

        return new Vector4(color.X * color.W, color.Y * color.W, color.Z * color.W, color.W);
    }

    internal static Vector3 ToneMapReinhard(Vector3 radiance)
    {
        EnsureNonnegativeFinite(radiance, nameof(radiance));
        return new Vector3(
            radiance.X / (1.0f + radiance.X),
            radiance.Y / (1.0f + radiance.Y),
            radiance.Z / (1.0f + radiance.Z));
    }

    internal static Vector3 TransformNormal(Vector3 normal, Matrix4x4 world)
    {
        EnsureFinite(normal, nameof(normal));

        if (!world.IsFinite().All() || !Matrix4x4.TryInvert(world, out var inverse))
        {
            throw new ArgumentException("The world transform is not finitely invertible.", nameof(world));
        }

        if (!Vector3.TryNormalize(normal, out var normalized))
        {
            throw new ArgumentException("A surface normal must be nonzero.", nameof(normal));
        }

        var transformed = Vector3.TransformDirection(normalized, Matrix4x4.Transpose(inverse));

        if (!Vector3.TryNormalize(transformed, out var result))
        {
            throw new ArgumentException("The transformed surface normal is not representable.", nameof(world));
        }

        return result;
    }

    private static void EnsureFinite(Vector3 value, string parameterName)
    {
        if (!value.IsFinite().All())
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void EnsureNonnegativeFinite(Vector3 value, string parameterName)
    {
        EnsureFinite(value, parameterName);

        if ((value.X < 0.0f) || (value.Y < 0.0f) || (value.Z < 0.0f))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void EnsureUnitInterval(float value, string parameterName)
    {
        if (!float.IsFinite(value) || (value < 0.0f) || (value > 1.0f))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static Vector3 NormalizeDirection(Vector3 value, string parameterName)
    {
        EnsureFinite(value, parameterName);

        if (!Vector3.TryNormalize(value, out var normalized))
        {
            throw new ArgumentException("A direction must be nonzero.", parameterName);
        }

        return normalized;
    }

    private static float Pow5(float value)
    {
        var squared = value * value;
        var fourth = squared * squared;
        return fourth * value;
    }
}
