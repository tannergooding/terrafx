// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Hosting;
using TerraFX.Numerics;
using TerraFX.Runtime.Tests.Scenes;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class WorldPreparedRestoreSourceTests
{
    private static readonly Guid[] s_rootMotionClipSlots = [
        new("70000000-0000-4000-8000-000000000003"),
        new("70000000-0000-4000-8000-000000000001"),
        new("70000000-0000-4000-8000-000000000004"),
        new("70000000-0000-4000-8000-000000000002"),
    ];

    private static EbinRecord AssetRecord(int assetId, string target)
        => Record("native/asset-reference",
            EbinPrimitives.UInt32(1, checked((uint)assetId)),
            EbinPrimitives.Guid(2, new ContentSchemaIdentityAssignment(target).SchemaId),
            EbinPrimitives.UInt32(3, 1));

    private static byte[] CreateRootMotionPackage(string kind)
    {
        var rootJoint = new Guid("70000000-0000-4000-8000-000000000010");
        var transform = Record("scene/local-transform",
            EbinPrimitives.Geometry(1, Vector3.Zero),
            EbinPrimitives.Geometry(2, Quaternion.Identity),
            EbinPrimitives.Geometry(3, Vector3.One));
        var skeleton = Record("native/skeleton",
            EbinPrimitives.RecordArray(1, [
                Record("native/joint",
                    EbinPrimitives.Guid(1, rootJoint),
                    EbinPrimitives.Int32(2, -1),
                    EbinPrimitives.Record(3, transform),
                    EbinPrimitives.Geometry(4, Matrix4x4.Identity)),
            ]),
            Optional(EbinPrimitives.Guid(2, rootJoint)));
        var slots = s_rootMotionClipSlots.Select((id, index) => Record(
            "native/animation-clip-slot",
            EbinPrimitives.Guid(1, id),
            EbinPrimitives.Record(2, AssetRecord(101 + index, "native/clip")))).ToArray();
        Span<byte> mask = stackalloc byte[64];
        Assert.That(EbinPrimitives.WriteFixedArray(
            10, 1, BitConverter.GetBytes(1.0f), mask, out var maskBytes, out _),
            Is.EqualTo(EbinStatus.Complete));
        var layer = Record("native/animation-layer",
            EbinPrimitives.UInt8(1, 0),
            EbinPrimitives.UInt8(2, 0),
            EbinPrimitives.Binary32(3, 1.0f),
            new EbinField(4, 23, 1, 1, mask[..maskBytes]),
            EbinPrimitives.RecordArray(5, []),
            EbinPrimitives.RecordArray(6, [
                Record("native/animation-clip-weight",
                    EbinPrimitives.Guid(1, s_rootMotionClipSlots[0]),
                    EbinPrimitives.Binary32(2, 1.0f)),
            ]),
            EbinPrimitives.UInt8(7, 0));
        var ignore = kind == "ignore-empty";
        var controllerFields = new System.Collections.Generic.List<EbinField> {
            Optional(EbinPrimitives.Record(1, AssetRecord(20, "native/skeleton"))),
            EbinPrimitives.RecordArray(3, slots),
            EbinPrimitives.RecordArray(4, [layer]),
            EbinPrimitives.RecordArray(5, []),
            EbinPrimitives.UInt8(6, ignore ? (byte)0 : (byte)1),
        };

        if (!ignore)
        {
            controllerFields.Add(Optional(EbinPrimitives.Guid(7, new Guid("70000000-0000-4000-8000-000000000020"))));
        }

        var entries = new System.Collections.Generic.List<SceneAssetStoreTests.Package.Entry> {
            new(7, SceneAssetStoreTests.Package.CreateEnvelope(
                7, EbinPrimitives.Encode(Record("native/animation-controller", [.. controllerFields])))),
            new(20, SceneAssetStoreTests.Package.CreateEnvelope(20, EbinPrimitives.Encode(skeleton))),
        };

        for (var index = 0; index < 4; index++)
        {
            var fields = new System.Collections.Generic.List<EbinField> {
                EbinPrimitives.Int64(1, 100),
                EbinPrimitives.RecordArray(2, []),
                EbinPrimitives.RecordArray(3, []),
                EbinPrimitives.UInt8(4, 1),
            };

            if ((kind is not ("nonextractable-clip" or "ignore-empty")) || (index != 0))
            {
                fields.Add(Optional(EbinPrimitives.Record(5, AssetRecord(20, "native/skeleton"))));
                fields.Add(Optional(EbinPrimitives.Record(6, Record("native/root-motion",
                    EbinPrimitives.Guid(1, rootJoint), EbinPrimitives.UInt8(2, 0)))));
            }

            fields.Add(EbinPrimitives.UInt8(8, 0));
            fields.Add(EbinPrimitives.Binary32(9, 0.0f));
            var assetId = checked((uint)(101 + index));
            entries.Add(new SceneAssetStoreTests.Package.Entry(
                assetId, SceneAssetStoreTests.Package.CreateEnvelope(
                    assetId, EbinPrimitives.Encode(Record("native/clip", [.. fields])))));
        }

        return SceneAssetStoreTests.Package.CreatePackage([.. entries]);
    }

    private static EbinField Optional(EbinField field) => new(field.Tag, field.Kind, 0, field.Count, field.Payload);

    private static EbinRecord Record(string name, params EbinField[] fields)
        => new(new ContentSchemaIdentityAssignment(name).SchemaId, 1, 0, fields);

    [Test]
    public void AnimationAssetInputRetainsBothActualCachesAndThePackageAcrossPreparationClosure()
    {
        using var fixture = new Fixture(rootMotionCase: "empty");
        fixture.Save();
        using var preparation = fixture.Prepare();
        var state = State(preparation);
        using var input = state.AcquireAnimationAssets();
        var playback = input.PlaybackState.Author;
        var requests = input.RootMotionRequests.Author;
        var package = input.PackageLease;
        var digest = package.PackageSha256;
        var pins = Field<AnimationPreparedRestoreAssets>(state, "_animationAssets");
        var controller = Field<AssetLease<AnimationControllerAsset>?[]>(pins, "_controllers")[0]!;
        preparation.Dispose();
        Assert.Multiple(() => {
            Assert.That(input.PlaybackState.Author, Is.SameAs(playback));
            Assert.That(input.RootMotionRequests.Author, Is.SameAs(requests));
            Assert.That(playback.Players, Is.Empty);
            Assert.That(input.PackageLease, Is.SameAs(package));
            Assert.That(input.PackageLease.PackageSha256, Is.EqualTo(digest));
            Assert.That(controller.Value.AssetId.Value, Is.EqualTo(7));
            Assert.That(AdmissionBytes(fixture.World), Is.GreaterThan(0));
            Assert.That(() => state.AcquireAnimationAssets(), Throws.TypeOf<ObjectDisposedException>());
        });
        input.Dispose();
        Assert.That(() => controller.Value, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => input.PackageLease, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(fixture.LiveAssetLeases, Is.Zero);
        Assert.That(AdmissionBytes(fixture.World), Is.Zero);
    }

    [Test]
    public void CancellationDuringTypedClipDecodeReleasesTheAlreadyPreparedControllerWithoutConstructingACandidate()
    {
        using var fixture = new Fixture(rootMotionCase: "valid");
        fixture.Save();
        using var cancellation = new CancellationTokenSource();
        fixture.Counters.BeforeAssetRead = kind => {
            if (kind == "clip")
            {
                Assert.That(fixture.LiveAssetLeases, Is.GreaterThan(1));
                cancellation.Cancel();
            }
        };
        _ = Assert.Throws<OperationCanceledException>(() => fixture.Prepare(cancellationToken: cancellation.Token));
        Assert.Multiple(() => {
            Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
            Assert.That(fixture.Counters.ControllerReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.ClipReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.ResourceRestores, Is.Zero);
            Assert.That(fixture.LiveAssetLeases, Is.Zero);
            Assert.That(AdmissionBytes(fixture.World), Is.Zero);
        });
    }

    [TestCase("unknown-slot")]
    [TestCase("wrong-clip")]
    [TestCase("reverse-order")]
    [TestCase("second-slot-mismatch")]
    [TestCase("ignore-empty")]
    [TestCase("controller-codec")]
    [TestCase("clip-codec")]
    [TestCase("controller-id")]
    [TestCase("clip-id")]
    [TestCase("skeleton-id")]
    [TestCase("nonextractable-clip")]
    public void HistoricalRequestContentJoinsRejectBeforeCandidateConstruction(string kind)
    {
        using var fixture = new Fixture(rootMotionCase: kind);
        fixture.Save();
        using var preparation = fixture.Prepare(requirePrepared: false);
        Assert.Multiple(() => {
            Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Rejected));
            Assert.That(preparation.Plan, Is.Null);
            Assert.That(preparation.Diagnostics, Is.Not.Empty);
            Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
            Assert.That(fixture.Counters.ResourceReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.AttachmentReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.ControllerReads, Is.EqualTo(kind == "controller-codec" ? 0 : 1));
            Assert.That(fixture.Counters.ClipReads, Is.EqualTo(kind is "clip-id" or "skeleton-id" or "nonextractable-clip" ? 1 : 0));
            Assert.That(fixture.Counters.ResourceRestores, Is.Zero);
            Assert.That(fixture.Counters.AttachmentRestores, Is.Zero);
            Assert.That(fixture.LiveAssetLeases, Is.Zero);
            Assert.That(AdmissionBytes(fixture.World), Is.Zero);
            Assert.That(Field<long>(fixture.World, "_resourceOperationByteReservation"), Is.Zero);
        });
    }

    [Test]
    public void HistoricalRequestPreparationUsesTheRetainedPackageWhenItsTypedControllerReadReplacesTheStorePackage()
    {
        using var fixture = new Fixture(rootMotionCase: "valid");
        fixture.Save();
        fixture.Counters.BeforeAssetRead = kind => {
            if (kind == "controller")
            {
                fixture.ReplaceRootMotionPackage("ignore-empty");
            }
        };
        using var preparation = fixture.Prepare();
        var owner = Field<AnimationPreparedRestoreAssets>(State(preparation), "_animationAssets");
        var controller = Field<AssetLease<AnimationControllerAsset>?[]>(owner, "_controllers")[0]!;
        Assert.That(controller.Value.RootMotionMode, Is.EqualTo(1));
        Assert.That(fixture.Counters.ClipReads, Is.EqualTo(4));
        Assert.That(fixture.Counters.SkeletonReads, Is.EqualTo(4));
        fixture.Counters.BeforeAssetRead = null;
        using var replacementClip = fixture.LoadCurrentClip();
        using var replacementController = fixture.LoadCurrentController();
        Assert.That(replacementClip.Value.RootMotion, Is.Null);
        Assert.That(replacementController.Value.RootMotionMode, Is.Zero);
    }

    [TestCase("valid", 4)]
    [TestCase("empty", 0)]
    public void PreparedHistoricalRequestPinsTransferThroughTheRealRestoredWorldLifetime(string kind, int sourceCount)
    {
        using var fixture = new Fixture(rootMotionCase: kind);
        fixture.Save();
        using var preparation = fixture.Prepare();
        var state = State(preparation);
        var assets = Field<AnimationPreparedRestoreAssets>(state, "_animationAssets");
        var controller = Field<AssetLease<AnimationControllerAsset>?[]>(assets, "_controllers")[0]!;
        var clips = Field<AssetLease<AnimationClipAsset>?[]>(assets, "_clips").Select(static lease => lease!).ToArray();
        Assert.Multiple(() => {
            Assert.That(fixture.Counters.AttachmentAuthor!.Players, Is.Empty);
            Assert.That(fixture.Counters.ResourceAuthor!.Values.Count(static slot => slot.Present), Is.EqualTo(2));
            Assert.That(state.Checkpoint.WorldContents.Ecs.LiveEntityCount, Is.Zero);
            Assert.That(state.Checkpoint.WorldContents.Ecs.LiveSceneInstanceCount, Is.Zero);
            Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
            Assert.That(fixture.Counters.ControllerReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.ClipReads, Is.EqualTo(sourceCount));
            Assert.That(fixture.Counters.SkeletonReads, Is.EqualTo(sourceCount));
            Assert.That(fixture.LiveAssetLeases, Is.EqualTo(2 + (2 * sourceCount)));
            Assert.That(controller.Value.AssetId.Value, Is.EqualTo(7));
            Assert.That(controller.Value.RootMotionMode, Is.EqualTo(1));
        });
        var originalWorld = fixture.World;
        var receipt = fixture.Commit(preparation);
        preparation.Dispose();
        Assert.Multiple(() => {
            Assert.That(fixture.Counters.ResourceRestoreAuthor, Is.SameAs(fixture.Counters.ResourceAuthor));
            Assert.That(fixture.Counters.AttachmentRestoreAuthor, Is.SameAs(fixture.Counters.AttachmentAuthor));
            Assert.That(fixture.Counters.ResourceReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.AttachmentReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.ControllerReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.ClipReads, Is.EqualTo(sourceCount));
            Assert.That(controller.Value.AssetId.Value, Is.EqualTo(7));
            Assert.That(clips.Select(static lease => lease.Value.AssetId.Value), Is.EqualTo(Enumerable.Range(101, sourceCount)));
            Assert.That(AdmissionBytes(originalWorld), Is.GreaterThan(0));
        });
        fixture.World.Dispose();
        Assert.That(() => controller.Value, Throws.TypeOf<ObjectDisposedException>());

        foreach (var clip in clips)
        {
            Assert.That(() => clip.Value, Throws.TypeOf<ObjectDisposedException>());
        }

        Assert.That(fixture.LiveAssetLeases, Is.Zero);
        Assert.That(AdmissionBytes(originalWorld), Is.Zero);
        receipt.IdentityRemap?.Dispose();
    }

    [Test]
    public void QuarantinedRestoredAttachmentRetainsTheRealRequestAssetPinsAndTheirSourceCredits()
    {
        using var fixture = new Fixture(rootMotionCase: "valid");
        fixture.Save();
        using var preparation = fixture.Prepare();
        var state = State(preparation);
        var assets = Field<AnimationPreparedRestoreAssets>(state, "_animationAssets");
        var controller = Field<AssetLease<AnimationControllerAsset>?[]>(assets, "_controllers")[0]!;
        var clip = Field<AssetLease<AnimationClipAsset>?[]>(assets, "_clips")[0]!;
        var originalWorld = fixture.World;
        var receipt = fixture.Commit(preparation);
        preparation.Dispose();
        fixture.Counters.FailRestoredRelease = true;
        Assert.That(fixture.World.Dispose, Throws.Exception);
        Assert.Multiple(() => {
            Assert.That(controller.Value.AssetId.Value, Is.EqualTo(7));
            Assert.That(clip.Value.AssetId.Value, Is.EqualTo(101));
            Assert.That(fixture.LiveAssetLeases, Is.EqualTo(10));
            Assert.That(AdmissionBytes(originalWorld), Is.GreaterThan(0));
            Assert.That(state.Checkpoint.WorldContents.Ecs.ResourceCount, Is.EqualTo(1));
            Assert.That(fixture.Counters.RestoredReleases, Is.EqualTo(1));
        });
        receipt.IdentityRemap?.Dispose();
    }

    private sealed partial class Counters
    {
        internal Action<string>? BeforeAssetRead;
        internal int ClipReads;
        internal int ControllerReads;
        internal string? MisdecodedAsset;
        internal int SkeletonReads;
    }

    private sealed partial class Fixture
    {
        internal int LiveAssetLeases => Field<int>(_assets, "_liveAssetLeases");

        internal AssetLease<AnimationClipAsset> LoadCurrentClip()
            => _assets.LoadAsync<AnimationClipAsset>(new AssetId(101)).GetAwaiter().GetResult();

        internal AssetLease<AnimationControllerAsset> LoadCurrentController()
            => _assets.LoadAsync<AnimationControllerAsset>(new AssetId(7)).GetAwaiter().GetResult();

        internal void ReplaceRootMotionPackage(string kind)
        {
            var package = CreateRootMotionPackage(kind);
            var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.epkg");
            File.WriteAllBytes(path, package);
            _assets.ReplacePackageAsync(new AssetStoreOptions(
                path, SHA256.HashData(package), _assetRegistration)).GetAwaiter().GetResult();
        }

        private void InstallHistoricalRootMotion(string kind)
        {
            var pairs = new (WorldEntityIdentity Player, WorldEntityIdentity Actor)[2];

            for (var index = 0; index < pairs.Length; index++)
            {
                var player = World.CreateEntity(new EntityDescription());
                var actor = World.CreateEntity(new EntityDescription());
                pairs[index] = (World.GetEntityIdentity(player), World.GetEntityIdentity(actor));
                World.DestroyEntity(player, HierarchyDestroyMode.DetachChildrenKeepWorld);
                World.DestroyEntity(actor, HierarchyDestroyMode.DetachChildrenKeepWorld);
            }

            Array.Sort(pairs, static (left, right) => FoundationCatalogue.CompareRfc(left.Player.EntityId, right.Player.EntityId));
            var partitions = (RootMotionRequest[])Field<ImmutableArray<object>>(World, "_resourceStorage")[0];
            var sourceCount = kind is "empty" or "ignore-empty" ? 0 : 4;

            // These are literal persisted-resource fixtures, not a live Animation producer.
            for (var index = 0; index < pairs.Length; index++)
            {
                var sources = new RootMotionSource[sourceCount];

                for (var sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
                {
                    var slot = kind == "reverse-order" ? sourceCount - sourceIndex - 1 : sourceIndex;
                    var clip = kind == "wrong-clip" || ((kind == "second-slot-mismatch") && (index == 1))
                        ? 104 - slot : 101 + slot;
                    var slotId = kind == "unknown-slot" && (sourceIndex == 0) ? Guid.NewGuid() : s_rootMotionClipSlots[slot];
                    sources[sourceIndex] = new RootMotionSource(
                        slotId, clip, sourceIndex + 1, -2 - sourceIndex, 0.25f);
                }

                partitions[index] = new RootMotionRequest(
                    pairs[index].Player, pairs[index].Actor, 0, Vector3.Zero, Quaternion.Identity,
                    RootMotionExtractionPolicy.Extract, sources, controllerAssetId: 7);
            }
        }

        private void RegisterRootMotionAssetCodecs(AssetStoreRegistrationBuilder builder, string kind)
        {
            Counters.MisdecodedAsset = kind;

            if (kind == "controller-codec")
            {
                _ = builder.RegisterCodec(new ReferenceAssetCodec<byte>(new ContentSchemaReference(
                    new ContentSchemaIdentityAssignment("native/animation-controller").SchemaId, 1, 0, ContentSchemaScope.Content)));
            }
            else
            {
                _ = builder.RegisterCodec(new ObservedAssetCodec<AnimationControllerAsset>(
                    new AnimationControllerAssetCodec(), Counters, "controller"));
            }

            if (kind == "clip-codec")
            {
                _ = builder.RegisterCodec(new ReferenceAssetCodec<byte>(new ContentSchemaReference(
                    new ContentSchemaIdentityAssignment("native/clip").SchemaId, 1, 0, ContentSchemaScope.Content)));
            }
            else
            {
                _ = builder.RegisterCodec(new ObservedAssetCodec<AnimationClipAsset>(
                    new AnimationClipAssetCodec(), Counters, "clip"));
            }

            _ = builder.RegisterCodec(new ObservedAssetCodec<SkeletonAsset>(new SkeletonAssetCodec(), Counters, "skeleton"));
        }
    }

    private sealed class ObservedAssetCodec<T>(
        IContextualBinarySchemaCodec<T> inner,
        Counters counters,
        string kind) : IContextualBinarySchemaCodec<T>
    {
        public Guid SchemaId => inner.SchemaId;

        public int SchemaMajor => inner.SchemaMajor;

        public int SchemaMinor => inner.SchemaMinor;

        public BinaryRecordStatus Read(
            scoped ReadOnlySpan<byte> source,
            AssetCodecContext context,
            [MaybeNull] out T value,
            out int bytesConsumed,
            out BinarySchemaDiagnostic? diagnostic)
        {
            Assert.That(counters.Creates, Is.EqualTo(1));

            switch (kind)
            {
                case "controller":
                {
                    counters.ControllerReads++;
                    break;
                }

                case "clip":
                {
                    counters.ClipReads++;
                    break;
                }

                case "skeleton":
                {
                    counters.SkeletonReads++;
                    break;
                }
            }

            counters.BeforeAssetRead?.Invoke(kind);
            var status = inner.Read(source, context, out value, out bytesConsumed, out diagnostic);

            if ((counters.MisdecodedAsset == "controller-id") && value is AnimationControllerAsset controller)
            {
                value = (T)(object)new AnimationControllerAsset(
                    new AssetId(99), controller.SkeletonAssetId, controller.SceneAssetId, controller.ClipSlots,
                    controller.Layers, controller.Transitions, controller.RootMotionMode,
                    controller.MotionActorKey, controller.EncodedRecord);
            }
            else if ((counters.MisdecodedAsset == "clip-id") && value is AnimationClipAsset clip)
            {
                value = (T)(object)new AnimationClipAsset(
                    new AssetId(99), clip.DurationTicks, clip.LoopMode, clip.SkeletonAssetId,
                    clip.SceneAssetId, clip.RootMotion, clip.Bounds, clip.BoundsQualification,
                    clip.BoundsMargin, clip.Tracks, clip.Events, clip.EncodedRecord);
            }
            else if ((counters.MisdecodedAsset == "skeleton-id") && value is SkeletonAsset skeleton)
            {
                value = (T)(object)new SkeletonAsset(
                    new AssetId(99), skeleton.RootMotionJointId, skeleton.Joints, skeleton.EncodedRecord);
            }

            return status;
        }

        public BinaryRecordStatus Write(
            in T value,
            AssetCodecContext context,
            scoped Span<byte> destination,
            out int bytesWritten,
            out BinarySchemaDiagnostic? diagnostic)
            => inner.Write(value, context, destination, out bytesWritten, out diagnostic);
    }
}
