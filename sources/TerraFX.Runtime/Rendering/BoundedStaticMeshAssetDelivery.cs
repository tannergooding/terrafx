// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal sealed class BoundedStaticMeshAssetDelivery
{
    private AssetStore? _issuer;
    private int _state;
    private BoundedStaticMeshAssetPreclaimRelease _terminalPreclaimRelease;

    internal BoundedStaticMeshAssetDelivery(AssetStore issuer)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        _issuer = issuer;
    }

    internal long ReservedBytes
        => Volatile.Read(ref _issuer)?.BoundedReservedBytes ?? 0;

    private static BoundedStaticMeshFailureFact ConvertFailure(AssetBoundedFailureFact failure)
        => new(
            (BoundedStaticMeshFailureStage)failure.Stage,
            (BoundedStaticMeshFailureKind)failure.Kind,
            (BoundedStaticMeshNativeApi)failure.NativeApi,
            failure.NativeError,
            failure.SchemaId,
            failure.FieldTag,
            (BoundedStaticMeshFileDisposition)failure.FileDisposition);

    private static BoundedStaticMeshAssetPreclaimRelease ConvertPreclaimRelease(AssetBoundedPreclaimRelease release)
        => new(
            release.OwnerReleased,
            (BoundedStaticMeshAssetRefusal)release.Refusal,
            release.HasFailure,
            ConvertFailure(release.Failure),
            release.RetainedSharedProvisionBytes);

    private static BoundedStaticMeshAssetPreclaimRelease RefusedPreclaim(BoundedStaticMeshAssetRefusal refusal)
        => new(
            ownerReleased: false,
            refusal,
            hasFailure: false,
            failure: default,
            retainedSharedProvisionBytes: 0);

    internal BoundedStaticMeshAssetPreclaimRelease ReleaseUnstartedPreclaim()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            return (Volatile.Read(ref _state) == 2)
                ? _terminalPreclaimRelease
                : RefusedPreclaim(BoundedStaticMeshAssetRefusal.Busy);
        }

        var issuer = Volatile.Read(ref _issuer)
            ?? throw new InvalidOperationException("The bounded Assets delivery lost its original issuer.");
        var release = issuer.ReleaseUnstartedBoundedPreclaim();
        _terminalPreclaimRelease = ConvertPreclaimRelease(release);
        _ = Interlocked.CompareExchange(ref _issuer, null, issuer);
        Volatile.Write(ref _state, 2);
        return _terminalPreclaimRelease;
    }
}
