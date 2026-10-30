// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldControlTests
{
    private static WorldRegistration CreateEmptyRegistration()
        => new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();

    [Test]
    public void BeginIdentityBranchChangesFutureIssuanceAndPreservesEarlierIdentities()
    {
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var rootBranch = world.IdentityBranchId;
        var firstEntity = world.CreateEntity(new EntityDescription());
        var firstIdentity = world.GetEntityIdentity(firstEntity);
        var nextBranch = Guid.NewGuid();

        world.BeginIdentityBranch(nextBranch);

        var secondEntity = world.CreateEntity(new EntityDescription());
        var secondIdentity = world.GetEntityIdentity(secondEntity);

        Assert.That(world.IdentityBranchId, Is.EqualTo(nextBranch));
        Assert.That(firstIdentity.CreationBranchId, Is.EqualTo(rootBranch));
        Assert.That(secondIdentity.CreationBranchId, Is.EqualTo(nextBranch));
        Assert.That(world.TryGetEntity(firstIdentity, out var resolvedFirst), Is.True);
        Assert.That(resolvedFirst, Is.EqualTo(firstEntity));
        Assert.That(world.TryGetEntity(secondIdentity, out var resolvedSecond), Is.True);
        Assert.That(resolvedSecond, Is.EqualTo(secondEntity));
        Assert.That(firstIdentity.EntityId, Is.Not.EqualTo(secondIdentity.EntityId));
    }

    [Test]
    public void BeginIdentityBranchRefusesDuplicateAndCapacityBeforeChangingTheActiveBranch()
    {
        var branchId = Guid.NewGuid();
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            InitialReplayLineageId = Guid.NewGuid(),
            MaximumIdentityBranches = 1,
            WorldGuid = Guid.NewGuid(),
        });
        var initialBranchId = world.IdentityBranchId;

        _ = Assert.Throws<InvalidOperationException>(() => world.BeginIdentityBranch(branchId));
        Assert.That(world.IdentityBranchId, Is.EqualTo(initialBranchId));

        using var branchWorld = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        branchWorld.BeginIdentityBranch(branchId);

        _ = Assert.Throws<ArgumentException>(() => branchWorld.BeginIdentityBranch(branchId));
        Assert.That(branchWorld.IdentityBranchId, Is.EqualTo(branchId));
    }

    [Test]
    public void FrameCandidateInspectionDoesNotConsumeRevisionOrControlSequence()
    {
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var originalFrame2 = world.CurrentFrame2;
        var initialFrame3 = world.CurrentFrame3;
        var firstOrigin = new SpatialPosition3(
            new ChunkIndex3(1, -2, 3),
            Vector3.Zero);
        var signedMinimum = new Vector3(-0.0f, -0.0f, -0.0f);
        var signedMaximum = Vector3.Zero;
        var staleCandidate = world.CreateNextFrame3(firstOrigin, signedMinimum, signedMaximum);
        var appliedCandidate = world.CreateNextFrame3(
            new SpatialPosition3(new ChunkIndex3(4, 5, 6), new Vector3(1, 2, 3)),
            initialFrame3.Minimum,
            initialFrame3.Maximum);

        Assert.That(staleCandidate.Revision, Is.EqualTo(2));
        Assert.That(appliedCandidate.Revision, Is.EqualTo(2));

        var applied = world.SetFrame(appliedCandidate);
        var rejected = world.SetFrame(staleCandidate);

        Assert.That(applied.Status, Is.EqualTo(FrameChangeStatus.Applied));
        Assert.That(applied.Retirement.IsCompletedSuccessfully, Is.True);
        Assert.That(applied.ControlSequence, Is.EqualTo(1));
        Assert.That(applied.AppliedAtNextTick, Is.EqualTo(0));
        Assert.That(applied.CommittedFrame3, Is.EqualTo(appliedCandidate));
        var firstStepReceipt = world.Step();
        Assert.That(firstStepReceipt.CompletedTick, Is.EqualTo(0));
        Assert.That(firstStepReceipt.Frame3, Is.EqualTo(appliedCandidate));
        Assert.That(rejected.Status, Is.EqualTo(FrameChangeStatus.Rejected));
        Assert.That(rejected.Diagnostics, Has.Length.EqualTo(1));
        Assert.That(rejected.Diagnostics[0].Code, Is.EqualTo("WorldFrameRevisionStale"));
        Assert.That(rejected.ControlSequence, Is.Null);
        Assert.That(world.CurrentFrame3.Revision, Is.EqualTo(2));
        Assert.That(world.CurrentFrame2, Is.EqualTo(originalFrame2));

        var nextCandidate = world.CreateNextFrame3(
            firstOrigin,
            signedMinimum,
            signedMaximum);
        var nextApplied = world.SetFrame(nextCandidate);

        Assert.That(nextApplied.ControlSequence, Is.EqualTo(2));
        Assert.That(nextApplied.AppliedAtNextTick, Is.EqualTo(1));
        Assert.That(
            BitConverter.SingleToInt32Bits(world.CurrentFrame3.Minimum.X),
            Is.EqualTo(BitConverter.SingleToInt32Bits(-0.0f)));
        var stepReceipt = world.Step();
        Assert.That(stepReceipt.CompletedTick, Is.EqualTo(1));
        Assert.That(stepReceipt.Frame3, Is.EqualTo(nextCandidate));
        Assert.That(firstStepReceipt.Frame3, Is.EqualTo(appliedCandidate));
    }

    [Test]
    public void FrameCandidatesFromAnotherWorldDoNotGainThisWorldAuthority()
    {
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        using var foreignWorld = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var foreignFrame = foreignWorld.CreateNextFrame3(
            foreignWorld.CurrentFrame3.Origin,
            foreignWorld.CurrentFrame3.Minimum,
            foreignWorld.CurrentFrame3.Maximum);

        var exception = Assert.Throws<ArgumentException>(() => world.SetFrame(foreignFrame));

        Assert.That(exception!.ParamName, Is.EqualTo("frame"));
        Assert.That(world.CurrentFrame3.Revision, Is.EqualTo(1));
    }

    [Test]
    public void FrameControlHistoryCapacityRejectsWithoutChangingTheCurrentFrame()
    {
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            InitialReplayLineageId = Guid.NewGuid(),
            MaximumRecordedControls = 1,
            WorldGuid = Guid.NewGuid(),
        });
        var current = world.CurrentFrame3;
        world.SetTimeScale(1);
        var candidate = world.CreateNextFrame3(current.Origin, current.Minimum, current.Maximum);

        var receipt = world.SetFrame(candidate);

        Assert.That(receipt.Status, Is.EqualTo(FrameChangeStatus.Rejected));
        Assert.That(receipt.Diagnostics[0].Code, Is.EqualTo("WorldFramePreflightCapacityExceeded"));
        Assert.That(receipt.ControlSequence, Is.Null);
        Assert.That(world.CurrentFrame3, Is.EqualTo(current));
        _ = Assert.Throws<InvalidOperationException>(() => world.SetTimeScale(1));
    }

    [Test]
    public void PumpControlBoundaryDoesNotAdvanceTheSimulationClock()
    {
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });

        world.PumpControlBoundary();

        Assert.That(world.Step().CompletedTick, Is.EqualTo(0));
    }

    [Test]
    public void SetTimeScaleRejectsInvalidValuesBeforeMutation()
    {
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            InitialTimeScale = -0.0f,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        Assert.That(
            BitConverter.SingleToInt32Bits(world.InitialTimeScale),
            Is.EqualTo(BitConverter.SingleToInt32Bits(-0.0f)));

        Assert.That(
            Assert.Throws<ArgumentOutOfRangeException>(() => world.SetTimeScale(float.NaN))!.ParamName,
            Is.EqualTo("timeScale"));
        Assert.That(
            Assert.Throws<ArgumentOutOfRangeException>(() => world.SetTimeScale(-0.1f))!.ParamName,
            Is.EqualTo("timeScale"));
        Assert.That(
            Assert.Throws<ArgumentOutOfRangeException>(() => world.SetTimeScale(4.1f))!.ParamName,
            Is.EqualTo("timeScale"));

        world.BeginIdentityBranch(Guid.NewGuid());
        world.SetTimeScale(-0.0f);
        Assert.That(
            BitConverter.SingleToInt32Bits(world.CurrentTimeScale),
            Is.EqualTo(BitConverter.SingleToInt32Bits(-0.0f)));
        world.SetTimeScale(0.0f);
        Assert.That(
            BitConverter.SingleToInt32Bits(world.CurrentTimeScale),
            Is.EqualTo(BitConverter.SingleToInt32Bits(0.0f)));
        var frame = world.CurrentFrame3;
        var receipt = world.SetFrame(world.CreateNextFrame3(frame.Origin, frame.Minimum, frame.Maximum));

        Assert.That(receipt.ControlSequence, Is.EqualTo(4));
        Assert.That(receipt.AppliedAtNextTick, Is.EqualTo(0));
        world.SetTimeScale(1.0f);
        Assert.That(world.CurrentTimeScale, Is.EqualTo(1.0f));
        Assert.That(world.Step().CompletedTick, Is.EqualTo(0));
    }

    [Test]
    public void TwoDimensionalFrameCandidatesRejectWhenAdmissionIsDisabled()
    {
        using var world = World.Create(new WorldOptions(CreateEmptyRegistration()) {
            Enable2DFrame = false,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });

        Assert.That(world.CurrentFrame2, Is.Null);
        _ = Assert.Throws<InvalidOperationException>(
            () => world.CreateNextFrame2(
                new SpatialPosition2(new ChunkIndex2(0, 0), Vector2.Zero),
                Vector2.Zero,
                Vector2.One));
    }
}
