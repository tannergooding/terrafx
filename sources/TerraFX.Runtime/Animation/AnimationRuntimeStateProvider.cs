// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Animation;

internal sealed class AnimationRuntimeStateProvider : IWorldAttachmentStateProvider<AnimationRuntimeState, AnimationRuntimeAuthor>
{
    private static readonly Guid s_factoryId = FoundationNameUuid.Create(
        new Guid("2ed8b750-e1a0-4b46-9000-000000000001"),
        "attachment/animation/world-state/factory/v1");
    private readonly WorldAttachmentFactoryBinding<AnimationRuntimeState, AnimationRuntimeAuthor> _binding;
    private readonly CompiledContentSchemaRegistry _schemas;

    internal AnimationRuntimeStateProvider(CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        _schemas = schemas;
        _binding = CreateBinding(schemas, this);
    }

    internal WorldAttachmentFactoryBinding<AnimationRuntimeState, AnimationRuntimeAuthor> Binding => _binding;

    public nint Count(
        AnimationRuntimeState value,
        WorldAttachmentCaptureBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();

        if (!binding.IsBoundTo(_binding.Descriptor))
        {
            throw new ArgumentException("The capture binding belongs to another Animation attachment family.", nameof(binding));
        }

        return AnimationRuntimeStateCodec.Encode(value.Value, binding.Limits).Length;
    }

    public WorldAttachmentReadResult<AnimationRuntimeAuthor> Read(
        scoped ReadOnlySpan<byte> source,
        WorldAttachmentReadBinding<AnimationRuntimeAuthor> binding,
        CancellationToken cancellationToken)
        => AnimationRuntimeStateCodec.Read(source, binding, cancellationToken);

