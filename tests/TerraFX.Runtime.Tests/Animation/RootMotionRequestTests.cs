// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX.Animation;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Runtime.Tests.Animation;

internal sealed class RootMotionRequestTests
{
    private static readonly Guid s_branchId = new("10000000-0000-4000-8000-000000000003");
    private static readonly Guid s_lineageId = new("10000000-0000-4000-8000-000000000002");
    private static readonly Guid s_worldGuid = new("10000000-0000-4000-8000-000000000001");

    private static WorldEntityIdentity CreateIdentity(string entityId)
        => new(s_worldGuid, s_lineageId, s_branchId, new Guid(entityId));

    [Test]
    public void CopiedRootMotionValuesKeepTheirPackedUnmanagedLayout()
    {
        (string Field, int Offset)[] requestOffsets = [
            ("_present", 0),
            ("_worldGuid", 1),
            ("_playerId", 17),
            ("_playerCreationLineageId", 33),
            ("_playerCreationBranchId", 49),
            ("_actorId", 65),
            ("_actorCreationLineageId", 81),
            ("_actorCreationBranchId", 97),
            ("_tickIndex", 113),
            ("_translationX", 121),
            ("_translationY", 125),
            ("_translationZ", 129),
            ("_rotationX", 133),
            ("_rotationY", 137),
            ("_rotationZ", 141),
            ("_rotationW", 145),
            ("_extractionPolicy", 149),
            ("_sourceCount", 150),
            ("_source0", 151),
            ("_source1", 191),
            ("_source2", 231),
            ("_source3", 271),
            ("_controllerAssetId", 311),
        ];
        (string Field, int Offset)[] sourceOffsets = [
            ("_clipSlotId", 0),
            ("_clipAssetId", 16),
            ("_playSequence", 20),
            ("_loopOrdinal", 28),
            ("_weight", 36),
        ];

        Assert.Multiple(() => {
            Assert.That(Unsafe.SizeOf<RootMotionSource>(), Is.EqualTo(40));
            Assert.That(Unsafe.SizeOf<RootMotionRequest>(), Is.EqualTo(315));
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<RootMotionSource>(), Is.False);
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<RootMotionRequest>(), Is.False);
            Assert.That(AnimationResources.RootMotionRequestsId, Is.Not.EqualTo(Guid.Empty));

            foreach (var (field, offset) in requestOffsets)
            {
                Assert.That(Marshal.OffsetOf<RootMotionRequest>(field).ToInt32(), Is.EqualTo(offset), field);
            }

            foreach (var (field, offset) in sourceOffsets)
            {
                Assert.That(Marshal.OffsetOf<RootMotionSource>(field).ToInt32(), Is.EqualTo(offset), field);
            }
        });
    }

    [Test]
    public void DefaultValuesAreAbsentAndRejectBodyReads()
    {
        var source = default(RootMotionSource);
        var request = default(RootMotionRequest);

        Assert.Multiple(() => {
            Assert.That(request.IsValid, Is.False);
            _ = Assert.Throws<InvalidOperationException>(() => _ = source.Clip);
            _ = Assert.Throws<InvalidOperationException>(() => _ = source.ClipSlotId);
            _ = Assert.Throws<InvalidOperationException>(() => _ = source.PlaySequence);
            _ = Assert.Throws<InvalidOperationException>(() => _ = source.LoopOrdinal);
            _ = Assert.Throws<InvalidOperationException>(() => _ = source.Weight);
            _ = Assert.Throws<InvalidOperationException>(() => _ = request.Player);
            _ = Assert.Throws<InvalidOperationException>(() => _ = request.Actor);
            _ = Assert.Throws<InvalidOperationException>(() => _ = request.TickIndex);
            _ = Assert.Throws<InvalidOperationException>(() => _ = request.Translation);
            _ = Assert.Throws<InvalidOperationException>(() => _ = request.Rotation);
            _ = Assert.Throws<InvalidOperationException>(() => _ = request.SourceCount);
            _ = Assert.Throws<InvalidOperationException>(() => _ = request.GetSource(0));
        });
    }

    [Test]
    public void IdentityRequestsRequirePositiveZeroAndIdentityRotation()
    {
        var player = CreateIdentity("30000000-0000-4000-8000-000000000001");
        var actor = CreateIdentity("30000000-0000-4000-8000-000000000002");

        var request = new RootMotionRequest(
            player,
            actor,
            tickIndex: 0,
            Vector3.Zero,
            Quaternion.Identity,
            RootMotionExtractionPolicy.Extract,
            [],
            controllerAssetId: 1);

        Assert.Multiple(() => {
            Assert.That(request.IsValid, Is.True);
            Assert.That(request.SourceCount, Is.Zero);
            Assert.That(request.Translation, Is.EqualTo(Vector3.Zero));
            Assert.That(request.Rotation, Is.EqualTo(Quaternion.Identity));
            _ = Assert.Throws<ArgumentException>(() => new RootMotionRequest(
                player,
                actor,
                tickIndex: 1,
                new Vector3(-0.0f, 0.0f, 0.0f),
                Quaternion.Identity,
                RootMotionExtractionPolicy.Extract,
                [],
                controllerAssetId: 1));
            _ = Assert.Throws<ArgumentException>(() => new RootMotionRequest(
                player,
                actor,
                tickIndex: 1,
                Vector3.UnitX,
                Quaternion.Identity,
                RootMotionExtractionPolicy.Extract,
                [],
                controllerAssetId: 1));
        });
    }

    [Test]
    public void IssuedRequestPreservesQualifiedIdentitiesBitsAndSourceOrder()
    {
        var player = CreateIdentity("20000000-0000-4000-8000-000000000001");
        var actor = CreateIdentity("20000000-0000-4000-8000-000000000002");
        var firstSlotId = new Guid("20000000-0000-4000-8000-000000000003");
        var secondSlotId = new Guid("20000000-0000-4000-8000-000000000004");
        RootMotionSource[] sources = [
            new(firstSlotId, clipAssetId: 7, playSequence: 11, loopOrdinal: -2, weight: 0.25f),
            new(secondSlotId, clipAssetId: 8, playSequence: 12, loopOrdinal: 3, weight: 0.75f),
        ];
        var translation = new Vector3(-0.0f, 0.5f, -0.25f);
        var rotation = Quaternion.Identity;
        var request = new RootMotionRequest(
            player,
            actor,
            tickIndex: 21,
            translation,
            rotation,
            RootMotionExtractionPolicy.Extract,
            sources,
            controllerAssetId: 9);

        Assert.Multiple(() => {
            Assert.That(request.IsValid, Is.True);
            Assert.That(request.Player, Is.EqualTo(player));
            Assert.That(request.Actor, Is.EqualTo(actor));
            Assert.That(request.TickIndex, Is.EqualTo(21));
            Assert.That(request.ExtractionPolicy, Is.EqualTo(RootMotionExtractionPolicy.Extract));
            Assert.That(request.SourceCount, Is.EqualTo(2));
            Assert.That(BitConverter.SingleToInt32Bits(request.Translation.X), Is.EqualTo(BitConverter.SingleToInt32Bits(-0.0f)));
            Assert.That(request.GetSource(0).ClipSlotId, Is.EqualTo(firstSlotId));
            Assert.That(request.GetSource(0).Clip.Value, Is.EqualTo(7));
            Assert.That(request.GetSource(0).PlaySequence, Is.EqualTo(11));
            Assert.That(request.GetSource(0).LoopOrdinal, Is.EqualTo(-2));
            Assert.That(request.GetSource(0).Weight, Is.EqualTo(0.25f));
            Assert.That(request.GetSource(1).ClipSlotId, Is.EqualTo(secondSlotId));
            Assert.That(request.GetSource(1).Clip.Value, Is.EqualTo(8));
            Assert.That(request.GetSource(1).Weight, Is.EqualTo(0.75f));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => request.GetSource(-1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => request.GetSource(2));
        });
    }

    [Test]
    public void RootMotionInputsRejectInvalidIdentityWeightAndRotation()
    {
        var player = CreateIdentity("40000000-0000-4000-8000-000000000001");
        var actor = CreateIdentity("40000000-0000-4000-8000-000000000002");
        var foreignActor = new WorldEntityIdentity(
            new Guid("40000000-0000-4000-8000-000000000004"),
            s_lineageId,
            s_branchId,
            new Guid("40000000-0000-4000-8000-000000000005"));
        var slotId = new Guid("40000000-0000-4000-8000-000000000003");

        Assert.Multiple(() => {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new RootMotionSource(slotId, 1, 1, 0, 0.0f));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new RootMotionSource(slotId, 1, 1, 0, float.NaN));
            _ = Assert.Throws<ArgumentException>(() => new RootMotionRequest(
                player,
                foreignActor,
                tickIndex: 1,
                Vector3.Zero,
                Quaternion.Identity,
                RootMotionExtractionPolicy.Extract,
                [],
                controllerAssetId: 1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new RootMotionRequest(
                player,
                actor,
                tickIndex: 1,
                Vector3.Zero,
                new Quaternion(0.0f, 0.0f, 0.0f, 2.0f),
                RootMotionExtractionPolicy.Extract,
                [new RootMotionSource(slotId, 1, 1, 0, 1.0f)],
                controllerAssetId: 1));
        });
    }

    [Test]
    public void TransitionRequestRequiresANonemptyIdentifier()
    {
        var transitionId = new Guid("50000000-0000-4000-8000-000000000001");
        var request = new AnimationTransitionRequest(transitionId);

        Assert.Multiple(() => {
            Assert.That(request.TransitionId, Is.EqualTo(transitionId));
            _ = Assert.Throws<ArgumentException>(() => new AnimationTransitionRequest(Guid.Empty));
        });
    }
}
