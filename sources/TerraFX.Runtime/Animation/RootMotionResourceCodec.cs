// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Animation;

internal static class RootMotionResourceCodec
{
    internal const int MaximumRecordBytes = 11_894_896;
    internal const int MaximumSlotBytes = 2896;
    internal const int MinimumRecordBytes = 491_632;
    private const int ArrayTableBytes = 32_784;
    private const int AssetBytes = 208;
    private const int SourceBytes = 496;
    private static readonly ContentSchemaReference s_assetSchema = RootMotionResourceSchemas.Reference("native/asset-reference");
    private static readonly Guid s_clipSchemaId = RootMotionResourceSchemas.Reference("native/clip").SchemaId;
    private static readonly Guid s_controllerSchemaId = RootMotionResourceSchemas.Reference("native/animation-controller").SchemaId;
    private static readonly ContentSchemaReference s_schema = RootMotionResourceSchemas.Reference("animation/root-motion-resource");
    private static readonly ContentSchemaReference s_slotSchema = RootMotionResourceSchemas.Reference("animation/root-motion-slot");
    private static readonly ContentSchemaReference s_sourceSchema = RootMotionResourceSchemas.Reference("animation/root-motion-source");

    internal static ContentSchemaReference Schema => s_schema;

    internal static (int RecordBytes, int SlotBytes) Measure(
        WorldResourceCaptureBinding binding,
        WorldResourceFieldDescriptor valuesField,
        CancellationToken cancellationToken)
    {
        if (binding.GetCount(valuesField) != 4096)
        {
            throw new InvalidOperationException("The root-motion capture does not expose its complete partition domain.");
        }

        var slots = 0;

        for (var index = 0; index < 4096; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = binding.GetElement<RootMotionRequest>(valuesField, index);
            slots = checked(slots + GetSlotBytes(in value));
        }

        var bytes = checked(96 + ArrayTableBytes + slots);

        if (bytes > binding.PersistenceLimits.MaximumRecordBytes)
        {
            throw new InvalidOperationException("The root-motion ResourceState exceeds its issued record-byte-budget.");
        }

        return (bytes, slots);
    }

    internal static RootMotionResourceAuthor ReadAuthors<TReferences>(
        scoped ReadOnlySpan<byte> source,
        scoped in TReferences references,
        CancellationToken cancellationToken)
        where TReferences : IWorldResourceReferences, allows ref struct
    {
        var root = new EbinRecordReadView(source);
        var slots = ImmutableArray.CreateBuilder<RootMotionSlotAuthor>(4096);
        Span<RootMotionSourceAuthor> sourceAuthors = stackalloc RootMotionSourceAuthor[4];
        sourceAuthors.Clear();

        for (var index = 0; index < 4096; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = ReadSlot(root.GetRecordArrayElement(1, index), in references);

            if (!request.IsValid)
            {
                slots.Add(new RootMotionSlotAuthor(
                    false, null, null, null, null, null, null, null, null, null, null, null, null));
                continue;
            }

            sourceAuthors.Clear();

            for (var sourceIndex = 0; sourceIndex < request.SourceCount; sourceIndex++)
            {
                var contribution = request.GetSource(sourceIndex);
                sourceAuthors[sourceIndex] = new RootMotionSourceAuthor(
                    contribution.ClipSlotId,
                    new WorldAssetReference(contribution.Clip.Value, 1),
                    contribution.PlaySequence, contribution.LoopOrdinal, contribution.Weight);
            }

            var player = request.Player;
            var actor = request.Actor;
            slots.Add(new RootMotionSlotAuthor(
                true,
                player.EntityId, player.CreationLineageId, player.CreationBranchId,
                actor.EntityId, actor.CreationLineageId, actor.CreationBranchId,
                request.TickIndex, request.Translation, request.Rotation,
                ImmutableArray.Create(sourceAuthors[..request.SourceCount]),
                (byte)request.ExtractionPolicy,
                new WorldAssetReference(request.ControllerAssetId, 1)));
        }

        return new RootMotionResourceAuthor(slots.MoveToImmutable());
    }

