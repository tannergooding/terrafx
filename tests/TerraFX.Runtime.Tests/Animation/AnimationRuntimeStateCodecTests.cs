// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Numerics;
using TerraFX.Runtime.Tests.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Animation;

internal sealed class AnimationRuntimeStateCodecTests
{
    private static CompiledContentSchemaRegistry CreateRegistry()
        => new([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.Schemas,
            .. AnimationSchemaCatalogue.PlaybackSchemas,
        ]);

    [Test]
    public void AggregateBindingRegistersAndRoundTripsEmptyWorldState()
    {
        var schemas = CreateRegistry();
        var provider = new AnimationRuntimeStateProvider(schemas);
        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterAttachment(AttachmentScope.World, provider.Binding);
        _ = builder.Build();
        var author = new AnimationRuntimeAuthor(1, []);
        provider.Binding.ValidateAuthor(in author);
        var encoded = AnimationRuntimeStateCodec.Encode(author);
        var readBinding = new WorldAttachmentReadBinding<AnimationRuntimeAuthor>(
            provider.Binding.Descriptor.Schema,
            encoded.Length);

        try
        {
            var result = provider.Read(encoded, readBinding, CancellationToken.None);
            result = readBinding.ValidateComplete(result);

            if (!result.TryGetAuthorValue(out var decoded))
            {
                throw new InvalidOperationException("The Animation codec rejected its own encoded empty state.");
            }

            Assert.Multiple(() => {
                Assert.That(result.Status, Is.EqualTo(WorldAttachmentReadStatus.Read));
                Assert.That(result.ConsumedBytes, Is.EqualTo((nint)encoded.Length));
                Assert.That(result.Diagnostics, Is.Empty);
                Assert.That(decoded.NextCommandSequence, Is.EqualTo(1));
                Assert.That(decoded.Players, Is.Empty);
                Assert.That(AnimationRuntimeStateCodec.Encode(decoded), Is.EqualTo(encoded));
            });
        }
        finally
        {
            readBinding.Revoke();
        }
    }

    [Test]
    public void AggregateBindingRoundTripsNestedReferencesTransitionsAndExactScalarBits()
    {
        var schemas = CreateRegistry();
        var provider = new AnimationRuntimeStateProvider(schemas);
        var actorId = new Guid("10000000-0000-4000-8000-000000000001");
        var targetId = new Guid("10000000-0000-4000-8000-000000000002");
        var slotId = new Guid("20000000-0000-4000-8000-000000000001");
        var transitionId = new Guid("30000000-0000-4000-8000-000000000001");
        var baseline = new AnimationLocalPoseAuthor(
            new Vector3(1.0f, 2.0f, 3.0f),
            new Quaternion(0.0f, 0.0f, 0.5f, 0.5f),
            new Vector3(1.0f, -2.0f, 3.0f));
        var target = new AnimationTargetBindingAuthor(
            domain: 0,
            Guid.NewGuid(),
            targetId,
            baseline,
            new SpatialPosition3(new ChunkIndex3(2, -3, 4), new Vector3(0.25f, 0.5f, 0.75f)));
        var clip = new AnimationActiveClipAuthor(
            slotId,
            playSequence: 4,
            localTicks: 7,
            previousTicks: 5,
            new AnimationTickRemainder192(0x0123456789ABCDEF, 0xFEDCBA9876543210, 0x8000000000000001),
            loopOrdinal: -3,
            previousLoopOrdinal: -2,
            weight: 0.75f,
            forwardCursor: 6,
            reverseCursor: 5,
            startDelivered: true,
            completed: false,
            lastDirection: -1);
        var transitionedLayer = new AnimationLayerStateAuthor(
            0,
            0.5f,
            [clip],
            new AnimationTransitionStateAuthor(
                transitionId,
                elapsedTicks: 9,
                fraction: 17,
                [0.25f, 0.75f],
                slotId));
        var player = new AnimationPlayerStateAuthor(
            actorId,
            Guid.NewGuid(),
            new WorldAssetReference(37, 1),
            speed: BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
            nextPlaySequence: 5,
            lastSampleTick: 12,
            [target],
            [transitionedLayer],
            targetId);
        var author = new AnimationRuntimeAuthor(7, [player]);
        provider.Binding.ValidateAuthor(in author);
        var encoded = AnimationRuntimeStateCodec.Encode(author);
        var readBinding = new WorldAttachmentReadBinding<AnimationRuntimeAuthor>(
            provider.Binding.Descriptor.Schema,
            encoded.Length);

        try
        {
            var result = readBinding.ValidateComplete(provider.Read(encoded, readBinding, CancellationToken.None));

            if (!result.TryGetAuthorValue(out var decoded))
            {
                throw new InvalidOperationException(
                    $"The Animation codec rejected its own encoded playback state: {result.Diagnostics[0].Code}: {result.Diagnostics[0].Message}");
            }

            provider.Binding.ValidateAuthor(in decoded);
            var decodedPlayer = decoded.Players.Single();

            Assert.Multiple(() => {
                Assert.That(decodedPlayer.Controller, Is.EqualTo(new WorldAssetReference(37, 1)));
                Assert.That(BitConverter.SingleToInt32Bits(decodedPlayer.Speed), Is.EqualTo(unchecked((int)0x80000000)));
                Assert.That(decodedPlayer.Bindings.Single().EntityId, Is.EqualTo(targetId));
                Assert.That(decodedPlayer.Bindings.Single().RootBaseline, Is.EqualTo(target.RootBaseline));
                Assert.That(decodedPlayer.Layers.Single().Clips.Single().Fraction, Is.EqualTo(clip.Fraction));
                Assert.That(decodedPlayer.Layers.Single().Transition?.SourceWeights, Is.EqualTo(ImmutableArray.Create(0.25f, 0.75f)));
                Assert.That(AnimationRuntimeStateCodec.Encode(decoded), Is.EqualTo(encoded));
            });
        }
        finally
        {
            readBinding.Revoke();
        }
    }

