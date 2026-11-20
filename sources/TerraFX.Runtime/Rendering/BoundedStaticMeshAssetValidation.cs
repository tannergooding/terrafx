// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal static class BoundedStaticMeshAssetValidation
{
    private const long PerStoreProvisionBytes = 16_384;
    private const long SharedProvisionBytes = 1_048_576;

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The original issuer transfers to the bounded delivery, or ReleaseUnstartedBoundedPreclaim returns it if delivery construction fails.")]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The closed registration factory maps managed failures into value-only outcomes and retains no exception reference.")]
    internal static BoundedStaticMeshAssetRefusal TryPreclaim(
        string absolutePackagePath,
        ReadOnlySpan<byte> expectedPackageSha256,
        long cpuAssetBudgetBytes,
        int maximumDecodedAssetBytes,
        int maximumConcurrentDecodeBytes,
        int maximumLiveAssetLeases,
        out BoundedStaticMeshAssetDelivery? delivery,
        CancellationToken cancellationToken)
    {
        delivery = null;
        var refusal = (AssetBoundedRefusal)AssetStore.TryPreclaimBoundedStaticMeshValidationV1(
            absolutePackagePath,
            expectedPackageSha256,
            cpuAssetBudgetBytes,
            maximumDecodedAssetBytes,
            maximumConcurrentDecodeBytes,
            maximumLiveAssetLeases,
            PerStoreProvisionBytes,
            SharedProvisionBytes,
            out var issuer,
            cancellationToken);

        if (refusal is not AssetBoundedRefusal.None)
        {
            return (BoundedStaticMeshAssetRefusal)refusal;
        }

        var boundedIssuer = issuer
            ?? throw new InvalidOperationException("A successful bounded Assets preclaim returned no original issuer.");

        AssetStoreRegistration? registration = null;

        try
        {
            registration = BoundedStaticMeshAssetRegistrationFactory.Create();
        }
        catch (Exception)
        {
            boundedIssuer.FailBoundedInitialization(new AssetBoundedFailureFact(
                AssetBoundedFailureStage.Registration,
                AssetBoundedFailureKind.ManagedExecution,
                AssetBoundedNativeApi.None,
                0,
                Guid.Empty,
                0,
                AssetBoundedFileDisposition.NotAttempted));
        }

        if ((registration is not null)
            && !boundedIssuer.TryBindBoundedStaticMeshRegistration(registration))
        {
            boundedIssuer.FailBoundedInitialization(new AssetBoundedFailureFact(
                AssetBoundedFailureStage.Registration,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedNativeApi.None,
                0,
                Guid.Empty,
                0,
                AssetBoundedFileDisposition.NotAttempted));
        }

        try
        {
            delivery = new BoundedStaticMeshAssetDelivery(boundedIssuer);
            return BoundedStaticMeshAssetRefusal.None;
        }
        catch (OutOfMemoryException)
        {
            _ = boundedIssuer.ReleaseUnstartedBoundedPreclaim();
            return BoundedStaticMeshAssetRefusal.Capacity;
        }
    }
}