    internal static RootMotionRequest RestoreSlot(RootMotionSlotAuthor slot, WorldResourceReferenceBinding references)
    {
        if (!slot.Present)
        {
            return default;
        }

        if (slot.PlayerId is not { } playerId || slot.PlayerCreationLineageId is not { } playerLineage
            || slot.PlayerCreationBranchId is not { } playerBranch || slot.ActorId is not { } actorId
            || slot.ActorCreationLineageId is not { } actorLineage || slot.ActorCreationBranchId is not { } actorBranch
            || slot.TickIndex is not { } tick || slot.Translation is not { } translation
            || slot.Rotation is not { } rotation || slot.Sources is not { } sources
            || slot.ExtractionPolicy is not { } policy || slot.Controller is not { } controller
            || !references.TryResolveHistoricalEntity(playerId, playerLineage, playerBranch, out var player)
            || !references.TryResolveHistoricalEntity(actorId, actorLineage, actorBranch, out var actor))
        {
            throw new InvalidDataException("A restored root-motion slot has incomplete historical creation facts.");
        }

        Span<RootMotionSource> values = stackalloc RootMotionSource[4];
        values.Clear();

        for (var index = 0; index < sources.Length; index++)
        {
            var source = sources[index];
            values[index] = new RootMotionSource(
                source.ClipSlotId, source.Clip.AssetId, source.PlaySequence, source.LoopOrdinal, source.Weight);
        }

        return new RootMotionRequest(
            player, actor, tick, translation, rotation, (RootMotionExtractionPolicy)policy,
            values[..sources.Length], controller.AssetId);
    }

    internal static EbinReadStatus Validate<TReferences>(
        scoped ReadOnlySpan<byte> source,
        scoped in TReferences references,
        CancellationToken cancellationToken,
        out bool valuesValid,
        out int consumed,
        out EbinReadDiagnostic? diagnostic)
        where TReferences : IWorldResourceReferences, allows ref struct
    {
        var limits = references.PersistenceLimits;
        _ = references.WorldGuid;

        if (!references.RequiredRequestTick.HasValue)
        {
            throw new InvalidOperationException("A root-motion source Read requires its issued completed tick.");
        }

        valuesValid = false;
        var status = EbinStructuralReader.Validate(source, limits, EbinReadPolicy.Canonical, out consumed, out diagnostic);

        if ((status != EbinReadStatus.Complete) || (consumed != source.Length)
            || (source.Length < MinimumRecordBytes) || (source.Length > MaximumRecordBytes))
        {
            return status;
        }

        try
        {
            var root = new EbinRecordReadView(source);
            ValidateHeader(root, s_schema, 1);
            _ = ReadField(root, 1, 24, required: true, count: 4096);
            var absentSeen = false;
            var previousPlayer = Guid.Empty;
            Span<Guid> actors = stackalloc Guid[4096];
            actors.Clear();
            var actorCount = 0;

            for (var index = 0; index < 4096; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var slot = ReadSlot(root.GetRecordArrayElement(1, index), in references);

                if (!slot.IsValid)
                {
                    absentSeen = true;
                    continue;
                }

                var player = slot.Player.EntityId;

                if (absentSeen || ((previousPlayer != Guid.Empty) && (FoundationCatalogue.CompareRfc(previousPlayer, player) >= 0)))
                {
                    return status;
                }

                previousPlayer = player;
                actors[actorCount++] = slot.Actor.EntityId;
            }

            var activeActors = actors[..actorCount];
            activeActors.Sort();

            for (var index = 1; index < activeActors.Length; index++)
            {
                if (activeActors[index - 1] == activeActors[index])
                {
                    return status;
                }
            }

            valuesValid = true;
        }
        catch (InvalidDataException)
        {
            valuesValid = false;
        }

        return status;
    }

    internal static bool ValidateRuntimeContext(
        scoped ReadOnlySpan<RootMotionRequest> values,
        WorldResourceReferenceBinding references)
    {
        var worldGuid = references.WorldGuid;
        var tick = references.RequiredRequestTick;

        foreach (ref readonly var value in values)
        {
            if (!value.IsValid)
            {
                continue;
            }

            var player = value.Player;
            var actor = value.Actor;

            if (!tick.HasValue || (value.TickIndex != tick) || (player.WorldGuid != worldGuid)
                || !references.TryResolveHistoricalEntity(player.EntityId, player.CreationLineageId, player.CreationBranchId, out _)
                || !references.TryResolveHistoricalEntity(actor.EntityId, actor.CreationLineageId, actor.CreationBranchId, out _))
            {
                return false;
            }
        }

        return true;
    }

