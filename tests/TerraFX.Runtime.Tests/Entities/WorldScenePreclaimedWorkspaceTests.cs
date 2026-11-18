// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Entities;

internal sealed class WorldScenePreclaimedWorkspaceTests
{
    private static World CreateWorld(WorldAdmissionDomain domain)
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);
        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        return World.Create(new WorldOptions(builder.Build()) {
            AdmissionDomain = domain,
        });
    }

    [Test]
    public void CloseRefusesCoveredWorkspaceInstallationWithoutReturningTheClaim()
    {
        const long dataBytes = 512;
        var bytes = WorldSceneStorageReservation.GetRequiredBytes(dataBytes);
        using var domain = new WorldAdmissionDomain(256 * 1024 * 1024);
        using var world = CreateWorld(domain);
        var use = world.AcquireGuidUse(2048);
        var control = use.Control;
        var parent = control.DomainReservation;
        var parentBytes = parent.Bytes;
        var participant = world.Registration.SceneSupport?.GetParticipant(world)
            ?? throw new InvalidOperationException("The World has no Scene participant.");

        Assert.That(control.TryReserveWorkspace(bytes), Is.True);
        world.Dispose();

        Assert.That(
            world.TryReserveSceneWorkspaceFromPreclaimed(
                participant,
                control,
                dataBytes),
            Is.Null);
        Assert.That(control.Anchor.ActiveWorkspaceBytes, Is.EqualTo(bytes));
        Assert.That(parent.Bytes, Is.EqualTo(parentBytes));
        Assert.That(control.TryReleaseWorkspace(bytes), Is.True);
        Assert.That(control.Anchor.ActiveWorkspaceBytes, Is.Zero);
        Assert.That(control.Anchor.UninstalledWorldWorkspaceBytes, Is.Zero);
        Assert.That(control.Anchor.UninstalledActiveWorldWorkspaceBytes, Is.Zero);

        use.Dispose();
        domain.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.True);
    }

    [Test]
    public void CoveredWorkspaceTransfersToStorageWithoutASecondDebit()
    {
        const long dataBytes = 512;
        var bytes = WorldSceneStorageReservation.GetRequiredBytes(dataBytes);
        using var domain = new WorldAdmissionDomain(256 * 1024 * 1024);
        using var world = CreateWorld(domain);
        using var use = world.AcquireGuidUse(2048);
        var control = use.Control;
        var parent = control.DomainReservation;
        var parentBytes = parent.Bytes;
        var independentUseBytes = parent.IndependentUseBytes;
        var workspaceLimit = control.WorkspaceLimit;

        Assert.That(control.TryReserveWorkspace(bytes), Is.True);

        var workspace = world.TryReserveSceneWorkspaceFromPreclaimed(
            world.Registration.SceneSupport?.GetParticipant(world)
                ?? throw new InvalidOperationException("The World has no Scene participant."),
            control,
            dataBytes);

        Assert.That(workspace, Is.Not.Null);
        using (workspace)
        {
            Assert.That(workspace!.IsWorkspace, Is.True);
            Assert.That(parent.Bytes, Is.EqualTo(parentBytes));
            Assert.That(parent.IndependentUseBytes, Is.EqualTo(independentUseBytes));
            Assert.That(control.Anchor.ActiveWorkspaceBytes, Is.EqualTo(bytes));
            Assert.That(control.WorkspaceLimit, Is.EqualTo(workspaceLimit - bytes));

            Assert.That(workspace.TryTransferWorkspaceToStorage(), Is.True);
            Assert.That(workspace.IsStorage, Is.True);
            Assert.That(parent.Bytes, Is.EqualTo(parentBytes + bytes));
            Assert.That(parent.IndependentUseBytes, Is.EqualTo(independentUseBytes - bytes));
            Assert.That(control.Anchor.ActiveWorkspaceBytes, Is.Zero);
            Assert.That(domain.GetSceneReservationBytes(parent), Is.EqualTo((0L, bytes)));
        }

        Assert.That(parent.Bytes, Is.EqualTo(parentBytes));
        Assert.That(parent.IndependentUseBytes, Is.EqualTo(independentUseBytes - bytes));
        Assert.That(domain.GetSceneReservationBytes(parent), Is.EqualTo((0L, 0L)));
    }
}
