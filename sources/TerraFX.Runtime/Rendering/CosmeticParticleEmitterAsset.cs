// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Assets;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal sealed class CosmeticParticleEmitterAsset
{
    internal CosmeticParticleEmitterAsset(
        AssetId assetId,
        CosmeticParticleSpace space,
        uint spawnPerTick,
        long lifetimeTicks,
        Vector3 velocityMinimum,
        Vector3 velocityMaximum,
        ImmutableArray<ColorKey> colorKeys,
        ImmutableArray<SizeKey> sizeKeys,
        AssetId atlasAssetId,
        Guid frameId,
        MaterialReference? material,
        ulong seed,
        ulong stream,
        int sortingLayer,
        int sortingOrder,
        ulong layerMask,
        ImmutableArray<byte> encodedRecord)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        if (!atlasAssetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(atlasAssetId));
        }

        if (!Enum.IsDefined(space))
        {
            throw new ArgumentOutOfRangeException(nameof(space));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(spawnPerTick, 131_072u);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lifetimeTicks);
        ArgumentOutOfRangeException.ThrowIfEqual(frameId, Guid.Empty);

        if (colorKeys.IsDefaultOrEmpty || (colorKeys.Length > 16)
            || sizeKeys.IsDefaultOrEmpty || (sizeKeys.Length > 16)
            || encodedRecord.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A cosmetic emitter requires initialized curves and encoded backing.");
        }

        if (material is MaterialReference selectedMaterial
            && (!selectedMaterial.AssetId.IsValid || (selectedMaterial.SchemaMajor is not 1 and not 2)))
        {
            throw new ArgumentException("A cosmetic emitter Material reference must select a valid admitted schema major.", nameof(material));
        }

        AssetId = assetId;
        Space = space;
        SpawnPerTick = spawnPerTick;
        LifetimeTicks = lifetimeTicks;
        VelocityMinimum = velocityMinimum;
        VelocityMaximum = velocityMaximum;
        ColorKeys = colorKeys;
        SizeKeys = sizeKeys;
        AtlasAssetId = atlasAssetId;
        FrameId = frameId;
        Material = material;
        Seed = seed;
        Stream = stream;
        SortingLayer = sortingLayer;
        SortingOrder = sortingOrder;
        LayerMask = layerMask;
        EncodedRecord = encodedRecord;
    }

    internal AssetId AssetId { get; }

    internal AssetId AtlasAssetId { get; }

    internal ImmutableArray<ColorKey> ColorKeys { get; }

    internal ImmutableArray<byte> EncodedRecord { get; }

    internal Guid FrameId { get; }

    internal ulong LayerMask { get; }

    internal long LifetimeTicks { get; }

    internal MaterialReference? Material { get; }

    internal ulong Seed { get; }

    internal ImmutableArray<SizeKey> SizeKeys { get; }

    internal int SortingLayer { get; }

    internal int SortingOrder { get; }

    internal CosmeticParticleSpace Space { get; }

    internal uint SpawnPerTick { get; }

    internal ulong Stream { get; }

    internal Vector3 VelocityMaximum { get; }

    internal Vector3 VelocityMinimum { get; }

    internal readonly record struct ColorKey(float Age, Vector4 Color);

    internal readonly record struct MaterialReference(AssetId AssetId, int SchemaMajor);

    internal readonly record struct SizeKey(float Age, Vector2 Size);
}
