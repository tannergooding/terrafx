// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Assets;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

/// <summary>Retains one package-backed texture and its uploaded device-generation resource.</summary>
public sealed class GpuResidencyLease : IDisposable
{
    private IDisposable? _packageLease;

    private readonly GpuResidencyResource _resource;

    internal GpuResidencyLease(
        AssetId assetId,
        uint deviceEpoch,
        GpuResidencyResource resource,
        IDisposable packageLease)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(packageLease);

        AssetId = assetId;
        DeviceEpoch = deviceEpoch;
        _resource = resource;
        _packageLease = packageLease;
    }

    /// <summary>Gets the package asset identity.</summary>
    public AssetId AssetId { get; }

    /// <summary>Gets the device generation that owns the uploaded texture.</summary>
    public uint DeviceEpoch { get; }

    internal RenderTextureAsset Asset => GetResource().Asset;

    internal GpuTexture Texture => GetResource().Texture;

    /// <summary>Releases the package pin and revokes the resident texture for new work.</summary>
    public void Dispose()
        => Interlocked.Exchange(ref _packageLease, null)?.Dispose();

    internal GpuResidencyResource TransferResource()
    {
        var packageLease = Interlocked.Exchange(ref _packageLease, null);
        ObjectDisposedException.ThrowIf(packageLease is null, this);

        if (!ReferenceEquals(packageLease, _resource))
        {
            packageLease.Dispose();
            throw new InvalidOperationException("Only a root residency lease can transfer its resource.");
        }

        return _resource;
    }

    private GpuResidencyResource GetResource()
    {
        var packageLease = Volatile.Read(ref _packageLease);
        ObjectDisposedException.ThrowIf(packageLease is null, this);
        return _resource;
    }
}
