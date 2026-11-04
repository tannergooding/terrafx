// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceRegistrationReader
{
    private const ushort ArrayKind = 24;
    private const ushort RecordKind = 22;
    private const int RegistrationFieldCount = 4;
    private const int SchemaReferenceFieldCount = 4;
    private static readonly Guid s_componentRegistrationSchemaId = new ContentSchemaIdentityAssignment("state/component-registration").SchemaId;
    private static readonly Guid s_registrationSchemaId = new ContentSchemaIdentityAssignment("state/registration").SchemaId;
    private static readonly Guid s_resourceRegistrationSchemaId = new ContentSchemaIdentityAssignment("state/resource-registration").SchemaId;
    private static readonly Guid s_schemaReferenceSchemaId = new ContentSchemaIdentityAssignment("content/schema-ref").SchemaId;
    private static readonly Guid s_systemRegistrationSchemaId = new ContentSchemaIdentityAssignment("state/system-registration").SchemaId;

    internal static WorldPersistenceRegistrationContents Read(
        ReadOnlyMemory<byte> registrationRecord,
        EbinReadLimits limits,
        int maximumComponentTypes,
        WorldRegistration? targetRegistration = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumComponentTypes);

        var status = EbinStructuralReader.Validate(
            registrationRecord.Span,
            limits,
            EbinReadPolicy.Canonical,
            out var bytesConsumed,
            out _);

        if ((status is not EbinReadStatus.Complete)
            || (bytesConsumed != registrationRecord.Length))
        {
            throw new ArgumentException("The state/registration record is not a complete canonical EBIN record.", nameof(registrationRecord));
        }

        var record = new EbinRecordReadView(registrationRecord.Span);

        if ((record.Header.SchemaId != s_registrationSchemaId)
            || (record.Header.Major != 1)
            || (record.Header.Minor != 0)
            || (record.FieldCount != RegistrationFieldCount))
        {
            throw new ArgumentException("The state/registration record has the wrong schema identity, version, or field count.", nameof(registrationRecord));
        }

        var componentCount = ValidateRecordArray(
            record,
            1,
            s_componentRegistrationSchemaId,
            maximumCount: 256,
            minimumFieldCount: 5,
            maximumFieldCount: 5,
            nameof(registrationRecord));
        var tagCount = ValidateRecordArray(
            record,
            2,
            s_schemaReferenceSchemaId,
            maximumCount: 256,
            minimumFieldCount: SchemaReferenceFieldCount,
            maximumFieldCount: SchemaReferenceFieldCount,
            nameof(registrationRecord));
        var systemCount = ValidateRecordArray(
            record,
            3,
            s_systemRegistrationSchemaId,
            maximumCount: 4096,
            minimumFieldCount: 9,
            maximumFieldCount: 10,
            nameof(registrationRecord));
        var resourceCount = ValidateRecordArray(
            record,
            4,
            s_resourceRegistrationSchemaId,
            maximumCount: 4096,
            minimumFieldCount: 6,
            maximumFieldCount: 6,
            nameof(registrationRecord));

        if ((componentCount + tagCount) > maximumComponentTypes)
        {
            throw new ArgumentException("The saved registration exceeds the configured component/tag identity limit.", nameof(registrationRecord));
        }

        if (targetRegistration is not null)
        {
            WorldPersistenceRegistrationTargetValidator.Validate(record, targetRegistration, nameof(registrationRecord));
        }

        return new WorldPersistenceRegistrationContents(componentCount, tagCount, systemCount, resourceCount);
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

    private static int ValidateRecordArray(
        EbinRecordReadView registrationRecord,
        uint fieldTag,
        Guid elementSchemaId,
        int maximumCount,
        int minimumFieldCount,
        int maximumFieldCount,
        string parameterName)
    {
        var field = GetRequiredField(registrationRecord, fieldTag, ArrayKind, expectedCount: null, parameterName);

        if ((field.Count > (ulong)maximumCount) || (field.Count > int.MaxValue))
        {
            throw new ArgumentException("A state/registration array exceeds its supported count.", parameterName);
        }

        var count = checked((int)field.Count);

        for (var index = 0; index < count; index++)
        {
            var element = registrationRecord.GetRecordArrayElement(fieldTag, index);
            ValidateRecordHeader(element, elementSchemaId, minimumFieldCount, maximumFieldCount, parameterName);

            if (elementSchemaId == s_componentRegistrationSchemaId)
            {
                var schemaField = GetRequiredField(element, 1, RecordKind, expectedCount: 1, parameterName);
                ValidateRecordHeader(
                    new EbinRecordReadView(schemaField.Payload),
                    s_schemaReferenceSchemaId,
                    SchemaReferenceFieldCount,
                    SchemaReferenceFieldCount,
                    parameterName);
            }
            else if (elementSchemaId == s_systemRegistrationSchemaId
                && element.TryGetField(10, out var stateSchemaField))
            {
                if ((stateSchemaField.Kind != RecordKind)
                    || stateSchemaField.IsRequired
                    || (stateSchemaField.Count != 1))
                {
                    throw new ArgumentException("A state/system-registration StateSchema field has an invalid wire shape.", parameterName);
                }

                ValidateRecordHeader(
                    new EbinRecordReadView(stateSchemaField.Payload),
                    s_schemaReferenceSchemaId,
                    SchemaReferenceFieldCount,
                    SchemaReferenceFieldCount,
                    parameterName);
            }
            else if (elementSchemaId == s_resourceRegistrationSchemaId)
            {
                var schemaField = GetRequiredField(element, 4, RecordKind, expectedCount: 1, parameterName);
                ValidateRecordHeader(
                    new EbinRecordReadView(schemaField.Payload),
                    s_schemaReferenceSchemaId,
                    SchemaReferenceFieldCount,
                    SchemaReferenceFieldCount,
                    parameterName);
            }
        }

        return count;
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
}
