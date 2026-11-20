// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Animation;

internal static class AnimationRuntimeStateCodec
{
    private static readonly Guid s_activeClipSchemaId = new ContentSchemaIdentityAssignment("animation/active-clip").SchemaId;
    private static readonly Guid s_assetReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;
    private static readonly Guid s_layerSchemaId = new ContentSchemaIdentityAssignment("animation/layer-state").SchemaId;
    private static readonly Guid s_localPoseSchemaId = new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId;
    private static readonly Guid s_playerSchemaId = new ContentSchemaIdentityAssignment("animation/player-state").SchemaId;
    private static readonly Guid s_targetBindingSchemaId = new ContentSchemaIdentityAssignment("animation/target-binding").SchemaId;
    private static readonly Guid s_transitionSchemaId = new ContentSchemaIdentityAssignment("animation/transition-state").SchemaId;
    private static readonly Guid s_worldStateSchemaId = new ContentSchemaIdentityAssignment("animation/world-state").SchemaId;

    internal static byte[] Encode(AnimationRuntimeAuthor author)
        => Encode(author, EbinReadLimits.Foundation);

    internal static byte[] Encode(AnimationRuntimeAuthor author, EbinReadLimits limits)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(limits);
        return Writer.EncodeWorldRecord(author, limits);
    }

    internal static long MeasureReadByteReservation(scoped ReadOnlySpan<byte> source)
    {
        static long ArrayBytes(ulong count, int stride)
        {
            return checked(64 + ((((long)count * stride) + 7) & ~7L));
        }

        var root = new EbinRecordReadView(source);
        var players = root.GetField(1).Count;
        // Reader-owned exact arrays are adopted by their authors, so each array has one backing.
        // The 256-byte object envelope covers fixed author fields; spatial values are explicit.
        var bytes = checked(4096 + 256 + ArrayBytes(players, IntPtr.Size));

        for (var playerIndex = 0; playerIndex < checked((int)players); playerIndex++)
        {
            var player = root.GetRecordArrayElement(2, playerIndex);
            _ = player.TryGetField(7, out var bindings);
            _ = player.TryGetField(8, out var layers);
            bytes = checked(bytes + 256
                + ArrayBytes(bindings.Count, IntPtr.Size)
                + ArrayBytes(layers.Count, IntPtr.Size)
                + ((long)bindings.Count * (512 + Unsafe.SizeOf<SpatialPosition3?>())));

            for (var layerIndex = 0; layerIndex < checked((int)layers.Count); layerIndex++)
            {
                var layer = player.GetRecordArrayElement(8, layerIndex);
                _ = layer.TryGetField(3, out var clips);
                bytes = checked(bytes + 256 + ArrayBytes(clips.Count, IntPtr.Size)
                    + ((long)clips.Count * 256));

                if (layer.TryGetField(4, out var transition))
                {
                    var transitionRecord = new EbinRecordReadView(transition.Payload);
                    _ = transitionRecord.TryGetField(4, out var weights);
                    bytes = checked(bytes + 256 + ArrayBytes(weights.Count, sizeof(float)));
                }
            }
        }

        return bytes;
    }

    internal static WorldAttachmentReadResult<AnimationRuntimeAuthor> Read(
        scoped ReadOnlySpan<byte> source,
        WorldAttachmentReadBinding<AnimationRuntimeAuthor> binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        var action = new Reader.ReadAction(binding, cancellationToken);
        var status = EbinStructuralReader.Read(
            source,
            binding.Limits,
            EbinReadPolicy.Canonical,
            ref action,
            out var bytesConsumed,
            out var diagnostic);

        if (status is not EbinReadStatus.Complete)
        {
            return binding.Reject(status, diagnostic);
        }

        if (bytesConsumed != source.Length)
        {
            return binding.Reject(
                WorldAttachmentReadFailureKind.ConsumptionMismatch,
                bytesConsumed,
                path: null,
                "The Animation playback record does not consume its complete source extent.");
        }

        return action.Result
            ?? throw new InvalidOperationException("The Animation playback reader completed without issuing a result.");
    }

    private static class Reader
    {
        internal static AnimationActiveClipAuthor ReadActiveClip(scoped EbinRecordReadView record)
        {
            ValidateHeader(record, s_activeClipSchemaId, fieldCount: 13);
            return new AnimationActiveClipAuthor(
                ReadGuid(record, tag: 1),
                ReadInt64(record, tag: 2),
                ReadInt64(record, tag: 3),
                ReadInt64(record, tag: 4),
                AnimationTickRemainder192.FromLittleEndian(ReadRequiredField(record, tag: 5, kind: 13, count: 24).Payload),
                ReadInt64(record, tag: 6),
                ReadInt64(record, tag: 7),
                ReadSingle(record, tag: 8),
                ReadUInt32(record, tag: 9),
                ReadUInt32(record, tag: 10),
                ReadBoolean(record, tag: 11),
                ReadBoolean(record, tag: 12),
                ReadInt8(record, tag: 13));
        }

        internal static AnimationLayerStateAuthor ReadLayer(scoped EbinRecordReadView record, CancellationToken cancellationToken)
        {
            var fieldCount = 3 + (record.TryGetField(4, out _) ? 1 : 0);
            ValidateHeader(record, s_layerSchemaId, fieldCount);
            var clipsField = ReadRecordArrayField(record, tag: 3, 0, 4);
            var clips = new AnimationActiveClipAuthor[checked((int)clipsField.Count)];

            for (var index = 0; index < clips.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                clips[index] = ReadActiveClip(record.GetRecordArrayElement(3, index));
            }

            AnimationTransitionStateAuthor? transition = null;

            if (record.TryGetField(4, out var transitionField))
            {
                RequireFieldShape(transitionField, tag: 4, kind: 22, isRequired: false, count: 1);
                transition = ReadTransition(new EbinRecordReadView(transitionField.Payload));
            }

            return AnimationLayerStateAuthor.CreateOwned(
                ReadUInt8(record, tag: 1),
                ReadSingle(record, tag: 2),
                clips,
                transition);
        }

        internal static AnimationLocalPoseAuthor ReadLocalPose(scoped EbinRecordReadView record)
        {
            ValidateHeader(record, s_localPoseSchemaId, fieldCount: 3);
            return new AnimationLocalPoseAuthor(
                EbinPrimitives.Vector3(ReadRequiredField(record, tag: 1, kind: 15, count: 1).Payload),
                ReadQuaternion(record, tag: 2),
                EbinPrimitives.Vector3(ReadRequiredField(record, tag: 3, kind: 15, count: 1).Payload));
        }

        internal static AnimationPlayerStateAuthor ReadPlayer(scoped EbinRecordReadView record, CancellationToken cancellationToken)
        {
            var fieldCount = 7
                + (record.TryGetField(6, out _) ? 1 : 0)
                + (record.TryGetField(9, out _) ? 1 : 0);
            ValidateHeader(record, s_playerSchemaId, fieldCount);
            var bindingsField = ReadRecordArrayField(record, tag: 7, 0, 4352);
            var bindings = new AnimationTargetBindingAuthor[checked((int)bindingsField.Count)];

            for (var index = 0; index < bindings.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bindings[index] = ReadTargetBinding(record.GetRecordArrayElement(7, index));
            }

            var layersField = ReadRecordArrayField(record, tag: 8, 1, 8);
            var layers = new AnimationLayerStateAuthor[checked((int)layersField.Count)];

            for (var index = 0; index < layers.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                layers[index] = ReadLayer(record.GetRecordArrayElement(8, index), cancellationToken);
            }

            return AnimationPlayerStateAuthor.CreateOwned(
                ReadGuid(record, tag: 1),
                ReadGuid(record, tag: 2),
                ReadAssetReference(
                    ReadRequiredRecord(record, tag: 3),
                    new ContentSchemaIdentityAssignment("native/animation-controller").SchemaId),
                ReadSingle(record, tag: 4),
                ReadInt64(record, tag: 5),
                ReadOptionalInt64(record, tag: 6),
                bindings,
                layers,
                ReadOptionalGuid(record, tag: 9));
        }

        internal static AnimationTargetBindingAuthor ReadTargetBinding(scoped EbinRecordReadView record)
        {
            var fieldCount = 3
                + (record.TryGetField(3, out _) ? 1 : 0)
                + (record.TryGetField(5, out _) ? 1 : 0);
            ValidateHeader(record, s_targetBindingSchemaId, fieldCount);
            SpatialPosition3? rootBaseline = null;

            if (record.TryGetField(5, out var rootBaselineField))
            {
                RequireFieldShape(rootBaselineField, tag: 5, kind: 21, isRequired: false, count: 1);
                rootBaseline = ReadPosition(rootBaselineField.Payload);
            }

            return new AnimationTargetBindingAuthor(
                ReadUInt8(record, tag: 1),
                ReadGuid(record, tag: 2),
                ReadOptionalGuid(record, tag: 3),
                ReadLocalPose(ReadRequiredRecord(record, tag: 4)),
                rootBaseline);
        }

        internal static AnimationTransitionStateAuthor ReadTransition(scoped EbinRecordReadView record)
        {
            ValidateHeader(record, s_transitionSchemaId, fieldCount: 5);
            return AnimationTransitionStateAuthor.CreateOwned(
                ReadGuid(record, tag: 1),
                ReadInt64(record, tag: 2),
                ReadUInt16(record, tag: 3),
                ReadFloatFixedArray(record, tag: 4),
                ReadGuid(record, tag: 5));
        }

        internal static AnimationRuntimeAuthor ReadWorldState(
            scoped EbinRecordReadView record,
            CancellationToken cancellationToken)
        {
            ValidateHeader(record, s_worldStateSchemaId, fieldCount: 2);
            var playersField = ReadRecordArrayField(record, tag: 2, 0, 4096);
            var players = new AnimationPlayerStateAuthor[checked((int)playersField.Count)];

            for (var index = 0; index < players.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                players[index] = ReadPlayer(record.GetRecordArrayElement(2, index), cancellationToken);
            }

            return AnimationRuntimeAuthor.CreateOwned(ReadInt64(record, tag: 1), players);
        }

        private static WorldAssetReference ReadAssetReference(
            EbinRecordReadView wrapper,
            Guid targetSchemaId)
        {
            ValidateHeader(wrapper, s_assetReferenceSchemaId, fieldCount: 3);
            var assetId = ReadUInt32(wrapper, tag: 1);
            var schemaId = ReadGuid(wrapper, tag: 2);
            var schemaMajor = ReadUInt32(wrapper, tag: 3);

            if ((schemaId != targetSchemaId)
                || (assetId == 0)
                || (assetId > int.MaxValue)
                || (schemaMajor != 1))
            {
                throw new InvalidDataException("The Animation asset reference does not match its fixed major-1 target.");
            }

            return new WorldAssetReference(checked((int)assetId), checked((int)schemaMajor));
        }

        private static bool ReadBoolean(scoped EbinRecordReadView record, uint tag)
        {
            var field = ReadRequiredField(record, tag, kind: 9, count: 1);

            if ((field.Payload.Length != 1) || (field.Payload[0] > 1))
            {
                throw new InvalidDataException($"Animation Boolean field {tag} is invalid.");
            }

            return field.Payload[0] != 0;
        }

        private static float[] ReadFloatFixedArray(scoped EbinRecordReadView record, uint tag)
        {
            var field = ReadRequiredField(record, tag, kind: 23, count: null);
            var count = checked((int)field.Count);
            var payload = field.Payload;

            if ((count is < 1 or > 4)
                || (payload.Length != checked(32 + (count * sizeof(float))))
                || (BinaryPrimitives.ReadUInt16LittleEndian(payload[16..]) != 10)
                || (BinaryPrimitives.ReadUInt32LittleEndian(payload[20..]) != sizeof(float)))
            {
                throw new InvalidDataException("Animation transition SourceWeights does not match its FixedArray schema.");
            }

            var values = new float[count];

            for (var index = 0; index < count; index++)
            {
                values[index] = EbinPrimitives.Single(payload[(32 + (index * sizeof(float)))..]);
            }

            return values;
        }

        private static Guid ReadGuid(scoped EbinRecordReadView record, uint tag)
            => new(ReadRequiredField(record, tag, kind: 11, count: 1).Payload, bigEndian: true);

        private static long ReadInt64(scoped EbinRecordReadView record, uint tag)
            => BinaryPrimitives.ReadInt64LittleEndian(ReadRequiredField(record, tag, kind: 8, count: 1).Payload);

        private static sbyte ReadInt8(scoped EbinRecordReadView record, uint tag)
            => unchecked((sbyte)ReadRequiredField(record, tag, kind: 5, count: 1).Payload[0]);

        private static Guid? ReadOptionalGuid(scoped EbinRecordReadView record, uint tag)
        {
            if (!record.TryGetField(tag, out var field))
            {
                return null;
            }

            RequireFieldShape(field, tag, kind: 11, isRequired: false, count: 1);
            return new Guid(field.Payload, bigEndian: true);
        }

        private static long? ReadOptionalInt64(scoped EbinRecordReadView record, uint tag)
        {
            if (!record.TryGetField(tag, out var field))
            {
                return null;
            }

            RequireFieldShape(field, tag, kind: 8, isRequired: false, count: 1);
            return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
        }

        private static SpatialPosition3 ReadPosition(scoped ReadOnlySpan<byte> payload)
            => new(
                new ChunkIndex3(
                    BinaryPrimitives.ReadInt32LittleEndian(payload),
                    BinaryPrimitives.ReadInt32LittleEndian(payload[sizeof(int)..]),
                    BinaryPrimitives.ReadInt32LittleEndian(payload[(2 * sizeof(int))..])),
                new Vector3(
                    EbinPrimitives.Single(payload[(3 * sizeof(int))..]),
                    EbinPrimitives.Single(payload[((3 * sizeof(int)) + sizeof(float))..]),
                    EbinPrimitives.Single(payload[((3 * sizeof(int)) + (2 * sizeof(float)))..])));

        private static Quaternion ReadQuaternion(scoped EbinRecordReadView record, uint tag)
        {
            var payload = ReadRequiredField(record, tag, kind: 17, count: 1).Payload;
            return new Quaternion(
                EbinPrimitives.Single(payload),
                EbinPrimitives.Single(payload[sizeof(float)..]),
                EbinPrimitives.Single(payload[(2 * sizeof(float))..]),
                EbinPrimitives.Single(payload[(3 * sizeof(float))..]));
        }

        private static EbinFieldReadView ReadRecordArrayField(
            EbinRecordReadView record,
            uint tag,
            ulong minimumCount,
            ulong maximumCount)
        {
            if (!record.TryGetField(tag, out var field)
                || (field.Kind != 24)
                || !field.IsRequired
                || (field.Count < minimumCount)
                || (field.Count > maximumCount)
                || (field.Count > int.MaxValue))
            {
                throw new InvalidDataException($"Animation record-array field {tag} does not match its schema.");
            }

            return field;
        }

        private static EbinFieldReadView ReadRequiredField(
            EbinRecordReadView record,
            uint tag,
            ushort kind,
            ulong? count)
        {
            if (!record.TryGetField(tag, out var field)
                || !field.IsRequired
                || (field.Kind != kind)
                || (count is { } expectedCount && field.Count != expectedCount))
            {
                throw new InvalidDataException($"Animation field {tag} does not match its required schema shape.");
            }

            return field;
        }

        private static EbinRecordReadView ReadRequiredRecord(EbinRecordReadView record, uint tag)
            => new(ReadRequiredField(record, tag, kind: 22, count: 1).Payload);

        private static float ReadSingle(scoped EbinRecordReadView record, uint tag)
            => EbinPrimitives.Single(ReadRequiredField(record, tag, kind: 10, count: 1).Payload);

        private static ushort ReadUInt16(scoped EbinRecordReadView record, uint tag)
            => BinaryPrimitives.ReadUInt16LittleEndian(ReadRequiredField(record, tag, kind: 2, count: 1).Payload);

        private static uint ReadUInt32(scoped EbinRecordReadView record, uint tag)
            => BinaryPrimitives.ReadUInt32LittleEndian(ReadRequiredField(record, tag, kind: 3, count: 1).Payload);

        private static byte ReadUInt8(scoped EbinRecordReadView record, uint tag)
            => ReadRequiredField(record, tag, kind: 1, count: 1).Payload[0];

        private static void RequireFieldShape(
            EbinFieldReadView field,
            uint tag,
            ushort kind,
            bool isRequired,
            ulong count)
        {
            if ((field.Tag != tag)
                || (field.Kind != kind)
                || (field.IsRequired != isRequired)
                || (field.Count != count))
            {
                throw new InvalidDataException($"Animation field {tag} does not match its schema shape.");
            }
        }

        private static void ValidateHeader(scoped EbinRecordReadView record, Guid schemaId, int fieldCount)
        {
            var header = record.Header;

            if ((header.SchemaId != schemaId)
                || (header.Major != 1)
                || (header.Minor != 0)
                || (header.FieldCount != fieldCount))
            {
                throw new NotSupportedException("The Animation record does not match its exact schema tuple.");
            }
        }

        internal struct ReadAction : IAction<EbinRecordReadView>
        {
            private readonly WorldAttachmentReadBinding<AnimationRuntimeAuthor> _binding;
            private readonly CancellationToken _cancellationToken;

            internal ReadAction(WorldAttachmentReadBinding<AnimationRuntimeAuthor> binding, CancellationToken cancellationToken)
            {
                _binding = binding;
                _cancellationToken = cancellationToken;
            }

            internal WorldAttachmentReadResult<AnimationRuntimeAuthor>? Result { get; private set; }

            public void Invoke(scoped EbinRecordReadView record)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                AnimationRuntimeAuthor author;

                try
                {
                    author = ReadWorldState(record, _cancellationToken);
                }
                catch (NotSupportedException exception)
                {
                    Result = _binding.Reject(
                        WorldAttachmentReadFailureKind.SchemaMismatch,
                        0,
                        path: null,
                        $"The Animation playback record uses an unsupported schema tuple: {exception.Message}");
                    return;
                }
                catch (InvalidDataException exception)
                {
                    Result = _binding.Reject(
                        WorldAttachmentReadFailureKind.InvalidField,
                        0,
                        path: null,
                        $"The Animation playback record does not match its registered schema: {exception.Message}");
                    return;
                }
                catch (ArgumentException exception)
                {
                    Result = _binding.Reject(
                        WorldAttachmentReadFailureKind.InvalidValue,
                        0,
                        path: null,
                        $"The Animation playback record contains an invalid author value: {exception.Message}");
                    return;
                }
                catch (OverflowException exception)
                {
                    Result = _binding.Reject(
                        WorldAttachmentReadFailureKind.InvalidValue,
                        0,
                        path: null,
                        $"The Animation playback record exceeds its admitted numeric range: {exception.Message}");
                    return;
                }

                Result = _binding.Complete(in author, record.Header.RecordByteLength);
            }
        }
    }

    private static class Writer
    {
        internal static EbinRecord CreateActiveClipRecord(AnimationActiveClipAuthor value)
        {
            Span<byte> fraction = stackalloc byte[AnimationTickRemainder192.ByteLength];
            value.Fraction.WriteLittleEndian(fraction);
            return new EbinRecord(s_activeClipSchemaId, 1, 0, [
                EbinPrimitives.Guid(1, value.ClipSlotId),
                EbinPrimitives.Int64(2, value.PlaySequence),
                EbinPrimitives.Int64(3, value.LocalTicks),
                EbinPrimitives.Int64(4, value.PreviousTicks),
                EbinPrimitives.Bytes(5, fraction),
                EbinPrimitives.Int64(6, value.LoopOrdinal),
                EbinPrimitives.Int64(7, value.PreviousLoopOrdinal),
                EbinPrimitives.Binary32(8, value.Weight),
                EbinPrimitives.UInt32(9, value.ForwardCursor),
                EbinPrimitives.UInt32(10, value.ReverseCursor),
                EbinPrimitives.Boolean(11, value.StartDelivered),
                EbinPrimitives.Boolean(12, value.Completed),
                EbinPrimitives.Int8(13, value.LastDirection),
            ]);
        }

        internal static EbinRecord CreateAssetReferenceRecord(WorldAssetReference value)
            => new(s_assetReferenceSchemaId, 1, 0, [
                EbinPrimitives.UInt32(1, checked((uint)value.AssetId)),
                EbinPrimitives.Guid(2, new ContentSchemaIdentityAssignment("native/animation-controller").SchemaId),
                EbinPrimitives.UInt32(3, checked((uint)value.SchemaMajor)),
            ]);

        internal static EbinRecord CreateLayerRecord(AnimationLayerStateAuthor value, EbinReadLimits limits)
        {
            var fields = new List<EbinField>(value.Transition is null ? 3 : 4) {
                EbinPrimitives.UInt8(1, value.Index),
                EbinPrimitives.Binary32(2, value.Weight),
                CreateRecordArray(
                    3,
                    value.Clips,
                    static (clip, _) => CreateActiveClipRecord(clip),
                    limits),
            };

            if (value.Transition is { } transition)
            {
                fields.Add(CreateOptionalRecord(4, CreateTransitionRecord(transition, limits), limits));
            }

            return new EbinRecord(s_layerSchemaId, 1, 0, CollectionsMarshal.AsSpan(fields));
        }

        internal static EbinRecord CreateLocalPoseRecord(AnimationLocalPoseAuthor value)
            => new(s_localPoseSchemaId, 1, 0, [
                EbinPrimitives.Geometry(1, value.Position),
                EbinPrimitives.Geometry(2, value.Rotation),
                EbinPrimitives.Geometry(3, value.Scale),
            ]);

        internal static EbinRecord CreatePlayerRecord(AnimationPlayerStateAuthor value, EbinReadLimits limits)
        {
            var optionalCount = (value.LastSampleTick.HasValue ? 1 : 0) + (value.MotionActorId.HasValue ? 1 : 0);
            var fields = new List<EbinField>(7 + optionalCount) {
                EbinPrimitives.Guid(1, value.ActorEntityId),
                EbinPrimitives.Guid(2, value.SceneInstanceId),
                CreateRecordField(3, CreateAssetReferenceRecord(value.Controller), limits),
                EbinPrimitives.Binary32(4, value.Speed),
                EbinPrimitives.Int64(5, value.NextPlaySequence),
            };

            if (value.LastSampleTick is long lastSampleTick)
            {
                fields.Add(CreateOptionalInt64(6, lastSampleTick));
            }

            fields.Add(CreateRecordArray(7, value.Bindings, CreateTargetBindingRecord, limits));
            fields.Add(CreateRecordArray(
                8,
                value.Layers,
                static (layer, profile) => CreateLayerRecord(layer, profile),
                limits));

            if (value.MotionActorId is Guid motionActorId)
            {
                fields.Add(CreateOptionalGuid(9, motionActorId));
            }

            return new EbinRecord(s_playerSchemaId, 1, 0, CollectionsMarshal.AsSpan(fields));
        }

        internal static EbinRecord CreateTargetBindingRecord(
            AnimationTargetBindingAuthor value,
            EbinReadLimits limits)
        {
            var optionalCount = (value.EntityId.HasValue ? 1 : 0) + (value.RootBaseline.HasValue ? 1 : 0);
            var fields = new List<EbinField>(3 + optionalCount) {
                EbinPrimitives.UInt8(1, value.Domain),
                EbinPrimitives.Guid(2, value.SourceKey),
            };

            if (value.EntityId is Guid entityId)
            {
                fields.Add(CreateOptionalGuid(3, entityId));
            }

            fields.Add(CreateRecordField(4, CreateLocalPoseRecord(value.Baseline), limits));

            if (value.RootBaseline is SpatialPosition3 rootBaseline)
            {
                fields.Add(CreateOptionalPosition(5, rootBaseline));
            }

            return new EbinRecord(s_targetBindingSchemaId, 1, 0, CollectionsMarshal.AsSpan(fields));
        }

        internal static EbinRecord CreateTransitionRecord(
            AnimationTransitionStateAuthor value,
            EbinReadLimits limits)
            => new(s_transitionSchemaId, 1, 0, [
                EbinPrimitives.Guid(1, value.TransitionId),
                EbinPrimitives.Int64(2, value.ElapsedTicks),
                EbinPrimitives.UInt16(3, value.Fraction),
                CreateFloatFixedArray(4, value.SourceWeights, limits),
                EbinPrimitives.Guid(5, value.TargetClipSlotId),
            ]);

        internal static EbinRecord CreateWorldRecord(AnimationRuntimeAuthor value, EbinReadLimits limits)
            => new(s_worldStateSchemaId, 1, 0, [
                EbinPrimitives.Int64(1, value.NextCommandSequence),
                CreateRecordArray(
                    2,
                    value.Players,
                    static (player, profile) => CreatePlayerRecord(player, profile),
                    limits),
            ]);

        internal static byte[] EncodeWorldRecord(AnimationRuntimeAuthor value, EbinReadLimits limits)
            => EncodeRecord(CreateWorldRecord(value, limits), limits);

        private static EbinField CreateFloatFixedArray(
            uint tag,
            ImmutableArray<float> values,
            EbinReadLimits limits)
        {
            var destination = new byte[checked(32 + (values.Length * sizeof(float)))];
            var status = EbinPayloadWriter.WriteFixedArray(
                values.AsSpan(),
                limits,
                destination,
                out var written,
                out var diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException($"The Animation transition weights cannot be encoded: {diagnostic?.Category ?? status.ToString()}.");
            }

            return new EbinField(tag, 23, 1, checked((ulong)values.Length), destination.AsSpan(0, written));
        }

        private static EbinField CreateOptionalGuid(uint tag, Guid value)
        {
            Span<byte> payload = stackalloc byte[16];
            _ = value.TryWriteBytes(payload, bigEndian: true, out _);
            return new EbinField(tag, 11, 0, 1, payload);
        }

        private static EbinField CreateOptionalInt64(uint tag, long value)
        {
            Span<byte> payload = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(payload, value);
            return new EbinField(tag, 8, 0, 1, payload);
        }

        private static EbinField CreateOptionalPosition(uint tag, SpatialPosition3 value)
        {
            var payload = new byte[24];
            BinaryPrimitives.WriteInt32LittleEndian(payload, value.Chunk.X);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(sizeof(int)), value.Chunk.Y);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(2 * sizeof(int)), value.Chunk.Z);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(3 * sizeof(int)), BitConverter.SingleToInt32Bits(value.Offset.X));
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan((3 * sizeof(int)) + sizeof(float)), BitConverter.SingleToInt32Bits(value.Offset.Y));
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan((3 * sizeof(int)) + (2 * sizeof(float))), BitConverter.SingleToInt32Bits(value.Offset.Z));
            return new EbinField(tag, 21, 0, 1, payload);
        }

        private static EbinField CreateOptionalRecord(uint tag, EbinRecord value, EbinReadLimits limits)
            => CreateRecordField(tag, value, limits, isRequired: false);

        private static EbinField CreateRecordArray<T>(
            uint tag,
            ImmutableArray<T> values,
            Func<T, EbinReadLimits, EbinRecord> createRecord,
            EbinReadLimits limits)
            where T : class
        {
            var encodedRecords = new byte[values.Length][];
            var encodedBytes = 0;

            for (var index = 0; index < values.Length; index++)
            {
                encodedRecords[index] = EncodeRecord(createRecord(values[index], limits), limits);
                encodedBytes = checked(encodedBytes + encodedRecords[index].Length);
            }

            var childStorage = new byte[encodedBytes];
            var childSlices = new EbinPayloadSlice[encodedRecords.Length];
            var childOffset = 0;

            for (var index = 0; index < encodedRecords.Length; index++)
            {
                var encodedRecord = encodedRecords[index];
                encodedRecord.AsSpan().CopyTo(childStorage.AsSpan(childOffset));
                childSlices[index] = new EbinPayloadSlice(childOffset, encodedRecord.Length);
                childOffset = checked(childOffset + encodedRecord.Length);
            }

            var measureStatus = EbinPayloadWriter.MeasureRecordArray(
                childSlices,
                childStorage,
                limits,
                out var requiredBytes,
                out var measureDiagnostic);

            if (measureStatus is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(
                    $"The Animation record array cannot be measured: {measureDiagnostic?.Category ?? measureStatus.ToString()}.");
            }

            var payload = new byte[requiredBytes];
            var writeStatus = EbinPayloadWriter.WriteRecordArray(
                childSlices,
                childStorage,
                limits,
                payload,
                out var payloadBytes,
                out var writeDiagnostic);

            if (writeStatus is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(
                    $"The Animation record array cannot be encoded: {writeDiagnostic?.Category ?? writeStatus.ToString()}.");
            }

            return new EbinField(tag, 24, 1, checked((ulong)encodedRecords.Length), payload.AsSpan(0, payloadBytes));
        }

        private static EbinField CreateRecordField(
            uint tag,
            EbinRecord record,
            EbinReadLimits limits,
            bool isRequired = true)
        {
            var payload = EncodeRecord(record, limits);
            return new EbinField(tag, 22, isRequired ? (ushort)1 : (ushort)0, 1, payload);
        }

        private static byte[] EncodeRecord(EbinRecord record, EbinReadLimits limits)
        {
            var fields = new EbinFieldSource[record.Fields.Length];
            var payloadStorageBytes = 0;

            for (var index = 0; index < record.Fields.Length; index++)
            {
                payloadStorageBytes = checked(payloadStorageBytes + record.Fields[index].Payload.Length);
            }

            var payloadStorage = new byte[payloadStorageBytes];
            var payloadOffset = 0;

            for (var index = 0; index < record.Fields.Length; index++)
            {
                var field = record.Fields[index];
                field.Payload.CopyTo(payloadStorage.AsSpan(payloadOffset));
                fields[index] = new EbinFieldSource(
                    field.Tag,
                    field.Kind,
                    field.Flags,
                    field.Count,
                    new EbinPayloadSlice(payloadOffset, field.Payload.Length));
                payloadOffset = checked(payloadOffset + field.Payload.Length);
            }

            var source = new EbinRecordWriteView(
                record.SchemaId,
                checked((int)record.Major),
                checked((int)record.Minor),
                fields,
                payloadStorage);
            var measureStatus = EbinStructuralWriter.Measure(in source, limits, out var recordBytes, out var measureDiagnostic);

            if (measureStatus is not EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(
                    $"The Animation record cannot be measured: {measureDiagnostic?.Category ?? measureStatus.ToString()}.");
            }

            var candidate = new byte[recordBytes];
            var destination = new byte[recordBytes];
            var writeStatus = EbinStructuralWriter.Write(
                in source,
                limits,
                candidate,
                destination,
                out var bytesWritten,
                out var writeDiagnostic);

            if ((writeStatus is not EbinWriteStatus.Complete) || (bytesWritten != recordBytes))
            {
                throw new InvalidOperationException(
                    $"The Animation record cannot be encoded: {writeDiagnostic?.Category ?? writeStatus.ToString()}.");
            }

            return destination;
        }
    }
}
