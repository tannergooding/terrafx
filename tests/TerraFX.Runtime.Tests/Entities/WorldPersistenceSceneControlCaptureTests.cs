// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceSceneControlCaptureTests
{
    [Test]
    public void CaptureRequiresAnEmptyQueueAndRejectsEnqueueUntilLeaseRelease()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);
        var sceneSupport = SceneSupport.CreateRegistration();
        var registrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = registrationBuilder.RegisterSceneSupport(sceneSupport);
        var registration = registrationBuilder.Build();
        using var world = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        _ = world.Step();
        var participant = (SceneWorldRegistry)world.GetSceneParticipant(sceneSupport);
        var pendingRequest = new TestSceneOwnerRequest();

        Assert.That(participant.TryEnqueue(pendingRequest), Is.True);
        _ = Assert.Throws<InvalidOperationException>(() => _ = world.AcquirePersistenceCaptureLease());
        Assert.That(pendingRequest.ExecutionCount, Is.Zero);

        world.PumpControlBoundary();
        Assert.That(pendingRequest.ExecutionCount, Is.EqualTo(1));

        var requestDuringCapture = new TestSceneOwnerRequest();

        using (world.AcquirePersistenceCaptureLease())
        {
            Assert.That(participant.TryEnqueue(requestDuringCapture), Is.False);
            Assert.That(requestDuringCapture.WasAdmitted, Is.False);
        }

        Assert.That(participant.TryEnqueue(requestDuringCapture), Is.True);
        world.PumpControlBoundary();
        Assert.That(requestDuringCapture.ExecutionCount, Is.EqualTo(1));
    }

    private sealed class TestSceneOwnerRequest : SceneOwnerRequest
    {
        internal int ExecutionCount { get; private set; }

        internal bool WasAdmitted { get; private set; }

        internal override void Admit()
            => WasAdmitted = true;

        internal override void CancelFromRegistryDisposal()
        {
        }

        internal override void Execute(World world, SceneWorldRegistry registry)
            => ExecutionCount++;
    }
}
