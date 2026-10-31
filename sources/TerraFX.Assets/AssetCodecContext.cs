// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Assets;

/// <summary>Supplies the trusted content-set context for a contextual asset codec.</summary>
public sealed class AssetCodecContext
{
    internal AssetCodecContext(
        AssetId rootAssetId,
        CompiledContentSchemaRegistry schemas,
        SpatialProfileId profileId,
        SpatialSettings spatialSettings,
        EbinReadLimits readLimits)
    {
        if (!rootAssetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(rootAssetId));
        }

        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(readLimits);
        RootAssetId = rootAssetId;
        Schemas = schemas;
        ProfileId = profileId;
        ReadLimits = readLimits;
        SpatialSettings = spatialSettings;
    }

    /// <summary>Gets the package's selected spatial profile identity.</summary>
    public SpatialProfileId ProfileId { get; }

    /// <summary>Gets the structural limits derived from the package's decoded-asset budget.</summary>
    public EbinReadLimits ReadLimits { get; }

    /// <summary>Gets the root asset being decoded or encoded.</summary>
    public AssetId RootAssetId { get; }

    /// <summary>Gets the application's immutable compiled schema registry.</summary>
    public CompiledContentSchemaRegistry Schemas { get; }

    /// <summary>Gets the package's selected spatial settings.</summary>
    public SpatialSettings SpatialSettings { get; }
}