    public void Release(AnimationRuntimeState value, WorldAttachmentReleaseBinding binding)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(binding);
    }

    public AnimationRuntimeState Restore(
        in AnimationRuntimeAuthor author,
        WorldAttachmentRestoreBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        return new AnimationRuntimeState(author);
    }

    public ImmutableArray<Diagnostic> Validate(
        AnimationRuntimeState value,
        WorldAttachmentValidationBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();
        return [];
    }

    public nint Write(
        AnimationRuntimeState value,
        scoped Span<byte> destination,
        WorldAttachmentCaptureBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();

        if (!binding.IsBoundTo(_binding.Descriptor))
        {
            throw new ArgumentException("The capture binding belongs to another Animation attachment family.", nameof(binding));
        }

        var encoded = AnimationRuntimeStateCodec.Encode(value.Value, binding.Limits);

        if (destination.Length < encoded.Length)
        {
            throw new InvalidOperationException("The Animation attachment destination is smaller than its canonical record.");
        }

        encoded.AsSpan().CopyTo(destination);
        cancellationToken.ThrowIfCancellationRequested();
        return encoded.Length;
    }

    private WorldAttachmentFactoryBinding<AnimationRuntimeState, AnimationRuntimeAuthor> CreateBinding(
        CompiledContentSchemaRegistry schemas,
        AnimationRuntimeStateProvider provider)
    {
        var rootSchema = GetSchema(schemas, "animation/world-state");
        var playerSchema = GetSchema(schemas, "animation/player-state");
        var targetBindingSchema = GetSchema(schemas, "animation/target-binding");
        var layerSchema = GetSchema(schemas, "animation/layer-state");
        var activeClipSchema = GetSchema(schemas, "animation/active-clip");
        var transitionSchema = GetSchema(schemas, "animation/transition-state");
        var localPoseSchema = GetSchema(schemas, "scene/local-transform");
        var controllerSchema = GetSchema(schemas, "native/animation-controller");

        var localPose = WorldAttachmentRecordBinding<AnimationLocalPoseAuthor, AnimationLocalPoseAuthor>.Create(
            schemas,
            localPoseSchema.Schema,
            [
                WorldAttachmentRecordFieldBinding<AnimationLocalPoseAuthor, AnimationLocalPoseAuthor>.Value<
                    TerraFX.Numerics.Vector3,
                    Readers.ValueReader<AnimationLocalPoseAuthor, TerraFX.Numerics.Vector3>,
                    Readers.ValueReader<AnimationLocalPoseAuthor, TerraFX.Numerics.Vector3>>(
                        GetField(localPoseSchema, 1),
                        ContentReferenceKind.None,
                        new(static value => value.Position),
                        new(static value => value.Position)),
                WorldAttachmentRecordFieldBinding<AnimationLocalPoseAuthor, AnimationLocalPoseAuthor>.Value<
                    TerraFX.Numerics.Quaternion,
                    Readers.ValueReader<AnimationLocalPoseAuthor, TerraFX.Numerics.Quaternion>,
                    Readers.ValueReader<AnimationLocalPoseAuthor, TerraFX.Numerics.Quaternion>>(
                        GetField(localPoseSchema, 2),
                        ContentReferenceKind.None,
                        new(static value => value.Rotation),
                        new(static value => value.Rotation)),
                WorldAttachmentRecordFieldBinding<AnimationLocalPoseAuthor, AnimationLocalPoseAuthor>.Value<
                    TerraFX.Numerics.Vector3,
                    Readers.ValueReader<AnimationLocalPoseAuthor, TerraFX.Numerics.Vector3>,
                    Readers.ValueReader<AnimationLocalPoseAuthor, TerraFX.Numerics.Vector3>>(
                        GetField(localPoseSchema, 3),
                        ContentReferenceKind.None,
                        new(static value => value.Scale),
                        new(static value => value.Scale)),
            ]);

        var targetBinding = WorldAttachmentRecordBinding<AnimationTargetBindingAuthor, AnimationTargetBindingAuthor>.Create(
            schemas,
            targetBindingSchema.Schema,
            [
                WorldAttachmentRecordFieldBinding<AnimationTargetBindingAuthor, AnimationTargetBindingAuthor>.Value<
                    byte,
                    Readers.ValueReader<AnimationTargetBindingAuthor, byte>,
                    Readers.ValueReader<AnimationTargetBindingAuthor, byte>>(
                        GetField(targetBindingSchema, 1),
                        ContentReferenceKind.None,
                        new(static value => value.Domain),
                        new(static value => value.Domain)),
                WorldAttachmentRecordFieldBinding<AnimationTargetBindingAuthor, AnimationTargetBindingAuthor>.Value<
                    Guid,
                    Readers.ValueReader<AnimationTargetBindingAuthor, Guid>,
                    Readers.ValueReader<AnimationTargetBindingAuthor, Guid>>(
                        GetField(targetBindingSchema, 2),
                        ContentReferenceKind.None,
                        new(static value => value.SourceKey),
                        new(static value => value.SourceKey)),
                WorldAttachmentRecordFieldBinding<AnimationTargetBindingAuthor, AnimationTargetBindingAuthor>.Optional<
                    Guid,
                    Readers.OptionalReader<AnimationTargetBindingAuthor, Guid>,
                    Readers.OptionalReader<AnimationTargetBindingAuthor, Guid>>(
                        GetField(targetBindingSchema, 3),
                        ContentReferenceKind.Entity,
                        new(static value => value.EntityId.HasValue, static value => value.EntityId.GetValueOrDefault()),
                        new(static value => value.EntityId.HasValue, static value => value.EntityId.GetValueOrDefault())),
                WorldAttachmentRecordFieldBinding<AnimationTargetBindingAuthor, AnimationTargetBindingAuthor>.Record<
                    AnimationLocalPoseAuthor,
                    AnimationLocalPoseAuthor,
                    Readers.ValueReader<AnimationTargetBindingAuthor, AnimationLocalPoseAuthor>,
                    Readers.ValueReader<AnimationTargetBindingAuthor, AnimationLocalPoseAuthor>>(
                        GetField(targetBindingSchema, 4),
                        localPose,
                        new(static value => value.Baseline),
                        new(static value => value.Baseline)),
                WorldAttachmentRecordFieldBinding<AnimationTargetBindingAuthor, AnimationTargetBindingAuthor>.Optional<
                    TerraFX.SpatialPosition3,
                    Readers.OptionalReader<AnimationTargetBindingAuthor, TerraFX.SpatialPosition3>,
                    Readers.OptionalReader<AnimationTargetBindingAuthor, TerraFX.SpatialPosition3>>(
                        GetField(targetBindingSchema, 5),
                        ContentReferenceKind.None,
                        new(static value => value.RootBaseline.HasValue, static value => value.RootBaseline.GetValueOrDefault()),
                        new(static value => value.RootBaseline.HasValue, static value => value.RootBaseline.GetValueOrDefault())),
            ]);

        var transition = WorldAttachmentRecordBinding<AnimationTransitionStateAuthor, AnimationTransitionStateAuthor>.Create(
            schemas,
            transitionSchema.Schema,
            [
                WorldAttachmentRecordFieldBinding<AnimationTransitionStateAuthor, AnimationTransitionStateAuthor>.Value<
                    Guid,
                    Readers.ValueReader<AnimationTransitionStateAuthor, Guid>,
                    Readers.ValueReader<AnimationTransitionStateAuthor, Guid>>(
                        GetField(transitionSchema, 1),
                        ContentReferenceKind.None,
                        new(static value => value.TransitionId),
                        new(static value => value.TransitionId)),
                WorldAttachmentRecordFieldBinding<AnimationTransitionStateAuthor, AnimationTransitionStateAuthor>.Value<
                    long,
                    Readers.ValueReader<AnimationTransitionStateAuthor, long>,
                    Readers.ValueReader<AnimationTransitionStateAuthor, long>>(
                        GetField(transitionSchema, 2),
                        ContentReferenceKind.None,
                        new(static value => value.ElapsedTicks),
                        new(static value => value.ElapsedTicks)),
                WorldAttachmentRecordFieldBinding<AnimationTransitionStateAuthor, AnimationTransitionStateAuthor>.Value<
                    ushort,
                    Readers.ValueReader<AnimationTransitionStateAuthor, ushort>,
                    Readers.ValueReader<AnimationTransitionStateAuthor, ushort>>(
                        GetField(transitionSchema, 3),
                        ContentReferenceKind.None,
                        new(static value => value.Fraction),
                        new(static value => value.Fraction)),
                WorldAttachmentRecordFieldBinding<AnimationTransitionStateAuthor, AnimationTransitionStateAuthor>.FixedValues<
                    float,
                    Readers.ArrayReader<AnimationTransitionStateAuthor, float>,
                    Readers.ArrayReader<AnimationTransitionStateAuthor, float>>(
                        GetField(transitionSchema, 4),
                        new(static value => value.SourceWeights.Length, static (value, index) => value.SourceWeights[index]),
                        new(static value => value.SourceWeights.Length, static (value, index) => value.SourceWeights[index])),
                WorldAttachmentRecordFieldBinding<AnimationTransitionStateAuthor, AnimationTransitionStateAuthor>.Value<
                    Guid,
                    Readers.ValueReader<AnimationTransitionStateAuthor, Guid>,
                    Readers.ValueReader<AnimationTransitionStateAuthor, Guid>>(
                        GetField(transitionSchema, 5),
                        ContentReferenceKind.None,
                        new(static value => value.TargetClipSlotId),
                        new(static value => value.TargetClipSlotId)),
            ]);

        var activeClip = WorldAttachmentRecordBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Create(
            schemas,
            activeClipSchema.Schema,
            [
                WorldAttachmentRecordFieldBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Value<
                    Guid,
                    Readers.ValueReader<AnimationActiveClipAuthor, Guid>,
                    Readers.ValueReader<AnimationActiveClipAuthor, Guid>>(
                        GetField(activeClipSchema, 1),
                        ContentReferenceKind.None,
                        new(static value => value.ClipSlotId),
                        new(static value => value.ClipSlotId)),
                WorldAttachmentRecordFieldBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Value<
                    long,
                    Readers.ValueReader<AnimationActiveClipAuthor, long>,
                    Readers.ValueReader<AnimationActiveClipAuthor, long>>(
                        GetField(activeClipSchema, 2),
                        ContentReferenceKind.None,
                        new(static value => value.PlaySequence),
                        new(static value => value.PlaySequence)),
                WorldAttachmentRecordFieldBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Value<
                    long,
                    Readers.ValueReader<AnimationActiveClipAuthor, long>,
                    Readers.ValueReader<AnimationActiveClipAuthor, long>>(
                        GetField(activeClipSchema, 3),
                        ContentReferenceKind.None,
                        new(static value => value.LocalTicks),
                        new(static value => value.LocalTicks)),
                WorldAttachmentRecordFieldBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Value<
                    long,
                    Readers.ValueReader<AnimationActiveClipAuthor, long>,
                    Readers.ValueReader<AnimationActiveClipAuthor, long>>(
                        GetField(activeClipSchema, 4),
                        ContentReferenceKind.None,
                        new(static value => value.PreviousTicks),
                        new(static value => value.PreviousTicks)),
                WorldAttachmentRecordFieldBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Bytes<
                    Readers.ArrayReader<AnimationActiveClipAuthor, byte>,
                    Readers.ArrayReader<AnimationActiveClipAuthor, byte>>(
                        GetField(activeClipSchema, 5),
                        new(static value => AnimationTickRemainder192.ByteLength, static (value, index) => value.GetFractionByte(index)),
                        new(static value => AnimationTickRemainder192.ByteLength, static (value, index) => value.GetFractionByte(index))),
                WorldAttachmentRecordFieldBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Value<
                    long,
                    Readers.ValueReader<AnimationActiveClipAuthor, long>,
                    Readers.ValueReader<AnimationActiveClipAuthor, long>>(
                        GetField(activeClipSchema, 6),
                        ContentReferenceKind.None,
                        new(static value => value.LoopOrdinal),
                        new(static value => value.LoopOrdinal)),
                WorldAttachmentRecordFieldBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Value<
                    long,
                    Readers.ValueReader<AnimationActiveClipAuthor, long>,
                    Readers.ValueReader<AnimationActiveClipAuthor, long>>(
                        GetField(activeClipSchema, 7),
                        ContentReferenceKind.None,
                        new(static value => value.PreviousLoopOrdinal),
                        new(static value => value.PreviousLoopOrdinal)),
                WorldAttachmentRecordFieldBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Value<
                    float,
                    Readers.ValueReader<AnimationActiveClipAuthor, float>,
                    Readers.ValueReader<AnimationActiveClipAuthor, float>>(
                        GetField(activeClipSchema, 8),
                        ContentReferenceKind.None,
                        new(static value => value.Weight),
                        new(static value => value.Weight)),
                WorldAttachmentRecordFieldBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Value<
                    uint,
                    Readers.ValueReader<AnimationActiveClipAuthor, uint>,
                    Readers.ValueReader<AnimationActiveClipAuthor, uint>>(
                        GetField(activeClipSchema, 9),
                        ContentReferenceKind.None,
                        new(static value => value.ForwardCursor),
                        new(static value => value.ForwardCursor)),
                WorldAttachmentRecordFieldBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Value<
                    uint,
                    Readers.ValueReader<AnimationActiveClipAuthor, uint>,
                    Readers.ValueReader<AnimationActiveClipAuthor, uint>>(
                        GetField(activeClipSchema, 10),
                        ContentReferenceKind.None,
                        new(static value => value.ReverseCursor),
                        new(static value => value.ReverseCursor)),
                WorldAttachmentRecordFieldBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Value<
                    bool,
                    Readers.ValueReader<AnimationActiveClipAuthor, bool>,
                    Readers.ValueReader<AnimationActiveClipAuthor, bool>>(
                        GetField(activeClipSchema, 11),
                        ContentReferenceKind.None,
                        new(static value => value.StartDelivered),
                        new(static value => value.StartDelivered)),
                WorldAttachmentRecordFieldBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Value<
                    bool,
                    Readers.ValueReader<AnimationActiveClipAuthor, bool>,
                    Readers.ValueReader<AnimationActiveClipAuthor, bool>>(
                        GetField(activeClipSchema, 12),
                        ContentReferenceKind.None,
                        new(static value => value.Completed),
                        new(static value => value.Completed)),
                WorldAttachmentRecordFieldBinding<AnimationActiveClipAuthor, AnimationActiveClipAuthor>.Value<
                    sbyte,
                    Readers.ValueReader<AnimationActiveClipAuthor, sbyte>,
                    Readers.ValueReader<AnimationActiveClipAuthor, sbyte>>(
                        GetField(activeClipSchema, 13),
                        ContentReferenceKind.None,
                        new(static value => value.LastDirection),
                        new(static value => value.LastDirection)),
            ]);

        var layer = WorldAttachmentRecordBinding<AnimationLayerStateAuthor, AnimationLayerStateAuthor>.Create(
            schemas,
            layerSchema.Schema,
            [
                WorldAttachmentRecordFieldBinding<AnimationLayerStateAuthor, AnimationLayerStateAuthor>.Value<
                    byte,
                    Readers.ValueReader<AnimationLayerStateAuthor, byte>,
                    Readers.ValueReader<AnimationLayerStateAuthor, byte>>(
                        GetField(layerSchema, 1),
                        ContentReferenceKind.None,
                        new(static value => value.Index),
                        new(static value => value.Index)),
                WorldAttachmentRecordFieldBinding<AnimationLayerStateAuthor, AnimationLayerStateAuthor>.Value<
                    float,
                    Readers.ValueReader<AnimationLayerStateAuthor, float>,
                    Readers.ValueReader<AnimationLayerStateAuthor, float>>(
                        GetField(layerSchema, 2),
                        ContentReferenceKind.None,
                        new(static value => value.Weight),
                        new(static value => value.Weight)),
                WorldAttachmentRecordFieldBinding<AnimationLayerStateAuthor, AnimationLayerStateAuthor>.Records<
                    AnimationActiveClipAuthor,
                    AnimationActiveClipAuthor,
                    Readers.ArrayReader<AnimationLayerStateAuthor, AnimationActiveClipAuthor>,
                    Readers.ArrayReader<AnimationLayerStateAuthor, AnimationActiveClipAuthor>>(
                        GetField(layerSchema, 3),
                        activeClip,
                        new(static value => value.Clips.Length, static (value, index) => value.Clips[index]),
                        new(static value => value.Clips.Length, static (value, index) => value.Clips[index])),
                WorldAttachmentRecordFieldBinding<AnimationLayerStateAuthor, AnimationLayerStateAuthor>.OptionalRecord<
                    AnimationTransitionStateAuthor,
                    AnimationTransitionStateAuthor,
                    Readers.OptionalReader<AnimationLayerStateAuthor, AnimationTransitionStateAuthor>,
                    Readers.OptionalReader<AnimationLayerStateAuthor, AnimationTransitionStateAuthor>>(
                        GetField(layerSchema, 4),
                        transition,
                        new(static value => value.Transition is not null, static value => value.Transition
                            ?? throw new InvalidOperationException("The Animation layer transition is absent.")),
                        new(static value => value.Transition is not null, static value => value.Transition
                            ?? throw new InvalidOperationException("The Animation layer transition is absent."))),
            ]);

        var player = WorldAttachmentRecordBinding<AnimationPlayerStateAuthor, AnimationPlayerStateAuthor>.Create(
            schemas,
            playerSchema.Schema,
            [
                WorldAttachmentRecordFieldBinding<AnimationPlayerStateAuthor, AnimationPlayerStateAuthor>.Value<
                    Guid,
                    Readers.ValueReader<AnimationPlayerStateAuthor, Guid>,
                    Readers.ValueReader<AnimationPlayerStateAuthor, Guid>>(
                        GetField(playerSchema, 1),
                        ContentReferenceKind.Entity,
                        new(static value => value.ActorEntityId),
                        new(static value => value.ActorEntityId)),
                WorldAttachmentRecordFieldBinding<AnimationPlayerStateAuthor, AnimationPlayerStateAuthor>.Value<
                    Guid,
                    Readers.ValueReader<AnimationPlayerStateAuthor, Guid>,
                    Readers.ValueReader<AnimationPlayerStateAuthor, Guid>>(
                        GetField(playerSchema, 2),
                        ContentReferenceKind.SceneInstance,
                        new(static value => value.SceneInstanceId),
                        new(static value => value.SceneInstanceId)),
                WorldAttachmentRecordFieldBinding<AnimationPlayerStateAuthor, AnimationPlayerStateAuthor>.AssetReference<
                    Readers.ValueReader<AnimationPlayerStateAuthor, WorldAssetReference>,
                    Readers.ValueReader<AnimationPlayerStateAuthor, WorldAssetReference>>(
                        GetField(playerSchema, 3),
                        controllerSchema.Schema.SchemaId,
                        [1],
                        new(static value => value.Controller),
                        new(static value => value.Controller)),
                WorldAttachmentRecordFieldBinding<AnimationPlayerStateAuthor, AnimationPlayerStateAuthor>.Value<
                    float,
                    Readers.ValueReader<AnimationPlayerStateAuthor, float>,
                    Readers.ValueReader<AnimationPlayerStateAuthor, float>>(
                        GetField(playerSchema, 4),
                        ContentReferenceKind.None,
                        new(static value => value.Speed),
                        new(static value => value.Speed)),
                WorldAttachmentRecordFieldBinding<AnimationPlayerStateAuthor, AnimationPlayerStateAuthor>.Value<
                    long,
                    Readers.ValueReader<AnimationPlayerStateAuthor, long>,
                    Readers.ValueReader<AnimationPlayerStateAuthor, long>>(
                        GetField(playerSchema, 5),
                        ContentReferenceKind.None,
                        new(static value => value.NextPlaySequence),
                        new(static value => value.NextPlaySequence)),
                WorldAttachmentRecordFieldBinding<AnimationPlayerStateAuthor, AnimationPlayerStateAuthor>.Optional<
                    long,
                    Readers.OptionalReader<AnimationPlayerStateAuthor, long>,
                    Readers.OptionalReader<AnimationPlayerStateAuthor, long>>(
                        GetField(playerSchema, 6),
                        ContentReferenceKind.None,
                        new(static value => value.LastSampleTick.HasValue, static value => value.LastSampleTick.GetValueOrDefault()),
                        new(static value => value.LastSampleTick.HasValue, static value => value.LastSampleTick.GetValueOrDefault())),
                WorldAttachmentRecordFieldBinding<AnimationPlayerStateAuthor, AnimationPlayerStateAuthor>.Records<
                    AnimationTargetBindingAuthor,
                    AnimationTargetBindingAuthor,
                    Readers.ArrayReader<AnimationPlayerStateAuthor, AnimationTargetBindingAuthor>,
                    Readers.ArrayReader<AnimationPlayerStateAuthor, AnimationTargetBindingAuthor>>(
                        GetField(playerSchema, 7),
                        targetBinding,
                        new(static value => value.Bindings.Length, static (value, index) => value.Bindings[index]),
                        new(static value => value.Bindings.Length, static (value, index) => value.Bindings[index])),
                WorldAttachmentRecordFieldBinding<AnimationPlayerStateAuthor, AnimationPlayerStateAuthor>.Records<
                    AnimationLayerStateAuthor,
                    AnimationLayerStateAuthor,
                    Readers.ArrayReader<AnimationPlayerStateAuthor, AnimationLayerStateAuthor>,
                    Readers.ArrayReader<AnimationPlayerStateAuthor, AnimationLayerStateAuthor>>(
                        GetField(playerSchema, 8),
                        layer,
                        new(static value => value.Layers.Length, static (value, index) => value.Layers[index]),
                        new(static value => value.Layers.Length, static (value, index) => value.Layers[index])),
                WorldAttachmentRecordFieldBinding<AnimationPlayerStateAuthor, AnimationPlayerStateAuthor>.Optional<
                    Guid,
                    Readers.OptionalReader<AnimationPlayerStateAuthor, Guid>,
                    Readers.OptionalReader<AnimationPlayerStateAuthor, Guid>>(
                        GetField(playerSchema, 9),
                        ContentReferenceKind.Entity,
                        new(static value => value.MotionActorId.HasValue, static value => value.MotionActorId.GetValueOrDefault()),
                        new(static value => value.MotionActorId.HasValue, static value => value.MotionActorId.GetValueOrDefault())),
            ]);

        var runtimeRoot = WorldAttachmentRecordBinding<AnimationRuntimeState, AnimationRuntimeAuthor>.Create(
            schemas,
            rootSchema.Schema,
            [
                WorldAttachmentRecordFieldBinding<AnimationRuntimeState, AnimationRuntimeAuthor>.Value<
                    long,
                    Readers.ValueReader<AnimationRuntimeState, long>,
                    Readers.ValueReader<AnimationRuntimeAuthor, long>>(
                        GetField(rootSchema, 1),
                        ContentReferenceKind.None,
                        new(static value => value.Value.NextCommandSequence),
                        new(static value => value.NextCommandSequence)),
                WorldAttachmentRecordFieldBinding<AnimationRuntimeState, AnimationRuntimeAuthor>.Records<
                    AnimationPlayerStateAuthor,
                    AnimationPlayerStateAuthor,
                    Readers.ArrayReader<AnimationRuntimeState, AnimationPlayerStateAuthor>,
                    Readers.ArrayReader<AnimationRuntimeAuthor, AnimationPlayerStateAuthor>>(
                        GetField(rootSchema, 2),
                        player,
                        new(static value => value.Value.Players.Length, static (value, index) => value.Value.Players[index]),
                        new(static value => value.Players.Length, static (value, index) => value.Players[index])),
            ]);
        runtimeRoot.ValidateNestedRoles();

        return WorldAttachmentFactoryBinding<AnimationRuntimeState, AnimationRuntimeAuthor>.CreateAggregate(
            s_factoryId,
            factoryVersion: 1,
            schemas,
            rootSchema.Schema,
            runtimeRoot,
            provider);
    }

    private ContentFieldDescriptor GetField(DataSchema schema, uint tag)
    {
        if (!_schemas.TryGetSchema(schema.Schema, out var registeredSchema)
            || !ReferenceEquals(registeredSchema, schema))
        {
            throw new ArgumentException("The selected Animation schema does not belong to this provider registry.", nameof(schema));
        }

        var index = checked((int)tag - 1);

        if ((index < 0)
            || (index >= schema.Fields.Length)
            || (schema.Fields[index].Tag != tag))
        {
            throw new ArgumentException("The Animation playback schema does not contain the expected ordered field.", nameof(schema));
        }

        return schema.Fields[index];
    }

    private DataSchema GetSchema(CompiledContentSchemaRegistry schemas, string name)
    {
        if (!ReferenceEquals(_schemas, schemas))
        {
            throw new ArgumentException("The Animation provider binding must use its captured compiled registry.", nameof(schemas));
        }

        var schemaId = new ContentSchemaIdentityAssignment(name).SchemaId;
        var reference = new ContentSchemaReference(schemaId, 1, 0, ContentSchemaScope.Content);

        if (!_schemas.TryGetSchema(reference, out var schema))
        {
            throw new ArgumentException($"The compiled registry does not contain the required Animation schema '{name}'.", nameof(schemas));
        }

        return schema;
    }

    private sealed class Readers
    {
        internal readonly struct ArrayReader<TSource, TElement> : IComponentArrayReader<TSource, TElement>
        {
            private readonly Func<TSource, int> _getCount;
            private readonly Func<TSource, int, TElement> _getElement;

            internal ArrayReader(Func<TSource, int> getCount, Func<TSource, int, TElement> getElement)
            {
                _getCount = getCount;
                _getElement = getElement;
            }

            public int GetCount(in TSource value) => _getCount(value);

            public TElement GetElement(in TSource value, int index) => _getElement(value, index);
        }

        internal readonly struct OptionalReader<TSource, TValue> : IComponentOptionalReader<TSource, TValue>
        {
            private readonly Func<TSource, TValue> _getPresent;
            private readonly Func<TSource, bool> _isPresent;

            internal OptionalReader(Func<TSource, bool> isPresent, Func<TSource, TValue> getPresent)
            {
                _isPresent = isPresent;
                _getPresent = getPresent;
            }

            public TValue GetPresent(in TSource value) => _getPresent(value);

            public bool IsPresent(in TSource value) => _isPresent(value);
        }

        internal readonly struct ValueReader<TSource, TValue> : IComponentValueReader<TSource, TValue>
        {
            private readonly Func<TSource, TValue> _get;

            internal ValueReader(Func<TSource, TValue> get)
            {
                _get = get;
            }

            public TValue Get(in TSource value) => _get(value);
        }
    }
}
