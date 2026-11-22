// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Assets;

namespace TerraFX.Rendering;

// The claimed path owns one staging buffer and transfers decoded values only through a result owner.
internal ref struct BoundedStaticMeshAssetDecoder
{
    private const long ArrayHeaderAndAlignmentBytes = 79;
    private const long CodecContextShellBytes = 64;
    private const long MaterialShellBytes = 240;
    private const int MaximumReferenceCount = 257;
    private const long MeshShellBytes = 176;
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
    private AssetBoundedOperationClaim _operationClaim;
    private int[] _referenceIndexWorkspace;
    private AssetReference[] _referenceWorkspace;
    private byte[]? _stagingBuffer;
    private byte _state;
    private AssetReference[] _uniqueReferences;

    private BoundedStaticMeshAssetDecoder(
        AssetId meshAssetId,
        AssetId materialAssetId,
        AssetStoreRegistration.IAssetCodec<RenderMeshAsset> meshCodec,
        AssetStoreRegistration.IAssetCodec<RenderMaterialAsset> materialCodec,
        AssetCodecContext meshContext,
        AssetCodecContext materialContext,
        AssetReference[] referenceWorkspace,
        int[] referenceIndexWorkspace,
        AssetReference[] uniqueReferences,
        AssetBoundedOperationClaim operationClaim,
        byte[]? stagingBuffer)
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
        _operationClaim = operationClaim;
        _referenceIndexWorkspace = referenceIndexWorkspace;
        _referenceWorkspace = referenceWorkspace;
        _stagingBuffer = stagingBuffer;
        _state = StateCreated;
        _uniqueReferences = uniqueReferences;
    }

    internal readonly Span<byte> StagingBuffer
        => _stagingBuffer
            ?? throw new InvalidOperationException("The bounded operation has no claimed staging buffer.");

    internal static AssetBoundedFailureFact CreateCandidateForTests(
        AssetStoreRegistration registration,
        AssetId meshAssetId,
        AssetId materialAssetId,
        AssetCodecContext meshContext,
        AssetCodecContext materialContext,
        AssetReference[] referenceWorkspace,
        int[] referenceIndexWorkspace,
        AssetReference[] uniqueReferences,
        out BoundedStaticMeshAssetDecoder decoder)
        => CreateCandidateCore(
            registration,
            meshAssetId,
            materialAssetId,
            meshContext,
            materialContext,
            referenceWorkspace,
            referenceIndexWorkspace,
            uniqueReferences,
            out decoder);

    internal static AssetBoundedRefusal CreateClaimed(
        BoundedStaticMeshAssetDelivery delivery,
        AssetId meshAssetId,
        AssetId materialAssetId,
        int meshEnvelopeLength,
        int materialEnvelopeLength,
        out AssetBoundedFailureFact failure,
        out AssetBoundedOperationClaim unconsumedClaim,
        out BoundedStaticMeshAssetDecoder decoder)
    {
        failure = default;
        unconsumedClaim = default;
        decoder = default;

        if ((delivery is null)
            || !meshAssetId.IsValid
            || !materialAssetId.IsValid
            || (meshAssetId == materialAssetId)
            || (meshEnvelopeLength <= 0)
            || (materialEnvelopeLength <= 0))
        {
            return AssetBoundedRefusal.InvalidOptions;
        }

        var claimBytes = GetMinimumKnownClaimBytes(meshEnvelopeLength, materialEnvelopeLength);
        var claimRefusal = delivery.TryClaimOperation(claimBytes, out var operationClaim);

        if (claimRefusal is not AssetBoundedRefusal.None)
        {
            return claimRefusal;
        }

        AssetBoundedOperationClaim handoffClaim = default;

        try
        {
            if ((operationClaim.MaximumConcurrentDecodeBytes < Math.Max(meshEnvelopeLength, materialEnvelopeLength))
                || (operationClaim.MaximumLiveAssetLeases < 2))
            {
                unconsumedClaim = operationClaim.Detach();
                return AssetBoundedRefusal.Capacity;
            }

            var readLimits = operationClaim.ReadLimits;

            if ((meshEnvelopeLength > readLimits.MaximumRecordBytes)
                || (materialEnvelopeLength > readLimits.MaximumRecordBytes))
            {
                unconsumedClaim = operationClaim.Detach();
                return AssetBoundedRefusal.InvalidOptions;
            }

            var registration = operationClaim.Registration;
            var meshContext = new AssetCodecContext(
                meshAssetId,
                registration.Schemas,
                SpatialProfileId.Default,
                SpatialSettings.Default,
                readLimits);
            var materialContext = new AssetCodecContext(
                materialAssetId,
                registration.Schemas,
                SpatialProfileId.Default,
                SpatialSettings.Default,
                readLimits);
            var referenceWorkspace = new AssetReference[MaximumReferenceCount];
            var referenceIndexWorkspace = new int[MaximumReferenceCount];
            var uniqueReferences = new AssetReference[MaximumReferenceCount];
            var stagingBuffer = new byte[Math.Max(meshEnvelopeLength, materialEnvelopeLength)];
            handoffClaim = operationClaim.Detach();
            var createRefusal = CreateClaimedCore(
                handoffClaim,
                registration,
                meshAssetId,
                materialAssetId,
                meshContext,
                materialContext,
                referenceWorkspace,
                referenceIndexWorkspace,
                uniqueReferences,
                stagingBuffer,
                meshEnvelopeLength,
                materialEnvelopeLength,
                out failure,
                out unconsumedClaim,
                out decoder);
            handoffClaim = default;
            return createRefusal;
        }
        catch (OutOfMemoryException)
        {
            if (handoffClaim.IsActive)
            {
                unconsumedClaim = handoffClaim.Detach();
            }
            else if (operationClaim.IsActive)
            {
                unconsumedClaim = operationClaim.Detach();
            }

            decoder = default;
            failure = default;
            return AssetBoundedRefusal.Capacity;
        }
    }

    internal static long GetMinimumKnownClaimBytes(int meshEnvelopeLength, int materialEnvelopeLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(meshEnvelopeLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(materialEnvelopeLength);
        var (referenceWorkspaceBytes, referenceResultBytes) = AssetReferenceScanner.GetFixedScanDataBytes(
            MaximumReferenceCount,
            MaximumReferenceCount);
        var stagingLength = Math.Max(meshEnvelopeLength, materialEnvelopeLength);
        var stagingBytes = checked(64L + (((long)stagingLength + 15L) & ~15L));
        var meshBytes = checked(
            (4L * meshEnvelopeLength)
            + (13L * ArrayHeaderAndAlignmentBytes)
            + MeshShellBytes);
        var materialBytes = checked(
            materialEnvelopeLength
            + ArrayHeaderAndAlignmentBytes
            + MaterialShellBytes);

        return checked(
            stagingBytes
            + meshBytes
            + materialBytes
            + referenceWorkspaceBytes
            + referenceResultBytes
            + (2L * CodecContextShellBytes));
    }

    private static AssetBoundedFailureFact CreateCandidateCore(
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
            uniqueReferences,
            default,
            null);
        return default;
    }

    private static AssetBoundedRefusal CreateClaimedCore(
        AssetBoundedOperationClaim operationClaim,
        AssetStoreRegistration registration,
        AssetId meshAssetId,
        AssetId materialAssetId,
        AssetCodecContext meshContext,
        AssetCodecContext materialContext,
        AssetReference[] referenceWorkspace,
        int[] referenceIndexWorkspace,
        AssetReference[] uniqueReferences,
        byte[] stagingBuffer,
        int meshEnvelopeLength,
        int materialEnvelopeLength,
        out AssetBoundedFailureFact failure,
        out AssetBoundedOperationClaim unconsumedClaim,
        out BoundedStaticMeshAssetDecoder decoder)
    {
        failure = default;
        unconsumedClaim = default;
        decoder = default;

        if (!operationClaim.IsActive)
        {
            return AssetBoundedRefusal.Busy;
        }

        if ((registration is null)
            || (meshContext is null)
            || (materialContext is null)
            || (referenceWorkspace is null)
            || (referenceIndexWorkspace is null)
            || (uniqueReferences is null)
            || (stagingBuffer is null)
            || (meshEnvelopeLength <= 0)
            || (materialEnvelopeLength <= 0)
            || (meshEnvelopeLength > meshContext.ReadLimits.MaximumRecordBytes)
            || (materialEnvelopeLength > materialContext.ReadLimits.MaximumRecordBytes)
            || (stagingBuffer.Length != Math.Max(meshEnvelopeLength, materialEnvelopeLength))
            || (referenceWorkspace.Length != MaximumReferenceCount)
            || (referenceIndexWorkspace.Length != MaximumReferenceCount)
            || (uniqueReferences.Length != MaximumReferenceCount))
        {
            unconsumedClaim = operationClaim.Detach();
            return AssetBoundedRefusal.InvalidOptions;
        }

        if (operationClaim.Bytes < GetMinimumKnownClaimBytes(meshEnvelopeLength, materialEnvelopeLength))
        {
            unconsumedClaim = operationClaim.Detach();
            return AssetBoundedRefusal.Capacity;
        }

        if (!ReferenceEquals(operationClaim.Registration, registration))
        {
            unconsumedClaim = operationClaim.Detach();
            return AssetBoundedRefusal.UnsupportedRegistration;
        }

        failure = CreateCandidateCore(
            registration,
            meshAssetId,
            materialAssetId,
            meshContext,
            materialContext,
            referenceWorkspace,
            referenceIndexWorkspace,
            uniqueReferences,
            out decoder);

        if (failure.Kind is not AssetBoundedFailureKind.None)
        {
            unconsumedClaim = operationClaim.Detach();
            return AssetBoundedRefusal.None;
        }

        decoder._operationClaim = operationClaim.Detach();
        decoder._stagingBuffer = stagingBuffer;
        return AssetBoundedRefusal.None;
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

    internal AssetBoundedFailureFact CompleteClaimed(
        out BoundedStaticMeshAssetDecodeResultOwner resultOwner,
        out AssetBoundedOperationClaim returnedClaim)
    {
        resultOwner = default;
        returnedClaim = default;

        if (!_operationClaim.IsActive)
        {
            this = default;
            return CreateFailure(
                AssetBoundedFailureStage.Publication,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedFileDisposition.NotAttempted);
        }

        if (_state is StateFailed)
        {
            var failure = _failure;
            returnedClaim = _operationClaim.Detach();
            this = default;
            return failure;
        }

        if ((_state is not StateMeshDecoded) || (_mesh is not { } mesh) || (_material is not { } material))
        {
            returnedClaim = _operationClaim.Detach();
            this = default;
            return CreateFailure(
                AssetBoundedFailureStage.Publication,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedFileDisposition.NotAttempted);
        }

        resultOwner = new BoundedStaticMeshAssetDecodeResultOwner(
            mesh,
            material,
            _meshReferenceCount,
            _operationClaim.Detach());
        this = default;
        return default;
    }

    internal AssetBoundedFailureFact CompleteUnfundedCandidate(
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
        this = default;
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
        if (_operationClaim.IsActive)
        {
            throw new InvalidOperationException("The bounded operation claim must be returned through Dispose before releasing its provision.");
        }

        this = default;
        _state = StateDisposed;
    }

    internal void Dispose(out AssetBoundedOperationClaim operationClaim)
    {
        operationClaim = _operationClaim.IsActive
            ? _operationClaim.Detach()
            : default;
        this = default;
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
