// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal static class WorldPersistenceSceneControlReader
{
    private const byte ActivateKind = 0;
    private const ushort ArrayKind = 24;
    private const byte AutomaticOrigin = 1;
    private const byte EditKind = 2;
    private const byte EntityIdentityKind = 1;
    private const ushort GuidKind = 11;
    private const ushort RecordKind = 22;
    private const byte SceneInstanceIdentityKind = 2;
    private const ushort UInt32Kind = 3;
    private const ushort UInt8Kind = 1;
    private const byte UnloadKind = 1;

    private static readonly Guid s_externalBindingSchemaId = new ContentSchemaIdentityAssignment("state/external-scene-binding").SchemaId;
    private static readonly Guid s_nativeAssetReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;
    private static readonly Guid s_reparentIntentSchemaId = new ContentSchemaIdentityAssignment("scene/intent-reparent").SchemaId;
    private static readonly Guid s_rootPositionIntentSchemaId = new ContentSchemaIdentityAssignment("scene/intent-root-position").SchemaId;
    private static readonly Guid s_sceneControlSchemaId = new ContentSchemaIdentityAssignment("state/control-scene").SchemaId;
    private static readonly Guid s_sceneSchemaId = new ContentSchemaIdentityAssignment("native/scene").SchemaId;
    private static readonly Guid s_scenesSchemaId = new ContentSchemaIdentityAssignment("state/scenes").SchemaId;
    private static readonly Guid s_sceneInstanceSchemaId = new ContentSchemaIdentityAssignment("state/scene-instance").SchemaId;

    internal static void Validate(
        EbinRecordReadView worldRecord,
        ReadOnlyMemory<byte> scenesRecord,
        int controlCount,
        scoped Span<WorldPersistenceIdentityScratchEntry> identityScratch,
        string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(controlCount);
        ArgumentNullException.ThrowIfNull(parameterName);
        var worldBytes = worldRecord.EncodedBytes;
        var identityHistory = worldRecord.GetRecord(5).GetRecord(5);
        var worldGuid = ReadGuid(identityHistory, 1, parameterName);
        var activationCount = 0;
        var activeCount = 0;

        for (var controlIndex = 0; controlIndex < controlCount; controlIndex++)
        {
            var control = worldRecord.GetRecordArrayElement(8, controlIndex);
            var payload = control.GetRecord(3);

            if (payload.Header.SchemaId != s_sceneControlSchemaId)
            {
                continue;
            }

            ValidateRecordHeader(payload, s_sceneControlSchemaId, minimumFieldCount: 3, maximumFieldCount: 6, parameterName);
            var kind = ReadByte(payload, 1, parameterName);
            var sceneInstanceId = ReadGuid(payload, 2, parameterName);
            var identityIndex = WorldPersistenceIdentityScratchEntry.FindIndex(identityScratch, sceneInstanceId);

            if ((identityIndex < 0) || (identityScratch[identityIndex].Kind != SceneInstanceIdentityKind))
            {
                throw new ArgumentException("A state/control-scene names no historical SceneInstance assignment.", parameterName);
            }

            switch (kind)
            {
                case ActivateKind:
                {
                    ValidateActivation(
                        payload,
                        sceneInstanceId,
                        identityScratch[identityIndex],
                        identityHistory,
                        worldGuid,
                        identityScratch,
                        parameterName);
                    var controlOffset = GetWorldRecordOffset(worldBytes, payload.EncodedBytes, parameterName);

                    var identity = identityScratch[identityIndex];

                    if (!identity.TryMarkSceneControlActive(controlOffset, payload.EncodedBytes.Length))
                    {
                        throw new ArgumentException("A SceneInstance history row has duplicate activation controls.", parameterName);
                    }

                    identityScratch[identityIndex] = identity;
                    activationCount = checked(activationCount + 1);
                    activeCount = checked(activeCount + 1);
                    break;
                }

                case UnloadKind:
                {
                    ValidateUnload(payload, parameterName);

                    var identity = identityScratch[identityIndex];

                    if (!identity.TryMarkSceneControlUnloaded())
                    {
                        throw new ArgumentException("A state/control-scene unload does not name an active SceneInstance.", parameterName);
                    }

                    identityScratch[identityIndex] = identity;
                    activeCount--;
                    break;
                }

                case EditKind:
                {
                    ValidateEdit(payload, identityScratch, parameterName);

                    if (!identityScratch[identityIndex].IsSceneControlActive)
                    {
                        throw new ArgumentException("A state/control-scene edit does not name an active SceneInstance.", parameterName);
                    }

                    break;
                }

                default:
                {
                    throw new ArgumentException("A state/control-scene has an unknown operation kind.", parameterName);
                }
            }
        }

        var sceneIdentityCount = 0;
        var activeIdentityCount = 0;

        for (var index = 0; index < identityScratch.Length; index++)
        {
            var identity = identityScratch[index];

            if (identity.Kind != SceneInstanceIdentityKind)
            {
                continue;
            }

            sceneIdentityCount = checked(sceneIdentityCount + 1);

            if (!identity.HasSceneControlActivation)
            {
                throw new ArgumentException("A historical SceneInstance assignment has no activation control.", parameterName);
            }

            if (identity.IsSceneControlActive)
            {
                activeIdentityCount = checked(activeIdentityCount + 1);
            }
        }

        if ((activationCount != sceneIdentityCount)
            || (activeIdentityCount != activeCount))
        {
            throw new ArgumentException("The state/control-scene scan does not cover the complete SceneInstance history.", parameterName);
        }

        ValidateFinalScenes(worldBytes, scenesRecord, identityScratch, activeIdentityCount, parameterName);
    }

    internal static void ValidateActivationIdentity(
        Guid sceneInstanceId,
        Guid originalSceneInstanceId,
        Guid? requestedSceneInstanceId,
        Guid worldGuid,
        WorldPersistenceIdentityScratchEntry identity,
        string parameterName)
    {
        if (originalSceneInstanceId == Guid.Empty)
        {
            throw new ArgumentException("A state/control-scene original identity cannot be empty.", parameterName);
        }

        if (requestedSceneInstanceId is { } requested
            && ((requested == Guid.Empty)
                || IsReservedUuidV8(requested)
                || (requested != originalSceneInstanceId)))
        {
            throw new ArgumentException("A requested SceneInstance identity does not preserve its original request fact.", parameterName);
        }

        if (identity.Origin is AutomaticOrigin or (byte)IdentityOrigin.Explicit)
        {
            var isExplicit = identity.Origin == (byte)IdentityOrigin.Explicit;

            if ((sceneInstanceId != originalSceneInstanceId)
                || (requestedSceneInstanceId.HasValue != isExplicit))
            {
                throw new ArgumentException("An unforked Scene activation does not match its original identity request.", parameterName);
            }

            return;
        }

        if (identity.Origin == (byte)IdentityOrigin.ForkImported)
        {
            var expectedIdentity = StableIdentityLedger.CreateAutomaticIdentity(
                worldGuid,
                IdentityKind.SceneInstance,
                identity.AssignmentOrdinal);
            var expectedOriginalIdentity = requestedSceneInstanceId
                ?? StableIdentityLedger.CreateAutomaticIdentity(
                    identity.SourceIdentityNamespace,
                    IdentityKind.SceneInstance,
                    identity.AssignmentOrdinal);

            if ((sceneInstanceId != expectedIdentity)
                || (originalSceneInstanceId != expectedOriginalIdentity))
            {
                throw new ArgumentException("A fork-imported SceneInstance does not preserve its source and target identity recipes.", parameterName);
            }

            return;
        }

        throw new ArgumentException("A SceneInstance assignment has an unsupported identity origin.", parameterName);
    }

    private static EbinFieldReadView GetOptionalField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        ulong? expectedCount,
        string parameterName)
    {
        if (!record.TryGetField(tag, out var field))
        {
            throw new ArgumentException("A required Scene control field is absent.", parameterName);
        }

        ValidateOptionalField(field, tag, kind, expectedCount, parameterName);
        return field;
    }

    private static EbinFieldReadView GetRequiredField(
        EbinRecordReadView record,
        uint tag,
        string parameterName)
    {
        if (!record.TryGetField(tag, out var field) || !field.IsRequired)
        {
            throw new ArgumentException("A required Scene control field is absent.", parameterName);
        }

        return field;
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
            throw new ArgumentException("A Scene control field has an invalid required wire shape.", parameterName);
        }

        return field;
    }

    private static int GetWorldRecordOffset(
        scoped ReadOnlySpan<byte> worldBytes,
        scoped ReadOnlySpan<byte> childRecord,
        string parameterName)
    {
        if (!worldBytes.Overlaps(childRecord, out var offset)
            || (offset < 0)
            || (offset > worldBytes.Length)
            || (childRecord.Length > (worldBytes.Length - offset)))
        {
            throw new ArgumentException("A Scene activation source is not backed by its enclosing state/world record.", parameterName);
        }

        return offset;
    }

    private static bool HasHistoricalIdentity(
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> identityScratch,
        Guid identity,
        byte kind)
    {
        var index = WorldPersistenceIdentityScratchEntry.FindIndex(identityScratch, identity);
        return (index >= 0) && (identityScratch[index].Kind == kind);
    }

    private static bool IsReservedUuidV8(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        _ = value.TryWriteBytes(bytes, bigEndian: true, out _);
        return ((bytes[6] & 0xF0) == 0x80) && ((bytes[8] & 0xC0) == 0x80);
    }

    private static byte ReadByte(EbinFieldReadView field, string parameterName)
    {
        if ((field.Kind != UInt8Kind)
            || (field.Count != 1)
            || (field.Payload.Length != sizeof(byte)))
        {
            throw new ArgumentException("A Scene control byte has an invalid wire shape.", parameterName);
        }

        return field.Payload[0];
    }

    private static byte ReadByte(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt8Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(byte))
        {
            throw new ArgumentException("A Scene control byte has an invalid wire shape.", parameterName);
        }

        return field.Payload[0];
    }

    private static Guid ReadGuid(EbinFieldReadView field, string parameterName)
    {
        if ((field.Kind != GuidKind) || (field.Count != 1) || (field.Payload.Length != 16))
        {
            throw new ArgumentException("A Scene control GUID has an invalid wire shape.", parameterName);
        }

        return new Guid(field.Payload, bigEndian: true);
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag, string parameterName)
        => ReadGuid(GetRequiredField(record, tag, GuidKind, expectedCount: 1, parameterName), parameterName);

    private static ReadOnlySpan<byte> ReadRecordArrayElement(
        EbinFieldReadView field,
        int index,
        string parameterName)
    {
        if ((field.Kind != ArrayKind)
            || (field.Count > 65_536)
            || (checked((ulong)index) >= field.Count))
        {
            throw new ArgumentException("A Scene control record array has an invalid element index.", parameterName);
        }

        var offsetsBytes = checked(8UL + ((field.Count + 1UL) * 8UL));
        var firstChildOffset = checked((int)((offsetsBytes + 15UL) & ~15UL));
        var startOffsetValue = BinaryPrimitives.ReadUInt64LittleEndian(
            field.Payload[checked(8 + (index * 8))..]);
        var endOffsetValue = BinaryPrimitives.ReadUInt64LittleEndian(
            field.Payload[checked(8 + ((index + 1) * 8))..]);

        if ((startOffsetValue > int.MaxValue)
            || (endOffsetValue > int.MaxValue))
        {
            throw new ArgumentException("A Scene control record array contains an out-of-range offset.", parameterName);
        }

        var startOffset = checked((int)startOffsetValue);
        var endOffset = checked((int)endOffsetValue);

        if ((startOffset < firstChildOffset)
            || (endOffset < startOffset)
            || (endOffset > field.Payload.Length))
        {
            throw new ArgumentException("A Scene control record array contains an invalid offset.", parameterName);
        }

        return field.Payload.Slice(startOffset, endOffset - startOffset);
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag, string parameterName)
    {
        var field = GetRequiredField(record, tag, UInt32Kind, expectedCount: 1, parameterName);

        if (field.Payload.Length != sizeof(uint))
        {
            throw new ArgumentException("A Scene control UInt32 has an invalid wire shape.", parameterName);
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static void ValidateActivation(
        EbinRecordReadView payload,
        Guid sceneInstanceId,
        WorldPersistenceIdentityScratchEntry identity,
        EbinRecordReadView identityHistory,
        Guid worldGuid,
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> identityScratch,
        string parameterName)
    {
        var requestedFieldPresent = payload.TryGetField(7, out var requestedField);
        var expectedFieldCount = requestedFieldPresent ? 6 : 5;

        if (payload.FieldCount != expectedFieldCount
            || payload.TryGetField(4, out _)
            || payload.TryGetField(5, out _))
        {
            throw new ArgumentException("A state/control-scene Activate has an invalid field set.", parameterName);
        }

        var sourceField = GetOptionalField(payload, 3, RecordKind, expectedCount: 1, parameterName);
        var source = new EbinRecordReadView(sourceField.Payload);
        ValidateSource(source, parameterName);
        var externalBindingsField = GetOptionalField(payload, 6, ArrayKind, expectedCount: null, parameterName);
        ValidateExternalBindings(externalBindingsField, identityScratch, parameterName);
        var originalSceneInstanceId = ReadGuid(
            GetOptionalField(payload, 8, GuidKind, expectedCount: 1, parameterName),
            parameterName);
        Guid? requestedSceneInstanceId = null;

        if (requestedFieldPresent)
        {
            ValidateOptionalField(requestedField, 7, GuidKind, expectedCount: 1, parameterName);
            requestedSceneInstanceId = ReadGuid(requestedField, parameterName);
        }

        ValidateActivationIdentity(
            sceneInstanceId,
            originalSceneInstanceId,
            requestedSceneInstanceId,
            worldGuid,
            identity,
            parameterName);

        var branchField = GetRequiredField(identityHistory, 6, ArrayKind, expectedCount: null, parameterName);

        if (identity.BranchIndex >= branchField.Count)
        {
            throw new ArgumentException("A SceneInstance assignment references an absent creation branch.", parameterName);
        }
    }

    private static void ValidateEdit(
        EbinRecordReadView payload,
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> identityScratch,
        string parameterName)
    {
        if ((payload.FieldCount != 3)
            || payload.TryGetField(3, out _)
            || payload.TryGetField(5, out _)
            || payload.TryGetField(6, out _)
            || payload.TryGetField(7, out _)
            || payload.TryGetField(8, out _))
        {
            throw new ArgumentException("A state/control-scene Edit has an invalid field set.", parameterName);
        }

        var intents = GetOptionalField(payload, 4, ArrayKind, expectedCount: null, parameterName);

        var intentCount = checked((int)intents.Count);

        if (intentCount is < 1 or > 65_536)
        {
            throw new ArgumentException("A state/control-scene Edit has an invalid intent count.", parameterName);
        }

        for (var index = 0; index < intentCount; index++)
        {
            var intent = payload.GetRecordArrayElement(4, index);
            ValidateIntent(intent, identityScratch, parameterName);
        }
    }

    private static void ValidateExternalBindings(
        EbinFieldReadView externalBindings,
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> identityScratch,
        string parameterName)
    {
        if (externalBindings.Count > 65_536)
        {
            throw new ArgumentException("A state/control-scene external-binding count exceeds its bound.", parameterName);
        }

        var bindingCount = checked((int)externalBindings.Count);
        var previousBindingId = Guid.Empty;

        for (var index = 0; index < bindingCount; index++)
        {
            var binding = new EbinRecordReadView(
                ReadRecordArrayElement(externalBindings, index, parameterName));
            ValidateRecordHeader(binding, s_externalBindingSchemaId, minimumFieldCount: 1, maximumFieldCount: 2, parameterName);
            var bindingId = ReadGuid(binding, 1, parameterName);

            if ((bindingId == Guid.Empty)
                || ((index > 0) && (FoundationCatalogue.CompareRfc(previousBindingId, bindingId) >= 0)))
            {
                throw new ArgumentException("Scene external-binding controls are not uniquely ordered by BindingId.", parameterName);
            }

            previousBindingId = bindingId;

            if (binding.TryGetField(2, out var targetField))
            {
                ValidateOptionalField(targetField, 2, GuidKind, expectedCount: 1, parameterName);
                var targetEntityId = ReadGuid(targetField, parameterName);

                if (!HasHistoricalIdentity(identityScratch, targetEntityId, EntityIdentityKind))
                {
                    throw new ArgumentException("A Scene external-binding control target is absent from Entity identity history.", parameterName);
                }
            }
        }
    }

    private static void ValidateFinalScenes(
        scoped ReadOnlySpan<byte> worldBytes,
        ReadOnlyMemory<byte> scenesRecord,
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> identityScratch,
        int activeIdentityCount,
        string parameterName)
    {
        var scenes = new EbinRecordReadView(scenesRecord.Span);

        if ((scenes.Header.SchemaId != s_scenesSchemaId)
            || (scenes.Header.Major != 1)
            || (scenes.Header.Minor != 0)
            || (scenes.FieldCount != 2))
        {
            throw new ArgumentException("The state/scenes record has the wrong schema identity, version, or field count.", parameterName);
        }

        var instances = GetRequiredField(scenes, 1, ArrayKind, expectedCount: null, parameterName);

        if (instances.Count != (ulong)activeIdentityCount)
        {
            throw new ArgumentException("The final state/scenes set does not match the Scene control scan.", parameterName);
        }

        var instanceCount = checked((int)instances.Count);

        for (var index = 0; index < instanceCount; index++)
        {
            var instance = new EbinRecordReadView(scenes.GetRecordArrayElement(1, index).EncodedBytes);
            ValidateRecordHeader(instance, s_sceneInstanceSchemaId, minimumFieldCount: 7, maximumFieldCount: 7, parameterName);
            var sceneInstanceId = ReadGuid(instance, 1, parameterName);
            var identityIndex = WorldPersistenceIdentityScratchEntry.FindIndex(identityScratch, sceneInstanceId);

            if ((identityIndex < 0)
                || !identityScratch[identityIndex].IsSceneControlActive
                || (identityScratch[identityIndex].SceneControlRecordOffset < 0)
                || (identityScratch[identityIndex].SceneControlRecordLength <= 0))
            {
                throw new ArgumentException("A final SceneInstance is absent from the active Scene control scan.", parameterName);
            }

            var controlOffset = identityScratch[identityIndex].SceneControlRecordOffset;
            var controlLength = identityScratch[identityIndex].SceneControlRecordLength;

            if ((controlOffset > worldBytes.Length)
                || (controlLength > (worldBytes.Length - controlOffset)))
            {
                throw new ArgumentException("An active Scene control record exceeds the World record.", parameterName);
            }

            var activation = new EbinRecordReadView(worldBytes.Slice(controlOffset, controlLength));
            var activationSource = activation.GetRecord(3).EncodedBytes;
            var sceneSource = instance.GetRecord(2).EncodedBytes;

            if (!activationSource.SequenceEqual(sceneSource))
            {
                throw new ArgumentException("A final SceneInstance source does not match its active Scene activation control.", parameterName);
            }

            var activationBindingCount = activation.TryGetField(6, out var activationBindings)
                ? checked((int)activationBindings.Count)
                : 0;
            var sceneBindings = GetRequiredField(instance, 5, ArrayKind, expectedCount: null, parameterName);

            if ((ulong)activationBindingCount != sceneBindings.Count)
            {
                throw new ArgumentException("A final SceneInstance external-binding set does not match its activation control.", parameterName);
            }

            for (var bindingIndex = 0; bindingIndex < activationBindingCount; bindingIndex++)
            {
                var activationBinding = ReadRecordArrayElement(activationBindings, bindingIndex, parameterName);
                var sceneBinding = instance.GetRecordArrayElement(5, bindingIndex).EncodedBytes;

                if (!activationBinding.SequenceEqual(sceneBinding))
                {
                    throw new ArgumentException("A final SceneInstance external-binding set does not match its activation control.", parameterName);
                }
            }
        }
    }

    private static void ValidateHistoricalEntity(
        Guid entityId,
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> identityScratch,
        string parameterName)
    {
        if (!HasHistoricalIdentity(identityScratch, entityId, EntityIdentityKind))
        {
            throw new ArgumentException("A Scene control intent names no historical Entity assignment.", parameterName);
        }
    }

    private static void ValidateIntent(
        EbinRecordReadView intent,
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> identityScratch,
        string parameterName)
    {
        if (intent.Header.SchemaId == s_reparentIntentSchemaId)
        {
            ValidateRecordHeader(intent, s_reparentIntentSchemaId, minimumFieldCount: 2, maximumFieldCount: 3, parameterName);
            ValidateHistoricalEntity(ReadGuid(intent, 1, parameterName), identityScratch, parameterName);
            var hasParent = intent.TryGetField(2, out var parentField);

            if (hasParent)
            {
                ValidateOptionalField(parentField, 2, GuidKind, expectedCount: 1, parameterName);
                ValidateHistoricalEntity(ReadGuid(parentField, parameterName), identityScratch, parameterName);
            }

            var mode = ReadByte(intent, 3, parameterName);

            if ((mode > 1) || (intent.FieldCount != (hasParent ? 3 : 2)))
            {
                throw new ArgumentException("A Scene reparent intent has an invalid mode or field set.", parameterName);
            }

            return;
        }

        if (intent.Header.SchemaId == s_rootPositionIntentSchemaId)
        {
            ValidateRecordHeader(intent, s_rootPositionIntentSchemaId, fieldCount: 2, parameterName);
            ValidateHistoricalEntity(ReadGuid(intent, 1, parameterName), identityScratch, parameterName);
            var position = GetRequiredField(intent, 2, 21, expectedCount: 1, parameterName);

            if ((position.Kind != 21) || (position.Payload.Length != 24))
            {
                throw new ArgumentException("A Scene root-position intent has an invalid spatial position shape.", parameterName);
            }

            for (var offset = 12; offset < 24; offset += sizeof(float))
            {
                var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(position.Payload[offset..]));

                if (!float.IsFinite(value))
                {
                    throw new ArgumentException("A Scene root-position intent contains a nonfinite offset.", parameterName);
                }
            }

            return;
        }

        throw new ArgumentException("A Scene control intent has an unsupported schema identity.", parameterName);
    }

    private static void ValidateOptionalField(
        EbinFieldReadView field,
        uint tag,
        ushort kind,
        ulong? expectedCount,
        string parameterName)
    {
        if ((field.Tag != tag)
            || (field.Kind != kind)
            || field.IsRequired
            || (expectedCount.HasValue && (field.Count != expectedCount.Value)))
        {
            throw new ArgumentException("An optional Scene control field has an invalid wire shape.", parameterName);
        }
    }

    private static void ValidateRecordHeader(
        EbinRecordReadView record,
        Guid schemaId,
        int fieldCount,
        string parameterName)
        => ValidateRecordHeader(record, schemaId, fieldCount, fieldCount, parameterName);

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
            throw new ArgumentException("A Scene control child record has the wrong schema identity, version, or field count.", parameterName);
        }
    }

    private static void ValidateSource(EbinRecordReadView source, string parameterName)
    {
        ValidateRecordHeader(source, s_nativeAssetReferenceSchemaId, fieldCount: 3, parameterName);
        var assetId = ReadUInt32(source, 1, parameterName);
        var schemaId = ReadGuid(source, 2, parameterName);
        var schemaMajor = ReadUInt32(source, 3, parameterName);

        if ((assetId == 0) || (schemaId != s_sceneSchemaId) || (schemaMajor != 2))
        {
            throw new ArgumentException("A Scene activation control has an unsupported Scene source reference.", parameterName);
        }
    }

    private static void ValidateUnload(EbinRecordReadView payload, string parameterName)
    {
        if ((payload.FieldCount != 3)
            || payload.TryGetField(3, out _)
            || payload.TryGetField(4, out _)
            || payload.TryGetField(6, out _)
            || payload.TryGetField(7, out _)
            || payload.TryGetField(8, out _))
        {
            throw new ArgumentException("A state/control-scene Unload has an invalid field set.", parameterName);
        }

        var destroyMode = GetOptionalField(payload, 5, UInt8Kind, expectedCount: 1, parameterName);
        var mode = ReadByte(destroyMode, parameterName);

        if (mode > 1)
        {
            throw new ArgumentException("A state/control-scene Unload has an unsupported destroy mode.", parameterName);
        }
    }
}
