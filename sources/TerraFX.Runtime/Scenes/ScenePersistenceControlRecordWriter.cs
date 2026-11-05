// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal static class ScenePersistenceControlRecordWriter
{
    private static readonly Guid s_controlSchemaId = new ContentSchemaIdentityAssignment("state/control-scene").SchemaId;
    private static readonly Guid s_reparentIntentSchemaId = new ContentSchemaIdentityAssignment("scene/intent-reparent").SchemaId;
    private static readonly Guid s_rootPositionIntentSchemaId = new ContentSchemaIdentityAssignment("scene/intent-root-position").SchemaId;

    internal static EbinWriteStatus MeasureActivation(
        World world,
        SceneExpansionPlan plan,
        Guid sceneInstanceId,
        Guid? requestedSceneInstanceId,
        scoped ReadOnlySpan<SceneExternalBindingCaptureRow> externalBindings,
        EbinReadLimits limits,
        out ScenePersistenceControlRecordWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;

        if (plan.Occurrences.IsDefaultOrEmpty || plan.GetSource(0).IsPrefab)
        {
            diagnostic = CreateDiagnostic("control-scene-source");
            return EbinWriteStatus.InvalidRecord;
        }

        var sourceStatus = ScenePersistenceOccurrenceWriter.MeasureSourceAssetReference(
            plan,
            0,
            limits,
            out var sourceRecordBytes,
            out diagnostic);

        if (sourceStatus is not EbinWriteStatus.Complete)
        {
            return sourceStatus;
        }

        var bindingStatus = ScenePersistenceExternalBindingArrayWriter.Measure(
            externalBindings,
            limits,
            out var bindingRecordStorageBytes,
            out var bindingCandidateBytes,
            out var bindingArrayPayloadBytes,
            out diagnostic);

        if (bindingStatus is not EbinWriteStatus.Complete)
        {
            return bindingStatus;
        }

        var rootPayloadBytes = checked(
            1
            + 16
            + sourceRecordBytes
            + bindingArrayPayloadBytes
            + (requestedSceneInstanceId.HasValue ? 16 : 0)
            + 16);
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[6];
        var fieldCount = BuildActivationLayout(
            sourceRecordBytes,
            externalBindings.Length,
            bindingArrayPayloadBytes,
            requestedSceneInstanceId.HasValue,
            rootPayloadBytes,
            fields);
        var status = MeasureRootRecord(
            fields[..fieldCount],
            rootPayloadBytes,
            limits,
            out var recordBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        workspace = new ScenePersistenceControlRecordWorkspaceRequirements(
            ScenePersistenceControlKind.Activate,
            recordBytes,
            rootPayloadBytes,
            sourceRecordBytes,
            bindingRecordStorageBytes,
            bindingCandidateBytes,
            bindingArrayPayloadBytes,
            externalBindings.Length,
            intentRecordStorageBytes: 0,
            intentCandidateBytes: 0,
            intentArrayPayloadBytes: 0,
            intentCount: 0);
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus MeasureEdit(
        World world,
        Guid sceneInstanceId,
        ImmutableArray<SceneTransactionOperation> operations,
        EbinReadLimits limits,
        out ScenePersistenceControlRecordWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;

        if (operations.IsDefaultOrEmpty)
        {
            diagnostic = CreateDiagnostic("control-scene-edit-empty");
            return EbinWriteStatus.InvalidRecord;
        }

        var intentStatus = MeasureIntents(
            world,
            operations.AsSpan(),
            limits,
            out var intentRecordStorageBytes,
            out var intentCandidateBytes,
            out var intentArrayPayloadBytes,
            out diagnostic);

        if (intentStatus is not EbinWriteStatus.Complete)
        {
            return intentStatus;
        }

        var rootPayloadBytes = checked(1 + 16 + intentArrayPayloadBytes);
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        var fieldCount = BuildEditLayout(
            operations.Length,
            intentArrayPayloadBytes,
            rootPayloadBytes,
            fields);
        var status = MeasureRootRecord(
            fields[..fieldCount],
            rootPayloadBytes,
            limits,
            out var recordBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        workspace = new ScenePersistenceControlRecordWorkspaceRequirements(
            ScenePersistenceControlKind.Edit,
            recordBytes,
            rootPayloadBytes,
            sourceAssetReferenceRecordBytes: 0,
            externalBindingRecordStorageBytes: 0,
            externalBindingCandidateBytes: 0,
            externalBindingArrayPayloadBytes: 0,
            externalBindingCount: 0,
            intentRecordStorageBytes,
            intentCandidateBytes,
            intentArrayPayloadBytes,
            operations.Length);
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus MeasureUnload(
        World world,
        Guid sceneInstanceId,
        HierarchyDestroyMode destroyMode,
        EbinReadLimits limits,
        out ScenePersistenceControlRecordWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        diagnostic = null;

        if (destroyMode is not HierarchyDestroyMode.DetachChildrenKeepWorld and not HierarchyDestroyMode.DestroySubtree)
        {
            diagnostic = CreateDiagnostic("control-scene-destroy-mode");
            return EbinWriteStatus.InvalidRecord;
        }

        const int rootPayloadBytes = 18;
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        var fieldCount = BuildUnloadLayout(rootPayloadBytes, fields);
        var status = MeasureRootRecord(
            fields[..fieldCount],
            rootPayloadBytes,
            limits,
            out var recordBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        workspace = new ScenePersistenceControlRecordWorkspaceRequirements(
            ScenePersistenceControlKind.Unload,
            recordBytes,
            rootPayloadBytes,
            sourceAssetReferenceRecordBytes: 0,
            externalBindingRecordStorageBytes: 0,
            externalBindingCandidateBytes: 0,
            externalBindingArrayPayloadBytes: 0,
            externalBindingCount: 0,
            intentRecordStorageBytes: 0,
            intentCandidateBytes: 0,
            intentArrayPayloadBytes: 0,
            intentCount: 0);
        return EbinWriteStatus.Complete;
    }

    internal static EbinWriteStatus WriteActivation(
        SceneExpansionPlan plan,
        Guid sceneInstanceId,
        Guid? requestedSceneInstanceId,
        scoped ReadOnlySpan<SceneExternalBindingCaptureRow> externalBindings,
        ScenePersistenceControlRecordWorkspaceRequirements workspace,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        bytesWritten = 0;

        if (workspace.Kind is not ScenePersistenceControlKind.Activate
            || workspace.ExternalBindingCount != externalBindings.Length
            || workspace.RecordBytes != candidateStorage.Length
            || workspace.RecordBytes != destination.Length)
        {
            diagnostic = CreateDiagnostic("control-scene-activation-workspace");
            return EbinWriteStatus.InvalidRecord;
        }

        var fields = (Span<EbinFieldSource>)stackalloc EbinFieldSource[6];
        var rootPayloadStorage = workspace.RootPayloadBytes <= 4096
            ? (Span<byte>)stackalloc byte[workspace.RootPayloadBytes]
            : new byte[workspace.RootPayloadBytes];
        rootPayloadStorage.Clear();
        var fieldCount = 0;
        var payloadOffset = 0;
        var status = AddByteField(
            1,
            (byte)ScenePersistenceControlKind.Activate,
            required: true,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref payloadOffset,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddGuidField(
            2,
            sceneInstanceId,
            required: true,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref payloadOffset,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        Span<byte> sourceCandidate = stackalloc byte[workspace.SourceAssetReferenceRecordBytes];
        Span<byte> sourceRecord = stackalloc byte[workspace.SourceAssetReferenceRecordBytes];
        status = ScenePersistenceOccurrenceWriter.WriteSourceAssetReference(
            plan.GetSource(0),
            limits,
            sourceCandidate,
            sourceRecord,
            out var sourceBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (sourceBytesWritten != workspace.SourceAssetReferenceRecordBytes)
        {
            throw new InvalidOperationException("The Scene control source-reference extent changed after measurement.");
        }

        var sourceValidationStatus = EbinStructuralReader.Validate(
            sourceRecord,
            limits,
            EbinReadPolicy.Canonical,
            out var sourceBytesConsumed,
            out _);

        if ((sourceValidationStatus is not EbinReadStatus.Complete)
            || (sourceBytesConsumed != sourceRecord.Length))
        {
            diagnostic = CreateDiagnostic("control-scene-source-record");
            return EbinWriteStatus.InvalidRecord;
        }

        AddRecordField(
            3,
            required: false,
            sourceRecord,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref payloadOffset);

        var bindingSlices = externalBindings.IsEmpty
            ? []
            : new EbinPayloadSlice[externalBindings.Length];
        var bindingRecordStorage = externalBindings.IsEmpty
            ? []
            : new byte[workspace.ExternalBindingRecordStorageBytes];
        var bindingCandidateStorage = externalBindings.IsEmpty
            ? []
            : new byte[workspace.ExternalBindingCandidateBytes];
        var bindingArrayPayload = new byte[workspace.ExternalBindingArrayPayloadBytes];
        status = ScenePersistenceExternalBindingArrayWriter.Write(
            externalBindings,
            limits,
            bindingSlices,
            bindingRecordStorage,
            bindingCandidateStorage,
            bindingArrayPayload,
            out var bindingPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (bindingPayloadBytes != workspace.ExternalBindingArrayPayloadBytes)
        {
            throw new InvalidOperationException("The Scene control external-binding array extent changed after measurement.");
        }

        AddArrayField(
            6,
            required: false,
            checked((ulong)externalBindings.Length),
            bindingArrayPayload,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref payloadOffset);

        if (requestedSceneInstanceId is Guid requestedId)
        {
            status = AddGuidField(
                7,
                requestedId,
                required: false,
                fields,
                ref fieldCount,
                rootPayloadStorage,
                ref payloadOffset,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }
        }

        status = AddGuidField(
            8,
            sceneInstanceId,
            required: false,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref payloadOffset,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        return WriteRoot(
            fields[..fieldCount],
            rootPayloadStorage[..payloadOffset],
            workspace,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal static EbinWriteStatus WriteEdit(
        World world,
        Guid sceneInstanceId,
        ImmutableArray<SceneTransactionOperation> operations,
        ScenePersistenceControlRecordWorkspaceRequirements workspace,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        bytesWritten = 0;

        if (workspace.Kind is not ScenePersistenceControlKind.Edit
            || operations.IsDefaultOrEmpty
            || workspace.IntentCount != operations.Length
            || workspace.RecordBytes != candidateStorage.Length
            || workspace.RecordBytes != destination.Length)
        {
            diagnostic = CreateDiagnostic("control-scene-edit-workspace");
            return EbinWriteStatus.InvalidRecord;
        }

        var intentSlices = new EbinPayloadSlice[operations.Length];
        var intentRecordStorage = new byte[workspace.IntentRecordStorageBytes];
        var intentCandidateStorage = new byte[workspace.IntentCandidateBytes];
        var intentArrayPayload = new byte[workspace.IntentArrayPayloadBytes];
        var status = WriteIntents(
            world,
            operations.AsSpan(),
            limits,
            intentSlices,
            intentRecordStorage,
            intentCandidateStorage,
            intentArrayPayload,
            out var intentPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (intentPayloadBytes != workspace.IntentArrayPayloadBytes)
        {
            throw new InvalidOperationException("The Scene control intent array extent changed after measurement.");
        }

        var fields = (Span<EbinFieldSource>)stackalloc EbinFieldSource[3];
        var rootPayloadStorage = workspace.RootPayloadBytes <= 4096
            ? (Span<byte>)stackalloc byte[workspace.RootPayloadBytes]
            : new byte[workspace.RootPayloadBytes];
        rootPayloadStorage.Clear();
        var fieldCount = 0;
        var payloadOffset = 0;
        status = AddByteField(
            1,
            (byte)ScenePersistenceControlKind.Edit,
            required: true,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref payloadOffset,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddGuidField(
            2,
            sceneInstanceId,
            required: true,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref payloadOffset,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        AddArrayField(
            4,
            required: false,
            checked((ulong)operations.Length),
            intentArrayPayload,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref payloadOffset);
        return WriteRoot(
            fields[..fieldCount],
            rootPayloadStorage[..payloadOffset],
            workspace,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    internal static EbinWriteStatus WriteUnload(
        Guid sceneInstanceId,
        HierarchyDestroyMode destroyMode,
        ScenePersistenceControlRecordWorkspaceRequirements workspace,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        bytesWritten = 0;

        if (workspace.Kind is not ScenePersistenceControlKind.Unload
            || workspace.RecordBytes != candidateStorage.Length
            || workspace.RecordBytes != destination.Length)
        {
            diagnostic = CreateDiagnostic("control-scene-unload-workspace");
            return EbinWriteStatus.InvalidRecord;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        Span<byte> rootPayloadStorage = stackalloc byte[workspace.RootPayloadBytes];
        rootPayloadStorage.Clear();
        var fieldCount = 0;
        var payloadOffset = 0;
        var status = AddByteField(
            1,
            (byte)ScenePersistenceControlKind.Unload,
            required: true,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref payloadOffset,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddGuidField(
            2,
            sceneInstanceId,
            required: true,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref payloadOffset,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddByteField(
            5,
            (byte)destroyMode,
            required: false,
            fields,
            ref fieldCount,
            rootPayloadStorage,
            ref payloadOffset,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        return WriteRoot(
            fields[..fieldCount],
            rootPayloadStorage[..payloadOffset],
            workspace,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static void AddArrayField(
        uint tag,
        bool required,
        ulong count,
        scoped ReadOnlySpan<byte> arrayPayload,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadOffset)
    {
        arrayPayload.CopyTo(payloadStorage[payloadOffset..]);
        fields[fieldCount++] = new EbinFieldSource(
            tag,
            24,
            required ? (ushort)1 : (ushort)0,
            count,
            new EbinPayloadSlice(payloadOffset, arrayPayload.Length));
        payloadOffset = checked(payloadOffset + arrayPayload.Length);
    }

    private static EbinWriteStatus AddByteField(
        uint tag,
        byte value,
        bool required,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadOffset,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(
            tag,
            1,
            required ? (ushort)1 : (ushort)0,
            1,
            new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddGuidField(
        uint tag,
        Guid value,
        bool required,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadOffset,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadOffset..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(
            tag,
            11,
            required ? (ushort)1 : (ushort)0,
            1,
            new EbinPayloadSlice(payloadOffset, fieldBytes));
        payloadOffset = checked(payloadOffset + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static void AddLayoutField(
        uint tag,
        ushort kind,
        bool required,
        int payloadBytes,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        ref int payloadOffset,
        ulong count = 1)
    {
        fields[fieldCount++] = new EbinFieldSource(
            tag,
            kind,
            required ? (ushort)1 : (ushort)0,
            count,
            new EbinPayloadSlice(payloadOffset, payloadBytes));
        payloadOffset = checked(payloadOffset + payloadBytes);
    }

    private static void AddRecordField(
        uint tag,
        bool required,
        scoped ReadOnlySpan<byte> record,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadOffset)
    {
        record.CopyTo(payloadStorage[payloadOffset..]);
        fields[fieldCount++] = new EbinFieldSource(
            tag,
            22,
            required ? (ushort)1 : (ushort)0,
            1,
            new EbinPayloadSlice(payloadOffset, record.Length));
        payloadOffset = checked(payloadOffset + record.Length);
    }

    private static int BuildActivationLayout(
        int sourceRecordBytes,
        int bindingCount,
        int bindingArrayPayloadBytes,
        bool hasRequestedSceneInstanceId,
        int rootPayloadBytes,
        Span<EbinFieldSource> fields)
    {
        fields.Clear();
        var fieldCount = 0;
        var payloadOffset = 0;
        AddLayoutField(1, 1, required: true, 1, fields, ref fieldCount, ref payloadOffset);
        AddLayoutField(2, 11, required: true, 16, fields, ref fieldCount, ref payloadOffset);
        AddLayoutField(3, 22, required: false, sourceRecordBytes, fields, ref fieldCount, ref payloadOffset);
        AddLayoutField(6, 24, required: false, bindingArrayPayloadBytes, fields, ref fieldCount, ref payloadOffset, checked((ulong)bindingCount));

        if (hasRequestedSceneInstanceId)
        {
            AddLayoutField(7, 11, required: false, 16, fields, ref fieldCount, ref payloadOffset);
        }

        AddLayoutField(8, 11, required: false, 16, fields, ref fieldCount, ref payloadOffset);
        ValidateLayoutExtent(payloadOffset, rootPayloadBytes);
        return fieldCount;
    }

    private static int BuildEditLayout(
        int intentCount,
        int intentArrayPayloadBytes,
        int rootPayloadBytes,
        Span<EbinFieldSource> fields)
    {
        fields.Clear();
        var fieldCount = 0;
        var payloadOffset = 0;
        AddLayoutField(1, 1, required: true, 1, fields, ref fieldCount, ref payloadOffset);
        AddLayoutField(2, 11, required: true, 16, fields, ref fieldCount, ref payloadOffset);
        AddLayoutField(
            4,
            24,
            required: false,
            intentArrayPayloadBytes,
            fields,
            ref fieldCount,
            ref payloadOffset,
            checked((ulong)intentCount));
        ValidateLayoutExtent(payloadOffset, rootPayloadBytes);
        return fieldCount;
    }

    private static int BuildUnloadLayout(int rootPayloadBytes, Span<EbinFieldSource> fields)
    {
        fields.Clear();
        var fieldCount = 0;
        var payloadOffset = 0;
        AddLayoutField(1, 1, required: true, 1, fields, ref fieldCount, ref payloadOffset);
        AddLayoutField(2, 11, required: true, 16, fields, ref fieldCount, ref payloadOffset);
        AddLayoutField(5, 1, required: false, 1, fields, ref fieldCount, ref payloadOffset);
        ValidateLayoutExtent(payloadOffset, rootPayloadBytes);
        return fieldCount;
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category)
        => new EbinStructuralDiagnostic(
            s_controlSchemaId,
            1,
            category,
            null,
            0,
            1,
            EbinStructuralDiagnosticSource.Construction);

    private static EbinWriteStatus MeasureIntent(
        World world,
        SceneTransactionOperation operation,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var entityId = world.GetEntityId(operation.Entity);

        if (entityId == Guid.Empty)
        {
            recordBytes = 0;
            diagnostic = CreateDiagnostic("control-scene-intent-entity");
            return EbinWriteStatus.InvalidRecord;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        Span<byte> payloadStorage = stackalloc byte[40];
        payloadStorage.Clear();
        var fieldCount = 0;
        var payloadOffset = 0;
        Guid schemaId;
        EbinWriteStatus status;

        switch (operation.Kind)
        {
            case SceneTransactionOperationKind.Reparent:
            {
                if (operation.Mode is not ReparentMode.KeepLocal and not ReparentMode.KeepWorld)
                {
                    recordBytes = 0;
                    diagnostic = CreateDiagnostic("control-scene-reparent-mode");
                    return EbinWriteStatus.InvalidRecord;
                }

                schemaId = s_reparentIntentSchemaId;
                status = AddGuidField(1, entityId, required: true, fields, ref fieldCount, payloadStorage, ref payloadOffset, out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    recordBytes = 0;
                    return status;
                }

                if (operation.Parent is Entity parent)
                {
                    status = AddGuidField(
                        2,
                        world.GetEntityId(parent),
                        required: false,
                        fields,
                        ref fieldCount,
                        payloadStorage,
                        ref payloadOffset,
                        out diagnostic);

                    if (status is not EbinWriteStatus.Complete)
                    {
                        recordBytes = 0;
                        return status;
                    }
                }

                status = AddByteField(
                    3,
                    (byte)operation.Mode,
                    required: true,
                    fields,
                    ref fieldCount,
                    payloadStorage,
                    ref payloadOffset,
                    out diagnostic);
                break;
            }

            case SceneTransactionOperationKind.SetRootPosition:
            {
                schemaId = s_rootPositionIntentSchemaId;
                status = AddGuidField(1, entityId, required: true, fields, ref fieldCount, payloadStorage, ref payloadOffset, out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    recordBytes = 0;
                    return status;
                }

                status = EbinPayloadWriter.Write(
                    operation.RootPosition,
                    payloadStorage[payloadOffset..],
                    out var positionBytes,
                    out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    recordBytes = 0;
                    return status;
                }

                fields[fieldCount++] = new EbinFieldSource(
                    2,
                    21,
                    1,
                    1,
                    new EbinPayloadSlice(payloadOffset, positionBytes));
                payloadOffset = checked(payloadOffset + positionBytes);
                break;
            }

            default:
            {
                recordBytes = 0;
                diagnostic = CreateDiagnostic("control-scene-intent-kind");
                return EbinWriteStatus.InvalidRecord;
            }
        }

        if (status is not EbinWriteStatus.Complete)
        {
            recordBytes = 0;
            return status;
        }

        var source = new EbinRecordWriteView(
            schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..payloadOffset]);
        return EbinStructuralWriter.Measure(in source, limits, out recordBytes, out diagnostic);
    }

    private static EbinWriteStatus MeasureIntents(
        World world,
        scoped ReadOnlySpan<SceneTransactionOperation> operations,
        EbinReadLimits limits,
        out int recordStorageBytes,
        out int maximumCandidateBytes,
        out int arrayPayloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        recordStorageBytes = 0;
        maximumCandidateBytes = 0;
        arrayPayloadBytes = 0;
        diagnostic = null;
        long totalRecordBytes = 0;

        for (var index = 0; index < operations.Length; index++)
        {
            var status = MeasureIntent(world, operations[index], limits, out var recordBytes, out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            totalRecordBytes = checked(totalRecordBytes + recordBytes);
            maximumCandidateBytes = Math.Max(maximumCandidateBytes, recordBytes);
        }

        if (totalRecordBytes > int.MaxValue)
        {
            diagnostic = CreateDiagnostic("control-scene-intent-storage");
            return EbinWriteStatus.InvalidRecord;
        }

        var arrayStatus = WorldPersistenceRecordArrayLayout.MeasurePayloadBytes(
            operations.Length,
            totalRecordBytes,
            limits,
            out arrayPayloadBytes,
            out diagnostic);

        if (arrayStatus is EbinWriteStatus.Complete)
        {
            recordStorageBytes = checked((int)totalRecordBytes);
        }

        return arrayStatus;
    }

    private static EbinWriteStatus MeasureRootRecord(
        scoped ReadOnlySpan<EbinFieldSource> fields,
        int rootPayloadBytes,
        EbinReadLimits limits,
        out int recordBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        if ((rootPayloadBytes <= 0) || (rootPayloadBytes > limits.MaximumRecordBytes))
        {
            recordBytes = 0;
            diagnostic = CreateDiagnostic("control-scene-root-payload-size");
            return EbinWriteStatus.InvalidRecord;
        }

        var cursor = checked(64UL + (checked((ulong)fields.Length) * 32UL));
        var previousTag = 0U;

        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            var fieldPayload = field.Payload;

            if ((field.Tag <= previousTag)
                || (fieldPayload.Offset < 0)
                || (fieldPayload.ByteLength < 0)
                || (fieldPayload.Offset > rootPayloadBytes)
                || (fieldPayload.ByteLength > (rootPayloadBytes - fieldPayload.Offset)))
            {
                recordBytes = 0;
                diagnostic = CreateDiagnostic("control-scene-root-field-layout");
                return EbinWriteStatus.InvalidRecord;
            }

            previousTag = field.Tag;
            cursor = checked((cursor + 15UL) & ~15UL);
            cursor = checked(cursor + (ulong)fieldPayload.ByteLength);
        }

        cursor = checked((cursor + 15UL) & ~15UL);

        if (cursor > (ulong)limits.MaximumRecordBytes)
        {
            recordBytes = 0;
            diagnostic = CreateDiagnostic("control-scene-root-record-size");
            return EbinWriteStatus.InvalidRecord;
        }

        recordBytes = checked((int)cursor);
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static void ValidateLayoutExtent(int actualPayloadBytes, int expectedPayloadBytes)
    {
        if (actualPayloadBytes != expectedPayloadBytes)
        {
            throw new InvalidOperationException("The Scene control payload extent changed after measurement.");
        }
    }

    private static EbinWriteStatus WriteIntent(
        World world,
        SceneTransactionOperation operation,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        bytesWritten = 0;
        var measureStatus = MeasureIntent(world, operation, limits, out var recordBytes, out diagnostic);

        if (measureStatus is not EbinWriteStatus.Complete)
        {
            return measureStatus;
        }

        if ((candidateStorage.Length != recordBytes) || (destination.Length != recordBytes))
        {
            throw new ArgumentException("The Scene control intent record workspaces must match the measured extent.");
        }

        var entityId = world.GetEntityId(operation.Entity);
        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[3];
        Span<byte> payloadStorage = stackalloc byte[40];
        payloadStorage.Clear();
        var fieldCount = 0;
        var payloadOffset = 0;
        Guid schemaId;
        EbinWriteStatus status;

        switch (operation.Kind)
        {
            case SceneTransactionOperationKind.Reparent:
            {
                schemaId = s_reparentIntentSchemaId;
                status = AddGuidField(1, entityId, required: true, fields, ref fieldCount, payloadStorage, ref payloadOffset, out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                if (operation.Parent is Entity parent)
                {
                    status = AddGuidField(
                        2,
                        world.GetEntityId(parent),
                        required: false,
                        fields,
                        ref fieldCount,
                        payloadStorage,
                        ref payloadOffset,
                        out diagnostic);

                    if (status is not EbinWriteStatus.Complete)
                    {
                        return status;
                    }
                }

                status = AddByteField(
                    3,
                    (byte)operation.Mode,
                    required: true,
                    fields,
                    ref fieldCount,
                    payloadStorage,
                    ref payloadOffset,
                    out diagnostic);
                break;
            }

            case SceneTransactionOperationKind.SetRootPosition:
            {
                schemaId = s_rootPositionIntentSchemaId;
                status = AddGuidField(1, entityId, required: true, fields, ref fieldCount, payloadStorage, ref payloadOffset, out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                status = EbinPayloadWriter.Write(
                    operation.RootPosition,
                    payloadStorage[payloadOffset..],
                    out var positionBytes,
                    out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                fields[fieldCount++] = new EbinFieldSource(
                    2,
                    21,
                    1,
                    1,
                    new EbinPayloadSlice(payloadOffset, positionBytes));
                payloadOffset = checked(payloadOffset + positionBytes);
                break;
            }

            default:
            {
                diagnostic = CreateDiagnostic("control-scene-intent-kind");
                return EbinWriteStatus.InvalidRecord;
            }
        }

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var source = new EbinRecordWriteView(
            schemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..payloadOffset]);
        return EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus WriteIntents(
        World world,
        scoped ReadOnlySpan<SceneTransactionOperation> operations,
        EbinReadLimits limits,
        scoped Span<EbinPayloadSlice> recordSlices,
        scoped Span<byte> recordStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> arrayPayloadDestination,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        payloadBytes = 0;

        if (recordSlices.Length != operations.Length)
        {
            throw new ArgumentException("The Scene control intent slices must match the exact intent count.", nameof(recordSlices));
        }

        var measureStatus = MeasureIntents(
            world,
            operations,
            limits,
            out var recordStorageBytes,
            out var maximumCandidateBytes,
            out var requiredArrayPayloadBytes,
            out diagnostic);

        if (measureStatus is not EbinWriteStatus.Complete)
        {
            return measureStatus;
        }

        if ((recordStorage.Length != recordStorageBytes)
            || (candidateStorage.Length != maximumCandidateBytes)
            || (arrayPayloadDestination.Length != requiredArrayPayloadBytes))
        {
            throw new ArgumentException("The Scene control intent workspaces must match their measured extents.");
        }

        var storageOffset = 0;

        for (var index = 0; index < operations.Length; index++)
        {
            var intentStatus = MeasureIntent(world, operations[index], limits, out var recordBytes, out diagnostic);

            if (intentStatus is not EbinWriteStatus.Complete)
            {
                return intentStatus;
            }

            var status = WriteIntent(
                world,
                operations[index],
                limits,
                candidateStorage[..recordBytes],
                recordStorage.Slice(storageOffset, recordBytes),
                out var bytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (bytesWritten != recordBytes)
            {
                throw new InvalidOperationException("A Scene control intent extent changed after measurement.");
            }

            recordSlices[index] = new EbinPayloadSlice(storageOffset, bytesWritten);
            storageOffset = checked(storageOffset + bytesWritten);
        }

        if (storageOffset != recordStorageBytes)
        {
            throw new InvalidOperationException("The Scene control intent array storage extent changed after measurement.");
        }

        var arrayStatus = EbinPayloadWriter.WriteRecordArray(
            recordSlices,
            recordStorage,
            limits,
            arrayPayloadDestination,
            out payloadBytes,
            out diagnostic);

        if ((arrayStatus is EbinWriteStatus.Complete) && (payloadBytes != requiredArrayPayloadBytes))
        {
            diagnostic = CreateDiagnostic("control-scene-intent-array-size");
            payloadBytes = 0;
            return EbinWriteStatus.InvalidRecord;
        }

        return arrayStatus;
    }

    private static EbinWriteStatus WriteRoot(
        scoped ReadOnlySpan<EbinFieldSource> fields,
        scoped ReadOnlySpan<byte> payloadStorage,
        ScenePersistenceControlRecordWorkspaceRequirements workspace,
        EbinReadLimits limits,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        bytesWritten = 0;

        if (payloadStorage.Length != workspace.RootPayloadBytes)
        {
            throw new ArgumentException("The state/control-scene payload workspace must match its measured extent.", nameof(payloadStorage));
        }

        var source = new EbinRecordWriteView(
            s_controlSchemaId,
            major: 1,
            minor: 0,
            fields,
            payloadStorage);
        var status = EbinStructuralWriter.Write(
            in source,
            limits,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);

        if ((status is EbinWriteStatus.Complete) && (bytesWritten != workspace.RecordBytes))
        {
            diagnostic = CreateDiagnostic("control-scene-record-size");
            bytesWritten = 0;
            return EbinWriteStatus.InvalidRecord;
        }

        return status;
    }
}