    [Test]
    public void PlaybackStateRecordArraysUseTheSuppliedLargeRecordLimit()
    {
        var players = ImmutableArray.CreateBuilder<AnimationPlayerStateAuthor>(160);

        for (var index = 0; index < players.Capacity; index++)
        {
            var actorId = new Guid(index + 1, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 1);
            players.Add(new AnimationPlayerStateAuthor(
                actorId,
                Guid.NewGuid(),
                new WorldAssetReference(37, 1),
                speed: 1.0f,
                nextPlaySequence: 1,
                lastSampleTick: null,
                [],
                [new AnimationLayerStateAuthor(0, 1.0f, [], transition: null)],
                motionActorId: null));
        }

        var author = new AnimationRuntimeAuthor(1, players.MoveToImmutable());
        var limits = new EbinReadLimits(
            16 * 1024 * 1024,
            EbinReadLimits.Foundation.MaximumFieldsPerRecord,
            EbinReadLimits.Foundation.MaximumDepth,
            EbinReadLimits.Foundation.MaximumArrayElements,
            EbinReadLimits.Foundation.MaximumUtf8Bytes,
            EbinReadLimits.Foundation.MaximumAggregateRecords,
            EbinReadLimits.Foundation.MaximumAggregateFields,
            EbinReadLimits.Foundation.MaximumAggregateArrayElements);
        var encoded = AnimationRuntimeStateCodec.Encode(author, limits);
        var provider = new AnimationRuntimeStateProvider(CreateRegistry());
        var readBinding = new WorldAttachmentReadBinding<AnimationRuntimeAuthor>(
            provider.Binding.Descriptor.Schema,
            encoded.Length,
            limits);

        try
        {
            var result = readBinding.ValidateComplete(provider.Read(encoded, readBinding, CancellationToken.None));

            if (!result.TryGetAuthorValue(out var decoded))
            {
                throw new InvalidOperationException("The Animation codec rejected its large encoded playback state.");
            }

            Assert.Multiple(() => {
                Assert.That(encoded.Length, Is.GreaterThan(EbinReadLimits.Foundation.MaximumRecordBytes));
                Assert.That(decoded.Players, Has.Length.EqualTo(160));
                Assert.That(AnimationRuntimeStateCodec.Encode(decoded, limits), Is.EqualTo(encoded));
            });
        }
        finally
        {
            readBinding.Revoke();
        }
    }

