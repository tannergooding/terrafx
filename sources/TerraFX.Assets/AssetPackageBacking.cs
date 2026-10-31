// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Assets;

internal sealed class AssetPackageBacking : IAsyncDisposable
{
    private readonly FileStream _stream;

    internal AssetPackageBacking(
        FileStream stream,
        ImmutableArray<AssetPackageEntry> entries,
        SpatialProfileId profileId,
        SpatialSettings spatialSettings,
        AssetStoreRegistration registration,
        long packageByteLength)
    {
        _stream = stream;
        Entries = entries;
        PackageByteLength = packageByteLength;
        ProfileId = profileId;
        Registration = registration;
        SpatialSettings = spatialSettings;
    }

    internal ImmutableArray<AssetPackageEntry> Entries { get; }

    internal long PackageByteLength { get; }

    internal SpatialProfileId ProfileId { get; }

    internal AssetStoreRegistration Registration { get; }

    internal SpatialSettings SpatialSettings { get; }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();

    internal ValueTask ReadExactlyAtAsync(ulong offset, Memory<byte> destination, CancellationToken cancellationToken)
        => AssetPackageReader.ReadExactlyAtAsync(_stream, offset, destination, cancellationToken);
}
