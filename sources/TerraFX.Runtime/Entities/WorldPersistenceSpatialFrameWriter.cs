// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Entities;

internal static class WorldPersistenceSpatialFrameWriter
{
    private const int FieldCount = 6;
    private const int Frame2AdditionalPayloadBytes = 56;
    private const int Frame3AdditionalPayloadBytes = 72;
    private static readonly Guid s_frame2SchemaId = new ContentSchemaIdentityAssignment("foundation/frame2").SchemaId;
    private static readonly Guid s_frame3SchemaId = new ContentSchemaIdentityAssignment("foundation/frame3").SchemaId;

    internal static EbinWriteStatus MeasureFrame2(
        Guid worldGuid,
        uint ownerId,
        SpatialFrame2 frame,
        EbinReadLimits limits,
        out WorldPersistenceSpatialFrameWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        ValidateFrame2(worldGuid, ownerId, frame);

        var profileStatus = FoundationProfileWriter.Measure(
            frame.ProfileId,
            frame.Settings,
            limits,
            out var profileRecordBytes,
            out diagnostic);

        if (profileStatus is not EbinWriteStatus.Complete)
        {
            return profileStatus;
        }

        Span<byte> profileCandidateStorage = stackalloc byte[profileRecordBytes];
        Span<byte> profileStorage = stackalloc byte[profileRecordBytes];
        profileStatus = FoundationProfileWriter.Write(
            frame.ProfileId,
            frame.Settings,
            limits,
            profileCandidateStorage,
            profileStorage,
            out var profileBytesWritten,
            out diagnostic);

        if (profileStatus is not EbinWriteStatus.Complete)
        {
            return profileStatus;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[FieldCount];
        Span<byte> payloadStorage = stackalloc byte[checked(profileBytesWritten + Frame2AdditionalPayloadBytes)];
        var status = BuildFrame2Fields(
            worldGuid,
            in frame,
            profileStorage[..profileBytesWritten],
            fields,
            payloadStorage,
            out var fieldCount,
            out var payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var source = new EbinRecordWriteView(
            s_frame2SchemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..payloadBytes]);
        status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out diagnostic);

        if (status is EbinWriteStatus.Complete)
        {
            workspace = new WorldPersistenceSpatialFrameWorkspaceRequirements(
                profileRecordBytes,
                payloadBytes,
                recordBytes);
        }

        return status;
    }

