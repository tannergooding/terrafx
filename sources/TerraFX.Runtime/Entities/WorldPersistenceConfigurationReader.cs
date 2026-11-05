// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceConfigurationReader
{
    private const int ConfigurationFieldCount = 16;
    private const ushort Int64Kind = 8;
    private const ushort RecordKind = 22;
    private const int SchedulingLimitsFieldCount = 7;
    private const ushort UInt16Kind = 2;
    private const ushort UInt32Kind = 3;
    private const ushort UInt8Kind = 1;

    private static readonly Guid s_configurationSchemaId = new ContentSchemaIdentityAssignment("state/configuration").SchemaId;
    private static readonly Guid s_schedulingLimitsSchemaId = new ContentSchemaIdentityAssignment("state/scheduling-limits").SchemaId;

    internal static WorldPersistenceConfiguration Read(
        ReadOnlyMemory<byte> configurationRecord,
        EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        var status = EbinStructuralReader.Validate(
            configurationRecord.Span,
            limits,
            EbinReadPolicy.Canonical,
            out var bytesConsumed,
            out _);

        if ((status is not EbinReadStatus.Complete)
            || (bytesConsumed != configurationRecord.Length))
        {
            throw new ArgumentException("The state/configuration record is not a complete canonical EBIN record.", nameof(configurationRecord));
        }

        var record = new EbinRecordReadView(configurationRecord.Span);

        if ((record.Header.SchemaId != s_configurationSchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != ConfigurationFieldCount))
        {
            throw new ArgumentException("The state/configuration record has the wrong schema identity, version, or field count.", nameof(configurationRecord));
        }

        var maximumLiveEntities = ReadBoundedUInt32(record, 1, 1_048_576, nameof(configurationRecord));
        var maximumEntitySlots = ReadBoundedUInt32(record, 2, 2_097_152, nameof(configurationRecord));
        var maximumArchetypes = ReadBoundedUInt32(record, 3, 4096, nameof(configurationRecord));
        var maximumDeferredCommands = ReadBoundedUInt32(record, 4, 65_536, nameof(configurationRecord));
        var maximumComponentTypes = ReadBoundedUInt32(record, 5, 256, nameof(configurationRecord));
        var maximumPendingOwnerRequests = ReadBoundedUInt32(record, 6, 128, nameof(configurationRecord));
        var maximumRecordedControls = ReadBoundedUInt32(record, 7, 65_536, nameof(configurationRecord));
        var maximumIdentityAssignments = ReadBoundedUInt32(record, 8, 33_554_432, nameof(configurationRecord));
        _ = GetRequiredField(record, 9, RecordKind, nameof(configurationRecord));
        var schedulingLimits = ReadSchedulingLimits(record.GetRecord(9), nameof(configurationRecord));
        var executionOwner = ReadUInt8(record, 10, nameof(configurationRecord));
        var maximumIdentityBranches = ReadBoundedUInt16(record, 11, 1024, nameof(configurationRecord));
        var maximumEntityReferencePositions = ReadBoundedUInt32(record, 12, 4_194_304, nameof(configurationRecord));
        var maximumActiveSceneInstances = ReadBoundedUInt32(record, 13, 65_536, nameof(configurationRecord));
        var maximumSceneOccurrences = ReadBoundedUInt32(record, 14, 1_048_576, nameof(configurationRecord));
        var maximumSnapshotBytes = ReadBoundedInt64(record, 15, 64, 2_147_483_584, nameof(configurationRecord));
        var maximumPersistenceAdmissionBytes = ReadBoundedInt64(record, 16, 4096, 8_589_934_592, nameof(configurationRecord));

        if (executionOwner != 0)
        {
            throw new ArgumentException("The state/configuration record has an unsupported scheduling field or execution owner.", nameof(configurationRecord));
        }

        return new WorldPersistenceConfiguration(
            maximumLiveEntities,
            maximumEntitySlots,
            maximumArchetypes,
            maximumDeferredCommands,
            maximumComponentTypes,
            maximumPendingOwnerRequests,
            maximumRecordedControls,
            maximumIdentityAssignments,
            schedulingLimits,
            maximumIdentityBranches,
            maximumEntityReferencePositions,
            maximumActiveSceneInstances,
            maximumSceneOccurrences,
            maximumSnapshotBytes,
            maximumPersistenceAdmissionBytes);
    }

    internal static int ReadMaximumIdentityAssignments(EbinRecordReadView record, string parameterName)
    {
        if ((record.Header.SchemaId != s_configurationSchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != ConfigurationFieldCount))
        {
            throw new ArgumentException("The state/configuration record has the wrong schema identity, version, or field count.", parameterName);
        }

        return ReadBoundedUInt32(record, 8, 33_554_432, parameterName);
    }

    internal static int ReadMaximumIdentityBranches(EbinRecordReadView record, string parameterName)
    {
        if ((record.Header.SchemaId != s_configurationSchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != ConfigurationFieldCount))
        {
            throw new ArgumentException("The state/configuration record has the wrong schema identity, version, or field count.", parameterName);
        }

        return ReadBoundedUInt16(record, 11, 1024, parameterName);
    }

    private static EbinFieldReadView GetRequiredField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        string parameterName)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != kind)
            || !field.IsRequired
            || (field.Count != 1))
        {
            throw new ArgumentException("A state/configuration field has an invalid wire shape.", parameterName);
        }

        return field;
    }

    private static long ReadBoundedInt64(
        EbinRecordReadView record,
        uint tag,
        long minimum,
        long maximum,
        string parameterName)
    {
        var value = ReadInt64(record, tag, parameterName);

        if ((value < minimum) || (value > maximum))
        {
            throw new ArgumentException("A state/configuration byte limit is outside its supported range.", parameterName);
        }

        return value;
    }

    private static int ReadBoundedUInt16(
        EbinRecordReadView record,
        uint tag,
        ushort maximum,
        string parameterName)
    {
        var value = ReadUInt16(record, tag, parameterName);

        if ((value == 0) || (value > maximum))
        {
            throw new ArgumentException("A state/configuration count is outside its supported range.", parameterName);
        }

        return value;
    }

    private static int ReadBoundedUInt32(
        EbinRecordReadView record,
        uint tag,
        uint maximum,
        string parameterName)
    {
        var value = ReadUInt32(record, tag, parameterName);

        if ((value == 0) || (value > maximum))
        {
            throw new ArgumentException("A state/configuration count is outside its supported range.", parameterName);
        }

        return checked((int)value);
    }

    private static long ReadInt64(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, Int64Kind, parameterName);

        if (field.Payload.Length != sizeof(long))
        {
            throw new ArgumentException("A state/configuration Int64 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
    }

    private static WorldSchedulingLimits ReadSchedulingLimits(
        EbinRecordReadView record,
        string parameterName)
    {
        if ((record.Header.SchemaId != s_schedulingLimitsSchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != SchedulingLimitsFieldCount))
        {
            throw new ArgumentException("The state/scheduling-limits record has the wrong schema identity, version, or field count.", parameterName);
        }

        var maximumRegistrationBytes = ReadInt64(record, 1, parameterName);
        var maximumGraphBytes = ReadInt64(record, 2, parameterName);
        var maximumWorkPlanBytes = ReadInt64(record, 3, parameterName);
        var maximumCommandStagingBytes = ReadInt64(record, 4, parameterName);
        var maximumOwnedStateBytes = ReadInt64(record, 5, parameterName);
        var maximumDiagnosticBytes = ReadInt64(record, 6, parameterName);
        var maximumConflictComparisons = ReadUInt32(record, 7, parameterName);

        if (maximumConflictComparisons > 16_777_216)
        {
            throw new ArgumentException("The state/scheduling-limits conflict budget exceeds its supported range.", parameterName);
        }

        return new WorldSchedulingLimits(
            maximumRegistrationBytes,
            maximumGraphBytes,
            maximumWorkPlanBytes,
            maximumCommandStagingBytes,
            maximumOwnedStateBytes,
            maximumDiagnosticBytes,
            checked((int)maximumConflictComparisons));
    }

    private static ushort ReadUInt16(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt16Kind, parameterName);

        if (field.Payload.Length != sizeof(ushort))
        {
            throw new ArgumentException("A state/configuration UInt16 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(field.Payload);
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt32Kind, parameterName);

        if (field.Payload.Length != sizeof(uint))
        {
            throw new ArgumentException("A state/configuration UInt32 field has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt8Kind, parameterName);

        if (field.Payload.Length != sizeof(byte))
        {
            throw new ArgumentException("A state/configuration UInt8 field has an invalid payload length.", parameterName);
        }

        return field.Payload[0];
    }
}
