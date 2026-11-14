// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal readonly struct RenderMeshC0FrameData
{
    internal const int SizeInBytes = 160;

    private const int AmbientOffset = 96;
    private const int CameraPositionOffset = 80;
    private const int LightDirectionOffset = 112;
    private const int LightParametersOffset = 144;
    private const int LightRadianceOffset = 128;
    private readonly Vector4 _ambient;
    private readonly RenderCameraGpuData _camera;
    private readonly Vector4 _cameraPosition;
    private readonly Vector4 _lightDirection;
    private readonly Vector4 _lightParameters;
    private readonly Vector4 _lightRadiance;

    private RenderMeshC0FrameData(
        RenderCameraGpuData camera,
        Vector4 cameraPosition,
        Vector4 ambient,
        Vector4 lightDirection,
        Vector4 lightRadiance,
        Vector4 lightParameters)
    {
        _camera = camera;
        _cameraPosition = cameraPosition;
        _ambient = ambient;
        _lightDirection = lightDirection;
        _lightRadiance = lightRadiance;
        _lightParameters = lightParameters;
    }

    internal static RenderMeshC0FrameData Create(
        CameraSnapshot camera,
        RenderArea area,
        Vector3 lightPositionOrDirection,
        Vector3 lightColor,
        float lightIntensity,
        bool lightEnabled,
        bool isPointLight,
        float lightRange)
    {
        if (!SpatialMath.TryToFrameRelative(camera.Position, camera.Frame3, out var relativeCameraPosition))
        {
            throw new InvalidOperationException("The camera position is outside its captured spatial frame.");
        }

        if (isPointLight
            ? (!float.IsFinite(lightRange) || (lightRange <= 0.0f))
            : (lightRange != 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(lightRange));
        }

        var radiometricScale = isPointLight
            ? lightIntensity / (4.0f * float.Pi)
            : lightIntensity;
        var radiance = new Vector3(
            lightColor.X * radiometricScale,
            lightColor.Y * radiometricScale,
            lightColor.Z * radiometricScale);

        if (!radiance.IsFinite().All())
        {
            throw new InvalidOperationException("The selected light radiance is not representable.");
        }

        return new RenderMeshC0FrameData(
            RenderCameraGpuData.Create(camera, area),
            new Vector4(relativeCameraPosition, 1.0f),
            new Vector4(0.03f, 0.03f, 0.03f, 1.0f),
            new Vector4(isPointLight ? lightPositionOrDirection : -lightPositionOrDirection, 0.0f),
            new Vector4(radiance, lightEnabled ? 1.0f : 0.0f),
            new Vector4(lightRange, isPointLight ? 1.0f : 0.0f, 0.0f, 0.0f));
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));

    private static void WriteVector4(Span<byte> destination, int offset, Vector4 value)
    {
        WriteSingle(destination, offset, value.X);
        WriteSingle(destination, offset + sizeof(float), value.Y);
        WriteSingle(destination, offset + (2 * sizeof(float)), value.Z);
        WriteSingle(destination, offset + (3 * sizeof(float)), value.W);
    }

    internal void WriteTo(Span<byte> destination)
    {
        if (destination.Length < SizeInBytes)
        {
            throw new ArgumentException("The destination is smaller than the Mesh Frame GPU record.", nameof(destination));
        }

        _camera.WriteTo(destination);
        WriteVector4(destination, CameraPositionOffset, _cameraPosition);
        WriteVector4(destination, AmbientOffset, _ambient);
        WriteVector4(destination, LightDirectionOffset, _lightDirection);
        WriteVector4(destination, LightRadianceOffset, _lightRadiance);
        WriteVector4(destination, LightParametersOffset, _lightParameters);
    }
}
