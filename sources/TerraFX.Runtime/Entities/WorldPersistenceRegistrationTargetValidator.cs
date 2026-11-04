// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceRegistrationTargetValidator
{
    private const ushort ArrayKind = 24;
    private const ushort BooleanKind = 9;
    private const ushort FixedArrayKind = 23;
    private const ushort GuidKind = 11;
    private const ushort Int32Kind = 7;
    private const int MaximumSystemDependencyCount = 4095;
    private const ushort RecordKind = 22;
    private const ushort UInt32Kind = 3;
    private const ushort UInt8Kind = 1;

    private static readonly Guid s_accessSchemaId = new ContentSchemaIdentityAssignment("state/access").SchemaId;
    private static readonly Guid s_componentRegistrationSchemaId = new ContentSchemaIdentityAssignment("state/component-registration").SchemaId;
    private static readonly Guid s_partitionRangeSchemaId = new ContentSchemaIdentityAssignment("state/partition-range").SchemaId;
    private static readonly Guid s_resourceRegistrationSchemaId = new ContentSchemaIdentityAssignment("state/resource-registration").SchemaId;
    private static readonly Guid s_schemaReferenceSchemaId = new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId;
    private static readonly Guid s_systemRegistrationSchemaId = new ContentSchemaIdentityAssignment("state/system-registration").SchemaId;

    internal static void Validate(
        EbinRecordReadView record,
        WorldRegistration targetRegistration,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(targetRegistration);
        var savedComponentCount = GetRequiredField(record, 1, ArrayKind, expectedCount: null, parameterName).Count;
        var savedTagCount = GetRequiredField(record, 2, ArrayKind, expectedCount: null, parameterName).Count;
        var savedSystemCount = GetRequiredField(record, 3, ArrayKind, expectedCount: null, parameterName).Count;
        var savedResourceCount = GetRequiredField(record, 4, ArrayKind, expectedCount: null, parameterName).Count;
        var componentCount = targetRegistration.Components.Length;
        var tagCount = targetRegistration.Tags.Length;
        var systemCount = targetRegistration.Systems.Length;
        var resourceCount = targetRegistration.Resources.Length;

        if ((savedComponentCount != checked((ulong)componentCount))
            || (savedTagCount != checked((ulong)tagCount))
            || (savedSystemCount != checked((ulong)systemCount))
            || (savedResourceCount != checked((ulong)resourceCount)))
        {
            throw new ArgumentException("The saved registration counts do not match the target WorldRegistration.", parameterName);
        }

        Span<int> systemOrder = stackalloc int[systemCount];
        targetRegistration.CopyPersistenceSystemOrder(systemOrder);

        for (var index = 0; index < componentCount; index++)
        {
            ValidateComponentRegistration(
                record.GetRecordArrayElement(1, index),
                targetRegistration.Components[index].Descriptor,
                parameterName);
        }

        for (var index = 0; index < tagCount; index++)
        {
            ValidateSchemaReference(
                record.GetRecordArrayElement(2, index),
                targetRegistration.Tags[index].Schema,
                parameterName);
        }

        for (var index = 0; index < systemCount; index++)
        {
            var system = targetRegistration.Systems[systemOrder[index]];
            ValidateSystemRegistration(
                record.GetRecordArrayElement(3, index),
                system.Description,
                system.Factory.Descriptor,
                parameterName);
        }

        for (var index = 0; index < resourceCount; index++)
        {
            ValidateResourceRegistration(
                record.GetRecordArrayElement(4, index),
                targetRegistration.Resources[index].Descriptor,
                parameterName);
        }
    }

    private static EbinFieldReadView GetRequiredField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        ulong? expectedCount,
        string parameterName)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != kind)
            || !field.IsRequired
            || (expectedCount.HasValue && (field.Count != expectedCount.Value)))
        {
            throw new ArgumentException("A state/registration field has an invalid wire shape.", parameterName);
        }

        return field;
    }

    private static bool ReadBoolean(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, BooleanKind, expectedCount: 1, parameterName);

        if ((field.Payload.Length != sizeof(byte)) || (field.Payload[0] > 1))
        {
            throw new ArgumentException("A state/registration Boolean has an invalid payload.", parameterName);
        }

        return field.Payload[0] != 0;
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, GuidKind, expectedCount: 1, parameterName);

        if (field.Payload.Length != 16)
        {
            throw new ArgumentException("A state/registration GUID has an invalid payload length.", parameterName);
        }

        return new Guid(field.Payload, bigEndian: true);
    }

    private static int ReadInt32(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, Int32Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(int))
        {
            throw new ArgumentException("A state/registration Int32 has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadInt32LittleEndian(field.Payload);
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt32Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(uint))
        {
            throw new ArgumentException("A state/registration UInt32 has an invalid payload length.", parameterName);
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt8Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(byte))
        {
            throw new ArgumentException("A state/registration UInt8 has an invalid payload length.", parameterName);
        }

        return field.Payload[0];
    }

    private static void ValidateAccessArray(
        EbinRecordReadView systemRecord,
        SystemDescription description,
        string parameterName)
    {
        var accessField = GetRequiredField(
            systemRecord,
            5,
            ArrayKind,
            checked((ulong)description.Access.Length),
            parameterName);
        Span<SystemResourceAccess> expectedAccesses = stackalloc SystemResourceAccess[description.Access.Length];
        WorldPersistenceSystemRegistrationWriter.CopySortedAccesses(description.Access.AsSpan(), expectedAccesses);

        for (var index = 0; index < expectedAccesses.Length; index++)
        {
            var actual = systemRecord.GetRecordArrayElement(5, index);
            var expected = expectedAccesses[index];
            ValidateRecordHeader(actual, s_accessSchemaId, minimumFieldCount: 3, maximumFieldCount: 4, parameterName);

            if ((ReadUInt8(actual, 1, parameterName) != (byte)expected.Kind)
                || (ReadGuid(actual, 2, parameterName) != expected.ResourceId)
                || (ReadUInt8(actual, 3, parameterName) != (byte)expected.Mode))
            {
                throw new ArgumentException("A saved system access does not match the target SystemDescription.", parameterName);
            }

            var hasRange = actual.TryGetField(4, out var rangeField);

            if (expected.Range is SystemPartitionRange expectedRange)
            {
                if ((actual.FieldCount != 4)
                    || (rangeField.Kind != RecordKind)
                    || rangeField.IsRequired
                    || (rangeField.Count != 1))
                {
                    throw new ArgumentException("A saved system access partition range has an invalid wire shape.", parameterName);
                }

                var range = new EbinRecordReadView(rangeField.Payload);
                ValidateRecordHeader(range, s_partitionRangeSchemaId, 2, 2, parameterName);

                if ((ReadUInt32(range, 1, parameterName) != checked((uint)expectedRange.Start))
                    || (ReadUInt32(range, 2, parameterName) != checked((uint)expectedRange.Length)))
                {
                    throw new ArgumentException("A saved system access partition range does not match the target SystemDescription.", parameterName);
                }
            }
            else if (hasRange || (actual.FieldCount != 3))
            {
                throw new ArgumentException("A saved system access has an unexpected partition range.", parameterName);
            }
        }
    }

    private static void ValidateComponentRegistration(
        EbinRecordReadView record,
        ComponentFactoryDescriptor descriptor,
        string parameterName)
    {
        ValidateRecordHeader(
            record,
            s_componentRegistrationSchemaId,
            5,
            5,
            parameterName);
        var schemaField = GetRequiredField(record, 1, RecordKind, expectedCount: 1, parameterName);
        ValidateSchemaReference(new EbinRecordReadView(schemaField.Payload), descriptor.Schema, parameterName);

        if ((ReadUInt8(record, 2, parameterName) != (byte)descriptor.Flags)
            || (ReadUInt8(record, 3, parameterName) != (byte)descriptor.Restore)
            || (ReadGuid(record, 4, parameterName) != descriptor.FactoryId)
            || (ReadUInt32(record, 5, parameterName) != checked((uint)descriptor.FactoryVersion)))
        {
            throw new ArgumentException("A saved component registration does not match the target factory descriptor.", parameterName);
        }
    }

    private static void ValidateGuidArray(
        EbinFieldReadView field,
        ReadOnlySpan<Guid> expected,
        string parameterName)
    {
        var expectedLength = checked(32L + (16L * expected.Length));

        if ((field.Kind != FixedArrayKind)
            || !field.IsRequired
            || (field.Count != checked((ulong)expected.Length))
            || (field.Payload.Length != expectedLength)
            || (BinaryPrimitives.ReadUInt16LittleEndian(field.Payload[16..]) != GuidKind)
            || (BinaryPrimitives.ReadUInt32LittleEndian(field.Payload[20..]) != 16))
        {
            throw new ArgumentException("A state/registration GUID array has an invalid wire shape.", parameterName);
        }

        for (var index = 0; index < expected.Length; index++)
        {
            var offset = checked(32 + (index * 16));

            if (new Guid(field.Payload.Slice(offset, 16), bigEndian: true) != expected[index])
            {
                throw new ArgumentException("A saved system dependency order does not match the target SystemDescription.", parameterName);
            }
        }
    }

    private static void ValidateRecordHeader(
        EbinRecordReadView record,
        Guid schemaId,
        int minimumFieldCount,
        int maximumFieldCount,
        string parameterName)
    {
        if ((record.Header.SchemaId != schemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount < minimumFieldCount)
            || (record.FieldCount > maximumFieldCount))
        {
            throw new ArgumentException("A state/registration child has the wrong schema identity, version, or field count.", parameterName);
        }
    }

    private static void ValidateResourceRegistration(
        EbinRecordReadView record,
        WorldResourceRegistrationDescriptor descriptor,
        string parameterName)
    {
        ValidateRecordHeader(
            record,
            s_resourceRegistrationSchemaId,
            6,
            6,
            parameterName);
        var schemaField = GetRequiredField(record, 4, RecordKind, expectedCount: 1, parameterName);
        ValidateSchemaReference(
            new EbinRecordReadView(schemaField.Payload),
            descriptor.Factory.Schema,
            parameterName);

        if ((ReadGuid(record, 1, parameterName) != descriptor.ResourceId)
            || (ReadUInt32(record, 2, parameterName) != checked((uint)descriptor.PartitionCount))
            || (ReadBoolean(record, 3, parameterName) != descriptor.AllowParallelAccess)
            || (ReadGuid(record, 5, parameterName) != descriptor.Factory.FactoryId)
            || (ReadUInt32(record, 6, parameterName) != checked((uint)descriptor.Factory.FactoryVersion)))
        {
            throw new ArgumentException("A saved resource registration does not match the target factory descriptor.", parameterName);
        }
    }

    private static void ValidateSchemaReference(
        EbinRecordReadView record,
        ContentSchemaReference expected,
        string parameterName)
    {
        ValidateRecordHeader(record, s_schemaReferenceSchemaId, 4, 4, parameterName);

        if ((ReadGuid(record, 1, parameterName) != expected.SchemaId)
            || (ReadUInt32(record, 2, parameterName) != checked((uint)expected.Major))
            || (ReadUInt32(record, 3, parameterName) != checked((uint)expected.Minor))
            || (ReadUInt8(record, 4, parameterName) != (byte)expected.Scope))
        {
            throw new ArgumentException("A saved schema reference does not match the target WorldRegistration.", parameterName);
        }
    }

    private static void ValidateSystemRegistration(
        EbinRecordReadView record,
        SystemDescription description,
        WorldSystemFactoryDescriptor factory,
        string parameterName)
    {
        var hasStateSchema = factory.StateSchema.HasValue;
        ValidateRecordHeader(
            record,
            s_systemRegistrationSchemaId,
            minimumFieldCount: hasStateSchema ? 10 : 9,
            maximumFieldCount: hasStateSchema ? 10 : 9,
            parameterName);
        ValidateAccessArray(record, description, parameterName);

        Span<Guid> dependencyScratch = stackalloc Guid[MaximumSystemDependencyCount];
        var before = dependencyScratch[..description.Before.Length];
        WorldPersistenceSystemRegistrationWriter.CopySortedGuids(description.Before.AsSpan(), before);
        ValidateGuidArray(
            GetRequiredField(record, 6, FixedArrayKind, checked((ulong)description.Before.Length), parameterName),
            before,
            parameterName);
        var after = dependencyScratch[..description.After.Length];
        WorldPersistenceSystemRegistrationWriter.CopySortedGuids(description.After.AsSpan(), after);
        ValidateGuidArray(
            GetRequiredField(record, 7, FixedArrayKind, checked((ulong)description.After.Length), parameterName),
            after,
            parameterName);

        if ((ReadGuid(record, 1, parameterName) != description.SystemId)
            || (ReadInt32(record, 2, parameterName) != description.Order)
            || (ReadUInt8(record, 3, parameterName) != (byte)description.Stage)
            || (ReadUInt8(record, 4, parameterName) != (byte)description.ThreadMode)
            || (ReadGuid(record, 8, parameterName) != factory.FactoryId)
            || (ReadUInt32(record, 9, parameterName) != checked((uint)factory.FactoryVersion)))
        {
            throw new ArgumentException("A saved system registration does not match the target SystemDescription or factory.", parameterName);
        }

        var hasSavedStateSchema = record.TryGetField(10, out var stateSchemaField);

        if (factory.StateSchema is ContentSchemaReference stateSchema)
        {
            if (!hasSavedStateSchema
                || (stateSchemaField.Kind != RecordKind)
                || stateSchemaField.IsRequired
                || (stateSchemaField.Count != 1))
            {
                throw new ArgumentException("A saved system state schema has an invalid wire shape.", parameterName);
            }

            ValidateSchemaReference(new EbinRecordReadView(stateSchemaField.Payload), stateSchema, parameterName);
        }
        else if (hasSavedStateSchema)
        {
            throw new ArgumentException("A saved stateless system unexpectedly declares a state schema.", parameterName);
        }
    }
}
