// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Assets;

namespace TerraFX.Rendering;

// The caller preclaims contexts, workspaces, staging, and result storage; payload spans are borrowed one at a time.
internal ref struct BoundedStaticMeshAssetDecoder
{
    private const byte StateCompleted = 4;
    private const byte StateCreated = 1;
    private const byte StateDisposed = 5;
    private const byte StateFailed = 6;
    private const byte StateMaterialDecoded = 2;
    private const byte StateMeshDecoded = 3;

    private AssetBoundedFailureFact _failure;
    private RenderMaterialAsset? _material;
    private readonly AssetId _materialAssetId;
    private readonly AssetStoreRegistration.IAssetCodec<RenderMaterialAsset> _materialCodec;
    private readonly AssetCodecContext _materialContext;
    private RenderMeshAsset? _mesh;
    private readonly AssetId _meshAssetId;
    private readonly AssetStoreRegistration.IAssetCodec<RenderMeshAsset> _meshCodec;
    private readonly AssetCodecContext _meshContext;
    private int _meshReferenceCount;
    private readonly int[] _referenceIndexWorkspace;
    private readonly AssetReference[] _referenceWorkspace;
    private byte _state;
    private readonly AssetReference[] _uniqueReferences;

    private BoundedStaticMeshAssetDecoder(
        AssetId meshAssetId,
        AssetId materialAssetId,
        AssetStoreRegistration.IAssetCodec<RenderMeshAsset> meshCodec,
        AssetStoreRegistration.IAssetCodec<RenderMaterialAsset> materialCodec,
        AssetCodecContext meshContext,
        AssetCodecContext materialContext,
        AssetReference[] referenceWorkspace,
        int[] referenceIndexWorkspace,
        AssetReference[] uniqueReferences)
    {
        _failure = default;
        _material = null;
        _materialAssetId = materialAssetId;
        _materialCodec = materialCodec;
        _materialContext = materialContext;
        _mesh = null;
        _meshAssetId = meshAssetId;
        _meshCodec = meshCodec;
        _meshContext = meshContext;
        _meshReferenceCount = 0;
        _referenceIndexWorkspace = referenceIndexWorkspace;
        _referenceWorkspace = referenceWorkspace;
        _state = StateCreated;
        _uniqueReferences = uniqueReferences;
    }

    internal static AssetBoundedFailureFact Create(
        AssetStoreRegistration registration,
        AssetId meshAssetId,
        AssetId materialAssetId,
        AssetCodecContext meshContext,
        AssetCodecContext materialContext,
        AssetReference[] referenceWorkspace,
        int[] referenceIndexWorkspace,
        AssetReference[] uniqueReferences,
        out BoundedStaticMeshAssetDecoder decoder)
    {
        decoder = default;
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(meshContext);
        ArgumentNullException.ThrowIfNull(materialContext);
        ArgumentNullException.ThrowIfNull(referenceWorkspace);
        ArgumentNullException.ThrowIfNull(referenceIndexWorkspace);
        ArgumentNullException.ThrowIfNull(uniqueReferences);

        if (!meshAssetId.IsValid
            || !materialAssetId.IsValid
            || (meshAssetId == materialAssetId)
            || (meshContext.RootAssetId != meshAssetId)
            || (materialContext.RootAssetId != materialAssetId)
            || !ReferenceEquals(meshContext.Schemas, registration.Schemas)
            || !ReferenceEquals(materialContext.Schemas, registration.Schemas)
            || !ReferenceEquals(meshContext.ReadLimits, materialContext.ReadLimits)
            || !meshContext.ProfileId.Equals(materialContext.ProfileId)
            || !meshContext.SpatialSettings.Equals(materialContext.SpatialSettings))
        {
            return CreateFailure(
                AssetBoundedFailureStage.Header,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedFileDisposition.NotAttempted);
        }

        if ((registration.Family is not AssetStoreRegistrationFamily.StaticMeshValidationV1)
            || !registration.TryGetCodec<RenderMeshAsset>(out var meshCodec)
            || !registration.TryGetCodec<RenderMaterialAsset>(out var materialCodec))
        {
            return CreateFailure(
                AssetBoundedFailureStage.Registration,
                AssetBoundedFailureKind.UnsupportedContent,
                AssetBoundedFileDisposition.NotAttempted);
        }

        decoder = new BoundedStaticMeshAssetDecoder(
            meshAssetId,
            materialAssetId,
            meshCodec,
            materialCodec,
            meshContext,
            materialContext,
            referenceWorkspace,
            referenceIndexWorkspace,
            uniqueReferences);
        return default;
    }

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

    internal AssetBoundedFailureFact Complete(
        out RenderMeshAsset? mesh,
        out RenderMaterialAsset? material,
        out int meshReferenceCount)
    {
        mesh = null;
        material = null;
        meshReferenceCount = 0;

        if (_state is StateFailed)
        {
            return _failure;
        }

        if ((_state is not StateMeshDecoded) || (_mesh is null) || (_material is null))
        {
            return CreateFailure(
                AssetBoundedFailureStage.Publication,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedFileDisposition.NotAttempted);
        }

        mesh = _mesh;
        material = _material;
        meshReferenceCount = _meshReferenceCount;
        _mesh = null;
        _material = null;
        _state = StateCompleted;
        return default;
    }

    internal AssetBoundedFailureFact DecodeMaterial(
        scoped ReadOnlySpan<byte> materialEnvelope,
        AssetBoundedFileDisposition fileDisposition)
    {
        if (_state is not StateCreated)
        {
            return RetainFailure(
                AssetBoundedFailureStage.Decode,
                AssetBoundedFailureKind.Invariant,
                fileDisposition);
        }

        var failure = AssetEnvelopeDecoder.DecodeBounded(
            materialEnvelope,
            _materialAssetId,
            _materialCodec,
            _materialContext,
            _referenceWorkspace,
            _referenceIndexWorkspace,
            _uniqueReferences,
            fileDisposition,
            out var selectedMaterial,
            out var materialReferenceCount);

        if (failure.Kind is not AssetBoundedFailureKind.None)
        {
            _failure = failure;
            _state = StateFailed;
            return failure;
        }

        if ((selectedMaterial is null)
            || (materialReferenceCount != 0)
            || selectedMaterial.IsSprite
            || selectedMaterial.Parameters.SpriteTexture.HasValue
            || selectedMaterial.Parameters.BaseTexture.HasValue
            || selectedMaterial.Parameters.PackedMetalRoughOcclusion.HasValue
            || selectedMaterial.Parameters.NormalTexture.HasValue
            || selectedMaterial.Parameters.EmissiveTexture.HasValue)
        {
            return RetainFailure(
                AssetBoundedFailureStage.Reference,
                AssetBoundedFailureKind.Reference,
                fileDisposition);
        }

        _material = selectedMaterial;
        _state = StateMaterialDecoded;
        return default;
    }

    internal AssetBoundedFailureFact DecodeMesh(
        scoped ReadOnlySpan<byte> meshEnvelope,
        AssetBoundedFileDisposition fileDisposition)
    {
        if ((_state is not StateMaterialDecoded) || (_material is null))
        {
            return RetainFailure(
                AssetBoundedFailureStage.Decode,
                AssetBoundedFailureKind.Invariant,
                fileDisposition);
        }

        var failure = AssetEnvelopeDecoder.DecodeBounded(
            meshEnvelope,
            _meshAssetId,
            _meshCodec,
            _meshContext,
            _referenceWorkspace,
            _referenceIndexWorkspace,
            _uniqueReferences,
            fileDisposition,
            out var selectedMesh,
            out var selectedMeshReferenceCount);

        if (failure.Kind is not AssetBoundedFailureKind.None)
        {
            _failure = failure;
            _state = StateFailed;
            return failure;
        }

        if (selectedMesh is null)
        {
            return RetainFailure(
                AssetBoundedFailureStage.Decode,
                AssetBoundedFailureKind.TypedRecord,
                fileDisposition);
        }

        if ((selectedMeshReferenceCount != 1)
            || (_uniqueReferences[0] != new AssetReference(
                _materialAssetId,
                _materialCodec.Schema.SchemaId,
                _materialCodec.Schema.Major)))
        {
            return RetainFailure(
                AssetBoundedFailureStage.Reference,
                AssetBoundedFailureKind.Reference,
                fileDisposition);
        }

        if ((selectedMesh.MaterialSlots.Length != 1)
            || (selectedMesh.MaterialSlots[0] != _materialAssetId)
            || (selectedMesh.Groups.Length != 1)
            || (selectedMesh.Groups[0].MaterialSlot != 0)
            || selectedMesh.SkeletonAssetId.HasValue)
        {
            return RetainFailure(
                AssetBoundedFailureStage.Decode,
                AssetBoundedFailureKind.TypedRecord,
                fileDisposition);
        }

        _mesh = selectedMesh;
        _meshReferenceCount = selectedMeshReferenceCount;
        _state = StateMeshDecoded;
        return default;
    }

    internal void Dispose()
    {
        _mesh = null;
        _material = null;
        _state = StateDisposed;
    }

    private AssetBoundedFailureFact RetainFailure(
        AssetBoundedFailureStage stage,
        AssetBoundedFailureKind kind,
        AssetBoundedFileDisposition fileDisposition)
    {
        _failure = CreateFailure(stage, kind, fileDisposition);
        _state = StateFailed;
        return _failure;
    }
}
