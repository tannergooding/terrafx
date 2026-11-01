// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Assets;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal sealed class RenderMaterialAsset
{
    internal RenderMaterialAsset(
        Guid profileId,
        bool isSprite,
        Guid shaderId,
        Guid passId,
        Guid layoutId,
        ParameterValues parameters,
        MaterialAlphaMode alphaMode,
        float alphaCutoff,
        bool doubleSided,
        bool depthTest,
        bool depthWrite,
        MaterialCullMode cullMode,
        MaterialOutputAlpha outputAlpha,
        ulong requiredCapabilities,
        ImmutableArray<byte> encodedRecord)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(profileId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(shaderId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(passId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(layoutId, Guid.Empty);

        if (encodedRecord.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A Material value requires its exact encoded record.", nameof(encodedRecord));
        }

        ProfileId = profileId;
        IsSprite = isSprite;
        ShaderId = shaderId;
        PassId = passId;
        LayoutId = layoutId;
        Parameters = parameters;
        AlphaMode = alphaMode;
        AlphaCutoff = alphaCutoff;
        DoubleSided = doubleSided;
        DepthTest = depthTest;
        DepthWrite = depthWrite;
        CullMode = cullMode;
        OutputAlpha = outputAlpha;
        RequiredCapabilities = requiredCapabilities;
        EncodedRecord = encodedRecord;
    }

    internal float AlphaCutoff { get; }

    internal MaterialAlphaMode AlphaMode { get; }

    internal MaterialCullMode CullMode { get; }

    internal bool DepthTest { get; }

    internal bool DepthWrite { get; }

    internal bool DoubleSided { get; }

    internal ImmutableArray<byte> EncodedRecord { get; }

    internal bool IsSprite { get; }

    internal Guid LayoutId { get; }

    internal MaterialOutputAlpha OutputAlpha { get; }

    internal ParameterValues Parameters { get; }

    internal Guid PassId { get; }

    internal Guid ProfileId { get; }

    internal ulong RequiredCapabilities { get; }

    internal Guid ShaderId { get; }

    internal enum MaterialAlphaMode : byte
    {
        Opaque,
        Mask,
        Blend,
    }

    internal enum MaterialCullMode : byte
    {
        None,
        Back,
    }

    internal enum MaterialOutputAlpha : byte
    {
        Straight,
        Premultiplied,
    }

    internal readonly record struct ParameterValues(
        Vector4 Color,
        float Metallic,
        float Roughness,
        float NormalScale,
        Vector3 Emissive,
        TextureBinding? SpriteTexture,
        TextureBinding? BaseTexture,
        TextureBinding? PackedMetalRoughOcclusion,
        TextureBinding? NormalTexture,
        TextureBinding? EmissiveTexture);

    internal readonly record struct Sampler(
        byte Minification,
        byte Magnification,
        byte Mip,
        byte AddressU,
        byte AddressV);

    internal readonly record struct TextureBinding(AssetId TextureAssetId, Sampler Sampler, byte UvSet);
}