    [Test]
    public void PlayerComponentRegistrationRequiresAnExplicitControllerAsset()
    {
        var schemas = CreateRegistry();
        var builder = AnimationSupport.RegisterPlayerComponent(new WorldRegistrationBuilder(schemas), schemas);
        using var world = World.Create(new WorldOptions(builder.Build()) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var playerComponent = new AnimationComponentDefinitions.AnimationPlayerComponent(37);
        var entity = world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Value(in playerComponent)]));

        Assert.Multiple(() => {
            Assert.That(world.TryGet<AnimationComponentDefinitions.AnimationPlayerComponent>(entity, out var actual), Is.True);
            Assert.That(actual.ControllerAssetId, Is.EqualTo(37));
            Assert.That(world.Has<AnimationComponentDefinitions.AnimationPlayerComponent>(entity), Is.True);
        });

        _ = Assert.Throws<InvalidOperationException>(() => world.CreateEntity(new EntityDescription(
            components: [ComponentInitialization.Defaults<AnimationComponentDefinitions.AnimationPlayerComponent>()])));
    }

    [Test]
    public void PlayerComponentStateCodecPreservesControllerAssetIdAndMajorOnePolicy()
    {
        var schemas = CreateRegistry();
        var registration = AnimationSupport.RegisterPlayerComponent(new WorldRegistrationBuilder(schemas), schemas).Build();
        var componentRegistration = registration.TryGetComponentType<AnimationComponentDefinitions.AnimationPlayerComponent>(out var component)
            ? component ?? throw new InvalidOperationException("The Animation Player registration was null.")
            : throw new InvalidOperationException("The Animation Player component was not registered.");
        var playerComponent = new AnimationComponentDefinitions.AnimationPlayerComponent(37);
        var column = componentRegistration.CreateColumn(1);
        var initialization = ComponentInitialization.Value(in playerComponent);
        componentRegistration.InitializeColumn(column, 0, initialization, new SpatialSettings(1, 12));
        var captureContext = new AnimationPlayerCaptureContext();
        var byteCount = componentRegistration.CountStateRecord(column, 0, captureContext, CancellationToken.None);
        var encoded = new byte[checked((int)byteCount)];
        var bytesWritten = componentRegistration.WriteStateRecord(
            column,
            0,
            encoded,
            captureContext,
            CancellationToken.None);
        var readContext = new AnimationPlayerRestoreContext();
        var read = componentRegistration.RestoreFromRecord(encoded, readContext, CancellationToken.None);

        Assert.That(read.Status, Is.EqualTo(ComponentReadStatus.Read));
        Assert.That(read.TryGetInitialization(out var restoredInitialization), Is.True);
        var restoredValue = restoredInitialization
            ?? throw new InvalidOperationException("The Animation Player state read omitted its initialization.");

        Assert.Multiple(() => {
            Assert.That(bytesWritten, Is.EqualTo(byteCount));
            Assert.That(restoredValue.TryGetValue<AnimationComponentDefinitions.AnimationPlayerComponent>(out var restored), Is.True);
            Assert.That(restored.ControllerAssetId, Is.EqualTo(37));
            Assert.That(readContext.Failure, Is.Null);
        });
    }

    [Test]
    public void PlayerComponentStateCodecRejectsControllerMajorTwo()
    {
        var schemas = CreateRegistry();
        var registration = AnimationSupport.RegisterPlayerComponent(new WorldRegistrationBuilder(schemas), schemas).Build();
        var componentRegistration = registration.TryGetComponentType<AnimationComponentDefinitions.AnimationPlayerComponent>(out var component)
            ? component ?? throw new InvalidOperationException("The Animation Player registration was null.")
            : throw new InvalidOperationException("The Animation Player component was not registered.");
        var assetReference = new EbinRecord(
            new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId,
            1,
            0,
            [
                EbinPrimitives.UInt32(1, 37),
                EbinPrimitives.Guid(2, new ContentSchemaIdentityAssignment("native/animation-controller").SchemaId),
                EbinPrimitives.UInt32(3, 2),
            ]);
        var record = new EbinRecord(
            new ContentSchemaIdentityAssignment("animation/player").SchemaId,
            1,
            0,
            [EbinPrimitives.Record(1, assetReference)]);
        Assert.That(EbinCodec.Prepare(record, out var encoded, out _), Is.EqualTo(EbinStatus.Complete));
        var result = componentRegistration.RestoreFromRecord(
            encoded,
            new AnimationPlayerRestoreContext(),
            CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(ComponentReadStatus.Rejected));
            Assert.That(result.Diagnostics, Is.Not.Empty);
        });
    }

    [Test]
    public void WorldEcsCaptureWritesLargePlaybackAttachmentWithSnapshotLimits()
    {
        const int playerCount = 160;
        var schemas = CreateRegistry();
        var provider = new AnimationRuntimeStateProvider(schemas);
        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterAttachment(AttachmentScope.World, provider.Binding);
        var registration = builder.Build();
        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            MaximumSnapshotBytes = 16 * 1024 * 1024,
            WorldGuid = Guid.NewGuid(),
        });
        var players = ImmutableArray.CreateBuilder<AnimationPlayerStateAuthor>(playerCount);

        for (var index = 0; index < playerCount; index++)
        {
            var actorId = new Guid(index + 1, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 1);
            _ = world.CreateEntity(new EntityDescription(persistentId: actorId));
            players.Add(new AnimationPlayerStateAuthor(
                actorId,
                Guid.NewGuid(),
                new WorldAssetReference(37, 1),
                speed: 1.0f,
                nextPlaySequence: 1,
                lastSampleTick: null,
                [],
                [new AnimationLayerStateAuthor(0, 1.0f, [], transition: null)],
                motionActorId: null));
        }

        var runtime = new AnimationRuntimeState(new AnimationRuntimeAuthor(1, players.MoveToImmutable()));
        world.SetAttachment(runtime, AttachmentOwnership.Transferred);
        _ = world.Step();
        using var lease = world.AcquirePersistenceCaptureLease();
        var ecsRecord = WorldPersistenceEcsTestHelper.CopyEcsRecord(world, in lease);

        Assert.That(ecsRecord.Length, Is.GreaterThan(EbinReadLimits.Foundation.MaximumRecordBytes));
    }

    private sealed class AnimationPlayerCaptureContext : IComponentCaptureContext
    {
        public bool IsActive => true;

        public int OwnerThreadId => Environment.CurrentManagedThreadId;

        public Guid GetEntityId(ComponentFieldDescriptor referenceField, Entity entity)
            => throw new InvalidOperationException("Animation Player has no Entity references.");
    }

    private sealed class AnimationPlayerRestoreContext : IComponentRestoreContext
    {
        public Exception? Failure { get; private set; }

        public bool IsActive => true;

        public int OwnerThreadId { get; } = Environment.CurrentManagedThreadId;

        public TDependency ReadSameEntity<TDependency>()
            where TDependency : unmanaged
            => throw new InvalidOperationException("Animation Player has no Derived dependencies.");

        public void RecordFailure(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            Failure ??= exception;
        }

        public Entity ResolveEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId)
            => throw new InvalidOperationException("Animation Player has no Entity references.");

        public Entity ResolveEntity(WorldSystemFieldDescriptor referenceField, Guid targetEntityId)
            => throw new InvalidOperationException("Animation Player has no Entity references.");

        public Entity? ResolveOptionalEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId)
            => throw new InvalidOperationException("Animation Player has no optional Entity references.");

        public void ThrowIfFailed()
        {
            if (Failure is { } failure)
            {
                throw failure;
            }
        }
    }
}
