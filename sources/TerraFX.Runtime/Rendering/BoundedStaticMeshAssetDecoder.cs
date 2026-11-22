// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Assets;

namespace TerraFX.Rendering;

internal static class BoundedStaticMeshAssetDecoder
{
    // The caller preclaims all helper allocations and retains the typed-result charge until both returned graphs are released or transferred.
    internal static AssetBoundedFailureFact Decode(
        AssetStoreRegistration registration,
        AssetId meshAssetId,
        AssetId materialAssetId,
        SpatialProfileId profileId,
        SpatialSettings spatialSettings,
        EbinReadLimits readLimits,
        scoped ReadOnlySpan<byte> meshEnvelope,
        scoped ReadOnlySpan<byte> materialEnvelope,
        AssetReference[] referenceWorkspace,
        int[] referenceIndexWorkspace,
        AssetReference[] uniqueReferences,
        AssetBoundedFileDisposition fileDisposition,
        [MaybeNull] out RenderMeshAsset mesh,
        [MaybeNull] out RenderMaterialAsset material,
        out int meshReferenceCount)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(readLimits);
        ArgumentNullException.ThrowIfNull(referenceWorkspace);
        ArgumentNullException.ThrowIfNull(referenceIndexWorkspace);
        ArgumentNullException.ThrowIfNull(uniqueReferences);
        mesh = null;
        material = null;
        meshReferenceCount = 0;

        if (!meshAssetId.IsValid || !materialAssetId.IsValid || (meshAssetId == materialAssetId))
        {
            return CreateFailure(
                AssetBoundedFailureStage.Header,
                AssetBoundedFailureKind.Invariant,
                fileDisposition);
        }

        if (registration.Family is not AssetStoreRegistrationFamily.StaticMeshValidationV1
            || !registration.TryGetCodec<RenderMeshAsset>(out var meshCodec)
            || !registration.TryGetCodec<RenderMaterialAsset>(out var materialCodec))
        {
            return CreateFailure(
                AssetBoundedFailureStage.Registration,
                AssetBoundedFailureKind.UnsupportedContent,
                fileDisposition);
        }

        var meshContext = new AssetCodecContext(
            meshAssetId,
            registration.Schemas,
            profileId,
            spatialSettings,
            readLimits);
        var materialContext = new AssetCodecContext(
            materialAssetId,
            registration.Schemas,
            profileId,
            spatialSettings,
            readLimits);
        var materialFailure = AssetEnvelopeDecoder.DecodeBounded(
            materialEnvelope,
            materialAssetId,
            materialCodec,
            materialContext,
            referenceWorkspace,
            referenceIndexWorkspace,
            uniqueReferences,
            fileDisposition,
            out var selectedMaterial,
            out var materialReferenceCount);

        if (materialFailure.Kind is not AssetBoundedFailureKind.None)
        {
            return materialFailure;
        }

        if ((selectedMaterial is null) || (materialReferenceCount != 0))
        {
            return CreateFailure(
                AssetBoundedFailureStage.Reference,
                AssetBoundedFailureKind.Reference,
                fileDisposition);
        }

        var meshFailure = AssetEnvelopeDecoder.DecodeBounded(
            meshEnvelope,
            meshAssetId,
            meshCodec,
            meshContext,
            referenceWorkspace,
            referenceIndexWorkspace,
            uniqueReferences,
            fileDisposition,
            out var selectedMesh,
            out var selectedMeshReferenceCount);

        if (meshFailure.Kind is not AssetBoundedFailureKind.None)
        {
            return meshFailure;
        }

        if (selectedMesh is null)
        {
            return CreateFailure(
                AssetBoundedFailureStage.Decode,
                AssetBoundedFailureKind.TypedRecord,
                fileDisposition);
        }

        if ((selectedMeshReferenceCount != 1)
            || (uniqueReferences[0] != new AssetReference(
                materialAssetId,
                materialCodec.Schema.SchemaId,
                materialCodec.Schema.Major)))
        {
            return CreateFailure(
                AssetBoundedFailureStage.Reference,
                AssetBoundedFailureKind.Reference,
                fileDisposition);
        }

        if ((selectedMesh.MaterialSlots.Length != 1)
            || (selectedMesh.MaterialSlots[0] != materialAssetId)
            || (selectedMesh.Groups.Length != 1)
            || (selectedMesh.Groups[0].MaterialSlot != 0)
            || selectedMesh.SkeletonAssetId.HasValue
            || selectedMaterial.IsSprite
            || selectedMaterial.Parameters.SpriteTexture.HasValue
            || selectedMaterial.Parameters.BaseTexture.HasValue
            || selectedMaterial.Parameters.PackedMetalRoughOcclusion.HasValue
            || selectedMaterial.Parameters.NormalTexture.HasValue
            || selectedMaterial.Parameters.EmissiveTexture.HasValue)
        {
            return CreateFailure(
                AssetBoundedFailureStage.Decode,
                AssetBoundedFailureKind.TypedRecord,
                fileDisposition);
        }

        mesh = selectedMesh;
        material = selectedMaterial;
        meshReferenceCount = selectedMeshReferenceCount;
        return default;
    }

    internal static AssetBoundedFailureFact Decode(
        AssetStoreRegistration registration,
        AssetId meshAssetId,
        AssetId materialAssetId,
        SpatialProfileId profileId,
        SpatialSettings spatialSettings,
        EbinReadLimits readLimits,
        scoped ReadOnlySpan<byte> meshEnvelope,
        scoped ReadOnlySpan<byte> materialEnvelope,
        AssetReference[] referenceWorkspace,
        int[] referenceIndexWorkspace,
        AssetReference[] uniqueReferences,
        [MaybeNull] out RenderMeshAsset mesh,
        [MaybeNull] out RenderMaterialAsset material,
        out int meshReferenceCount)
        => Decode(
            registration,
            meshAssetId,
            materialAssetId,
            profileId,
            spatialSettings,
            readLimits,
            meshEnvelope,
            materialEnvelope,
            referenceWorkspace,
            referenceIndexWorkspace,
            uniqueReferences,
            AssetBoundedFileDisposition.NotAttempted,
            out mesh,
            out material,
            out meshReferenceCount);

    private static AssetBoundedFailureFact CreateFailure(
        AssetBoundedFailureStage stage,
        AssetBoundedFailureKind kind,
        AssetBoundedFileDisposition fileDisposition)
        => new(
            stage,
            kind,
            AssetBoundedNativeApi.None,
            0,
            Guid.Empty,
            0,
            fileDisposition);
}