    internal static int Write(
        WorldResourceCaptureBinding binding,
        WorldResourceFieldDescriptor valuesField,
        scoped Span<byte> destination,
        CancellationToken cancellationToken)
    {
        var (recordBytes, slotBytes) = Measure(binding, valuesField, cancellationToken);

        if (destination.Length < recordBytes)
        {
            throw new ArgumentException("The root-motion destination is smaller than its complete canonical record.", nameof(destination));
        }

        var limits = binding.PersistenceLimits;
        var slices = new EbinPayloadSlice[4096];
        var slotStorage = new byte[slotBytes];
        var offset = 0;

        for (var index = 0; index < 4096; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = binding.GetElement<RootMotionRequest>(valuesField, index);
            var bytes = GetSlotBytes(in request);
            WriteSlot(in request, limits, slotStorage.AsSpan(offset, bytes));
            slices[index] = new EbinPayloadSlice(offset, bytes);
            offset = checked(offset + bytes);
        }

        var payload = new byte[checked(ArrayTableBytes + slotBytes)];
        RequireComplete(EbinPayloadWriter.WriteRecordArray(slices, slotStorage, limits, payload, out var payloadBytes, out var diagnostic), diagnostic);

        if ((payloadBytes != payload.Length) || (offset != slotBytes))
        {
            throw new InvalidOperationException("The root-motion array changed its measured canonical extent.");
        }

        Span<EbinFieldSource> fields = [new(1, 24, 1, 4096, new EbinPayloadSlice(0, payload.Length))];
        var candidate = new byte[recordBytes];
        WriteRecord(s_schema, fields, payload, limits, candidate, destination[..recordBytes]);
        cancellationToken.ThrowIfCancellationRequested();
        return recordBytes;
    }

    private static int Align16(int value) => checked((value + 15) & ~15);

    private static int GetSlotBytes(in RootMotionRequest value)
    {
        if (!RootMotionResourceValidation.IsValid(in value))
        {
            throw new InvalidDataException("The root-motion slot violates its captured value contract.");
        }

        return value.IsValid ? checked(864 + GetSourcesBytes(value.SourceCount)) : 112;
    }

    private static int GetSourcesBytes(int count)
        => checked(Align16(8 + (8 * (count + 1))) + (SourceBytes * count));

    private static int ReadAsset(EbinRecordReadView record, Guid target)
    {
        ValidateHeader(record, s_assetSchema, 3);
        var id = BinaryPrimitives.ReadUInt32LittleEndian(ReadField(record, 1, 3, required: true).Payload);
        var schema = ReadGuid(record, 2, required: true);
        var major = BinaryPrimitives.ReadUInt32LittleEndian(ReadField(record, 3, 3, required: true).Payload);

        if ((id == 0) || (id > int.MaxValue) || (schema != target) || (major != 1))
        {
            throw new InvalidDataException("A root-motion AssetReference violates its fixed target/version policy.");
        }

        return checked((int)id);
    }

    private static EbinFieldReadView ReadField(
        EbinRecordReadView record, uint tag, ushort kind, bool required, ulong count = 1)
    {
        if (!record.TryGetField(tag, out var field)
            || (field.Kind != kind) || (field.IsRequired != required) || (field.Count != count))
        {
            throw new InvalidDataException("A root-motion field does not match its exact tag/kind/presence/count.");
        }

        return field;
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag, bool required)
    {
        var value = new Guid(ReadField(record, tag, 11, required).Payload, bigEndian: true);

        if (value == Guid.Empty)
        {
            throw new InvalidDataException("A root-motion identity cannot be empty.");
        }

        return value;
    }