    internal static EbinWriteStatus MeasureFrame3(
        Guid worldGuid,
        uint ownerId,
        SpatialFrame3 frame,
        EbinReadLimits limits,
        out WorldPersistenceSpatialFrameWorkspaceRequirements workspace,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        workspace = default;
        ValidateFrame3(worldGuid, ownerId, frame);

        var profileStatus = FoundationProfileWriter.Measure(
            frame.ProfileId,
            frame.Settings,
            limits,
            out var profileRecordBytes,
            out diagnostic);

        if (profileStatus is not EbinWriteStatus.Complete)
        {
            return profileStatus;
        }

        Span<byte> profileCandidateStorage = stackalloc byte[profileRecordBytes];
        Span<byte> profileStorage = stackalloc byte[profileRecordBytes];
        profileStatus = FoundationProfileWriter.Write(
            frame.ProfileId,
            frame.Settings,
            limits,
            profileCandidateStorage,
            profileStorage,
            out var profileBytesWritten,
            out diagnostic);

        if (profileStatus is not EbinWriteStatus.Complete)
        {
            return profileStatus;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[FieldCount];
        Span<byte> payloadStorage = stackalloc byte[checked(profileBytesWritten + Frame3AdditionalPayloadBytes)];
        var status = BuildFrame3Fields(
            worldGuid,
            in frame,
            profileStorage[..profileBytesWritten],
            fields,
            payloadStorage,
            out var fieldCount,
            out var payloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var source = new EbinRecordWriteView(
            s_frame3SchemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            payloadStorage[..payloadBytes]);
        status = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out diagnostic);

        if (status is EbinWriteStatus.Complete)
        {
            workspace = new WorldPersistenceSpatialFrameWorkspaceRequirements(
                profileRecordBytes,
                payloadBytes,
                recordBytes);
        }

        return status;
    }

    internal static EbinWriteStatus WriteFrame2(
        Guid worldGuid,
        uint ownerId,
        SpatialFrame2 frame,
        WorldPersistenceSpatialFrameWorkspaceRequirements workspace,
        EbinReadLimits limits,
        ref WorldPersistenceSpatialFrameBuffers buffers,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        bytesWritten = 0;
        diagnostic = null;
        ValidateFrame2(worldGuid, ownerId, frame);
        var status = FoundationProfileWriter.Write(
            frame.ProfileId,
            frame.Settings,
            limits,
            buffers.ProfileCandidateStorage,
            buffers.ProfileStorage,
            out var profileBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (profileBytesWritten != workspace.ProfileRecordBytes)
        {
            diagnostic = CreateDiagnostic("profile-record-length-changed", profileBytesWritten, workspace.ProfileRecordBytes);
            return EbinWriteStatus.InvalidRecord;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[FieldCount];
        var statusBuild = BuildFrame2Fields(
            worldGuid,
            in frame,
            buffers.ProfileStorage[..profileBytesWritten],
            fields,
            buffers.FramePayloadStorage,
            out var fieldCount,
            out var payloadBytes,
            out diagnostic);

        if (statusBuild is not EbinWriteStatus.Complete)
        {
            return statusBuild;
        }

        if (payloadBytes != workspace.FramePayloadStorageBytes)
        {
            diagnostic = CreateDiagnostic("frame-payload-length-changed", payloadBytes, workspace.FramePayloadStorageBytes);
            return EbinWriteStatus.InvalidRecord;
        }

        var source = new EbinRecordWriteView(
            s_frame2SchemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            buffers.FramePayloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Write(
            in source,
            limits,
            buffers.FrameCandidateStorage,
            buffers.Destination,
            out bytesWritten,
            out diagnostic);
    }

    internal static EbinWriteStatus WriteFrame3(
        Guid worldGuid,
        uint ownerId,
        SpatialFrame3 frame,
        WorldPersistenceSpatialFrameWorkspaceRequirements workspace,
        EbinReadLimits limits,
        ref WorldPersistenceSpatialFrameBuffers buffers,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);
        bytesWritten = 0;
        diagnostic = null;
        ValidateFrame3(worldGuid, ownerId, frame);
        var status = FoundationProfileWriter.Write(
            frame.ProfileId,
            frame.Settings,
            limits,
            buffers.ProfileCandidateStorage,
            buffers.ProfileStorage,
            out var profileBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (profileBytesWritten != workspace.ProfileRecordBytes)
        {
            diagnostic = CreateDiagnostic("profile-record-length-changed", profileBytesWritten, workspace.ProfileRecordBytes);
            return EbinWriteStatus.InvalidRecord;
        }

        Span<EbinFieldSource> fields = stackalloc EbinFieldSource[FieldCount];
        var statusBuild = BuildFrame3Fields(
            worldGuid,
            in frame,
            buffers.ProfileStorage[..profileBytesWritten],
            fields,
            buffers.FramePayloadStorage,
            out var fieldCount,
            out var payloadBytes,
            out diagnostic);

        if (statusBuild is not EbinWriteStatus.Complete)
        {
            return statusBuild;
        }

        if (payloadBytes != workspace.FramePayloadStorageBytes)
        {
            diagnostic = CreateDiagnostic("frame-payload-length-changed", payloadBytes, workspace.FramePayloadStorageBytes);
            return EbinWriteStatus.InvalidRecord;
        }

        var source = new EbinRecordWriteView(
            s_frame3SchemaId,
            major: 1,
            minor: 0,
            fields[..fieldCount],
            buffers.FramePayloadStorage[..payloadBytes]);
        return EbinStructuralWriter.Write(
            in source,
            limits,
            buffers.FrameCandidateStorage,
            buffers.Destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinWriteStatus AddGuidField(
        uint tag,
        Guid value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 11, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddRecordField(
        uint tag,
        scoped ReadOnlySpan<byte> record,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        if (record.Length > payloadStorage.Length - payloadBytes)
        {
            diagnostic = CreateDiagnostic(
                "workspace-too-small",
                payloadStorage.Length,
                checked(payloadBytes + record.Length));
            return EbinWriteStatus.WorkspaceTooSmall;
        }

        record.CopyTo(payloadStorage[payloadBytes..]);
        fields[fieldCount++] = new EbinFieldSource(tag, 22, 1, 1, new EbinPayloadSlice(payloadBytes, record.Length));
        payloadBytes = checked(payloadBytes + record.Length);
        diagnostic = null;
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddSpatialPosition2Field(
        uint tag,
        SpatialPosition2 value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 20, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddSpatialPosition3Field(
        uint tag,
        SpatialPosition3 value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 21, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddUInt64Field(
        uint tag,
        ulong value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 4, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddVector2Field(
        uint tag,
        Vector2 value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 14, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus AddVector3Field(
        uint tag,
        Vector3 value,
        Span<EbinFieldSource> fields,
        ref int fieldCount,
        Span<byte> payloadStorage,
        ref int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinPayloadWriter.Write(value, payloadStorage[payloadBytes..], out var fieldBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        fields[fieldCount++] = new EbinFieldSource(tag, 15, 1, 1, new EbinPayloadSlice(payloadBytes, fieldBytes));
        payloadBytes = checked(payloadBytes + fieldBytes);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus BuildFrame2Fields(
        Guid worldGuid,
        in SpatialFrame2 frame,
        scoped ReadOnlySpan<byte> profileRecord,
        Span<EbinFieldSource> fields,
        Span<byte> payloadStorage,
        out int fieldCount,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        payloadStorage.Clear();
        fieldCount = 0;
        payloadBytes = 0;

        var status = AddGuidField(1, worldGuid, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddRecordField(2, profileRecord, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddSpatialPosition2Field(3, frame.Origin, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt64Field(4, checked((ulong)frame.Revision), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddVector2Field(5, frame.Minimum, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        return AddVector2Field(6, frame.Maximum, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);
    }

    private static EbinWriteStatus BuildFrame3Fields(
        Guid worldGuid,
        in SpatialFrame3 frame,
        scoped ReadOnlySpan<byte> profileRecord,
        Span<EbinFieldSource> fields,
        Span<byte> payloadStorage,
        out int fieldCount,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        fields.Clear();
        payloadStorage.Clear();
        fieldCount = 0;
        payloadBytes = 0;

        var status = AddGuidField(1, worldGuid, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddRecordField(2, profileRecord, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddSpatialPosition3Field(3, frame.Origin, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddUInt64Field(4, checked((ulong)frame.Revision), fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = AddVector3Field(5, frame.Minimum, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        return AddVector3Field(6, frame.Maximum, fields, ref fieldCount, payloadStorage, ref payloadBytes, out diagnostic);
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, int claim, int limit)
        => new(
            Guid.Empty,
            0,
            category,
            null,
            checked((ulong)claim),
            checked((ulong)limit),
            EbinStructuralDiagnosticSource.Construction);

    private static void ValidateFrame2(Guid worldGuid, uint ownerId, SpatialFrame2 frame)
    {
        if (worldGuid == Guid.Empty)
        {
            throw new ArgumentException("The containing World identity cannot be empty.", nameof(worldGuid));
        }

        ArgumentOutOfRangeException.ThrowIfZero(ownerId);

        if (frame.OwnerId != ownerId)
        {
            throw new ArgumentException("The 2D frame belongs to another World owner.", nameof(frame));
        }
    }

    private static void ValidateFrame3(Guid worldGuid, uint ownerId, SpatialFrame3 frame)
    {
        if (worldGuid == Guid.Empty)
        {
            throw new ArgumentException("The containing World identity cannot be empty.", nameof(worldGuid));
        }

        ArgumentOutOfRangeException.ThrowIfZero(ownerId);

        if (frame.OwnerId != ownerId)
        {
            throw new ArgumentException("The 3D frame belongs to another World owner.", nameof(frame));
        }
    }
}