    private static float ReadSingle(ReadOnlySpan<byte> payload, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]));

    private static RootMotionRequest ReadSlot<TReferences>(EbinRecordReadView record, scoped in TReferences references)
        where TReferences : IWorldResourceReferences, allows ref struct
    {
        var present = ReadField(record, 1, 9, required: true).Payload[0] != 0;
        ValidateHeader(record, s_slotSchema, present ? 13 : 1);

        if (!present)
        {
            return default;
        }

        var playerId = ReadGuid(record, 2, required: false);
        var playerLineage = ReadGuid(record, 3, required: false);
        var playerBranch = ReadGuid(record, 4, required: false);
        var actorId = ReadGuid(record, 5, required: false);
        var actorLineage = ReadGuid(record, 6, required: false);
        var actorBranch = ReadGuid(record, 7, required: false);
        var tick = BinaryPrimitives.ReadInt64LittleEndian(ReadField(record, 8, 8, required: false).Payload);
        var translationField = ReadField(record, 9, 15, required: false).Payload;
        var rotationField = ReadField(record, 10, 17, required: false).Payload;
        var translation = new Vector3(ReadSingle(translationField, 0), ReadSingle(translationField, 4), ReadSingle(translationField, 8));
        var rotation = new Quaternion(ReadSingle(rotationField, 0), ReadSingle(rotationField, 4), ReadSingle(rotationField, 8), ReadSingle(rotationField, 12));
        var lengthSquared = Quaternion.Dot(rotation, rotation);
        var policy = ReadField(record, 12, 1, required: false).Payload[0];
        _ = ReadField(record, 13, 22, required: false);
        var controller = ReadAsset(record.GetRecord(13), s_controllerSchemaId);

        if (tick < 0 || (tick != references.RequiredRequestTick) || (policy != (byte)RootMotionExtractionPolicy.Extract)
            || !translation.IsFinite().All() || !rotation.IsFinite().All()
            || !float.IsFinite(lengthSquared) || (MathF.Abs(lengthSquared - 1.0f) > 2.0e-6f)
            || !record.TryGetField(11, out var sourceArray) || (sourceArray.Kind != 24)
            || sourceArray.IsRequired || (sourceArray.Count > 4)
            || !references.TryResolveHistoricalEntity(playerId, playerLineage, playerBranch, out var player)
            || !references.TryResolveHistoricalEntity(actorId, actorLineage, actorBranch, out var actor))
        {
            throw new InvalidDataException("A root-motion slot violates its motion, tick, or historical-creation contract.");
        }

        var count = checked((int)sourceArray.Count);
        Span<RootMotionSource> sources = stackalloc RootMotionSource[4];
        sources.Clear();

        for (var index = 0; index < count; index++)
        {
            sources[index] = ReadSource(record.GetRecordArrayElement(11, index));
        }

        if ((count == 0)
            && ((BitConverter.SingleToInt32Bits(translation.X) != 0)
                || (BitConverter.SingleToInt32Bits(translation.Y) != 0)
                || (BitConverter.SingleToInt32Bits(translation.Z) != 0)
                || (BitConverter.SingleToInt32Bits(rotation.X) != 0)
                || (BitConverter.SingleToInt32Bits(rotation.Y) != 0)
                || (BitConverter.SingleToInt32Bits(rotation.Z) != 0)
                || (BitConverter.SingleToInt32Bits(rotation.W) != BitConverter.SingleToInt32Bits(1.0f))))
        {
            throw new InvalidDataException("A source-free root-motion slot requires explicit positive-zero identity motion.");
        }

        var request = new RootMotionRequest(
            player, actor, tick, translation, rotation, (RootMotionExtractionPolicy)policy, sources[..count], controller);

        if (!RootMotionResourceValidation.IsValid(in request))
        {
            throw new InvalidDataException("The root-motion sources violate their complete ordered value contract.");
        }

        return request;
    }

    private static RootMotionSource ReadSource(EbinRecordReadView record)
    {
        ValidateHeader(record, s_sourceSchema, 5);
        var id = ReadGuid(record, 1, required: true);
        _ = ReadField(record, 2, 22, required: true);
        var clip = ReadAsset(record.GetRecord(2), s_clipSchemaId);
        var sequence = BinaryPrimitives.ReadInt64LittleEndian(ReadField(record, 3, 8, required: true).Payload);
        var loop = BinaryPrimitives.ReadInt64LittleEndian(ReadField(record, 4, 8, required: true).Payload);
        var weight = ReadSingle(ReadField(record, 5, 10, required: true).Payload, 0);

        if ((sequence <= 0) || !float.IsFinite(weight) || (weight <= 0) || (weight > 1))
        {
            throw new InvalidDataException("A root-motion source has an invalid sequence or positive contribution weight.");
        }

        return new RootMotionSource(id, clip, sequence, loop, weight);
    }

    private static void RequireComplete(EbinWriteStatus status, EbinStructuralDiagnostic? diagnostic)
    {
        if (status != EbinWriteStatus.Complete)
        {
            throw new InvalidOperationException(diagnostic?.Category ?? "The root-motion canonical writer failed.");
        }
    }

    private static void ValidateHeader(EbinRecordReadView record, ContentSchemaReference schema, int fieldCount)
    {
        var header = record.Header;

        if ((header.SchemaId != schema.SchemaId) || (header.Major != schema.Major) || (header.Minor != schema.Minor)
            || (record.FieldCount != fieldCount))
        {
            throw new InvalidDataException("A root-motion record has an unexpected schema tuple or field membership.");
        }
    }

    private static void WriteAsset(int id, Guid target, EbinReadLimits limits, Span<byte> destination)
    {
        Span<byte> payload = stackalloc byte[24];
        payload.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(payload, checked((uint)id));
        _ = target.TryWriteBytes(payload.Slice(4, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt32LittleEndian(payload[20..], 1);
        Span<EbinFieldSource> fields = [
            new(1, 3, 1, 1, new EbinPayloadSlice(0, 4)),
            new(2, 11, 1, 1, new EbinPayloadSlice(4, 16)),
            new(3, 3, 1, 1, new EbinPayloadSlice(20, 4)),
        ];
        Span<byte> candidate = stackalloc byte[AssetBytes];
        candidate.Clear();
        WriteRecord(s_assetSchema, fields, payload, limits, candidate, destination);
    }

    private static void WriteRecord(
        ContentSchemaReference schema,
        ReadOnlySpan<EbinFieldSource> fields,
        ReadOnlySpan<byte> payload,
        EbinReadLimits limits,
        Span<byte> candidate,
        Span<byte> destination)
    {
        var source = new EbinRecordWriteView(schema.SchemaId, schema.Major, schema.Minor, fields, payload);
        RequireComplete(EbinStructuralWriter.Write(in source, limits, candidate, destination, out var written, out var diagnostic), diagnostic);

        if (written != destination.Length)
        {
            throw new InvalidOperationException("A root-motion record changed its measured canonical extent.");
        }
    }

    private static void WriteSingle(Span<byte> payload, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(payload[offset..], BitConverter.SingleToInt32Bits(value));

    private static void WriteSlot(in RootMotionRequest request, EbinReadLimits limits, Span<byte> destination)
    {
        Span<byte> candidate = stackalloc byte[MaximumSlotBytes];
        candidate.Clear();

        if (!request.IsValid)
        {
            Span<byte> absent = [0];
            Span<EbinFieldSource> absentFields = [new(1, 9, 1, 1, new EbinPayloadSlice(0, 1))];
            WriteRecord(s_slotSchema, absentFields, absent, limits, candidate[..112], destination);
            return;
        }

        Span<byte> childStorage = stackalloc byte[4 * SourceBytes];
        childStorage.Clear();
        Span<EbinPayloadSlice> slices = stackalloc EbinPayloadSlice[4];
        slices.Clear();

        for (var index = 0; index < request.SourceCount; index++)
        {
            var source = request.GetSource(index);
            WriteSource(in source, limits, childStorage.Slice(index * SourceBytes, SourceBytes));
            slices[index] = new EbinPayloadSlice(index * SourceBytes, SourceBytes);
        }

        Span<byte> payload = stackalloc byte[2374];
        payload.Clear();
        payload[0] = 1;
        var player = request.Player;
        var actor = request.Actor;
        _ = player.EntityId.TryWriteBytes(payload.Slice(1, 16), bigEndian: true, out _);
        _ = player.CreationLineageId.TryWriteBytes(payload.Slice(17, 16), bigEndian: true, out _);
        _ = player.CreationBranchId.TryWriteBytes(payload.Slice(33, 16), bigEndian: true, out _);
        _ = actor.EntityId.TryWriteBytes(payload.Slice(49, 16), bigEndian: true, out _);
        _ = actor.CreationLineageId.TryWriteBytes(payload.Slice(65, 16), bigEndian: true, out _);
        _ = actor.CreationBranchId.TryWriteBytes(payload.Slice(81, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteInt64LittleEndian(payload[97..], request.TickIndex);
        var translation = request.Translation;
        var rotation = request.Rotation;
        WriteSingle(payload, 105, translation.X);
        WriteSingle(payload, 109, translation.Y);
        WriteSingle(payload, 113, translation.Z);
        WriteSingle(payload, 117, rotation.X);
        WriteSingle(payload, 121, rotation.Y);
        WriteSingle(payload, 125, rotation.Z);
        WriteSingle(payload, 129, rotation.W);
        var sourceBytes = GetSourcesBytes(request.SourceCount);
        RequireComplete(EbinPayloadWriter.WriteRecordArray(
            slices[..request.SourceCount], childStorage, limits,
            payload.Slice(133, sourceBytes), out var arrayBytes, out var diagnostic), diagnostic);

        if (arrayBytes != sourceBytes)
        {
            throw new InvalidOperationException("The root-motion source array changed its measured extent.");
        }

        var policyOffset = checked(133 + sourceBytes);
        payload[policyOffset] = (byte)request.ExtractionPolicy;
        WriteAsset(request.ControllerAssetId, s_controllerSchemaId, limits, payload.Slice(policyOffset + 1, AssetBytes));
        Span<EbinFieldSource> fields = [
            new(1, 9, 1, 1, new EbinPayloadSlice(0, 1)),
            new(2, 11, 0, 1, new EbinPayloadSlice(1, 16)),
            new(3, 11, 0, 1, new EbinPayloadSlice(17, 16)),
            new(4, 11, 0, 1, new EbinPayloadSlice(33, 16)),
            new(5, 11, 0, 1, new EbinPayloadSlice(49, 16)),
            new(6, 11, 0, 1, new EbinPayloadSlice(65, 16)),
            new(7, 11, 0, 1, new EbinPayloadSlice(81, 16)),
            new(8, 8, 0, 1, new EbinPayloadSlice(97, 8)),
            new(9, 15, 0, 1, new EbinPayloadSlice(105, 12)),
            new(10, 17, 0, 1, new EbinPayloadSlice(117, 16)),
            new(11, 24, 0, checked((ulong)request.SourceCount), new EbinPayloadSlice(133, sourceBytes)),
            new(12, 1, 0, 1, new EbinPayloadSlice(policyOffset, 1)),
            new(13, 22, 0, 1, new EbinPayloadSlice(policyOffset + 1, AssetBytes)),
        ];
        WriteRecord(s_slotSchema, fields, payload[..checked(policyOffset + 1 + AssetBytes)], limits, candidate[..destination.Length], destination);
    }

    private static void WriteSource(in RootMotionSource source, EbinReadLimits limits, Span<byte> destination)
    {
        Span<byte> payload = stackalloc byte[244];
        payload.Clear();
        _ = source.ClipSlotId.TryWriteBytes(payload[..16], bigEndian: true, out _);
        WriteAsset(source.Clip.Value, s_clipSchemaId, limits, payload.Slice(16, AssetBytes));
        BinaryPrimitives.WriteInt64LittleEndian(payload[224..], source.PlaySequence);
        BinaryPrimitives.WriteInt64LittleEndian(payload[232..], source.LoopOrdinal);
        WriteSingle(payload, 240, source.Weight);
        Span<EbinFieldSource> fields = [
            new(1, 11, 1, 1, new EbinPayloadSlice(0, 16)),
            new(2, 22, 1, 1, new EbinPayloadSlice(16, AssetBytes)),
            new(3, 8, 1, 1, new EbinPayloadSlice(224, 8)),
            new(4, 8, 1, 1, new EbinPayloadSlice(232, 8)),
            new(5, 10, 1, 1, new EbinPayloadSlice(240, 4)),
        ];
        Span<byte> candidate = stackalloc byte[SourceBytes];
        candidate.Clear();
        WriteRecord(s_sourceSchema, fields, payload, limits, candidate, destination);
    }
}
