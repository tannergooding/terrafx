// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldAttachmentTests
{
    private static World CreateWorld(WorldAdmissionDomain domain)
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        return World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
    }

    [Test]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The test verifies that caller-owned attachments are not disposed by their World.")]
    public void CallerOwnedAttachmentsAreScopedAndDetachOnRemovalOrEntityDestruction()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = CreateWorld(domain);
        var worldValue = new ProbeAttachment(1);
        var entityValue = new ProbeAttachment(2);
        world.SetAttachment(worldValue);
        var entity = world.CreateEntity(new EntityDescription());
        world.SetAttachment(entity, entityValue);

        Assert.That(world.TryGetAttachment<ProbeAttachment>(out var storedWorldValue), Is.True);
        Assert.That(storedWorldValue, Is.SameAs(worldValue));
        Assert.That(world.TryGetAttachment<ProbeAttachment>(entity, out var storedEntityValue), Is.True);
        Assert.That(storedEntityValue, Is.SameAs(entityValue));

        var replacement = new ProbeAttachment(3);
        world.SetAttachment(replacement);
        Assert.That(world.TryGetAttachment<ProbeAttachment>(out storedWorldValue), Is.True);
        Assert.That(storedWorldValue, Is.SameAs(replacement));
        Assert.That(world.RemoveAttachment<ProbeAttachment>(entity), Is.True);
        Assert.That(world.RemoveAttachment<ProbeAttachment>(entity), Is.False);
        Assert.That(entityValue.DisposeCount, Is.Zero);

        world.SetAttachment(entity, entityValue);
        world.DestroyEntity(entity, HierarchyDestroyMode.DetachChildrenKeepWorld);
        var replacementEntity = world.CreateEntity(new EntityDescription());
        Assert.That(world.TryGetAttachment<ProbeAttachment>(replacementEntity, out _), Is.False);
        Assert.That(world.RemoveAttachment<ProbeAttachment>(replacementEntity), Is.False);
        world.Dispose();
        Assert.That(replacement.DisposeCount, Is.Zero);
        Assert.That(entityValue.DisposeCount, Is.Zero);
        Assert.That(worldValue.DisposeCount, Is.Zero);
        worldValue.Dispose();
        entityValue.Dispose();
        replacement.Dispose();
    }

    [Test]
    public void EntityScopedAttachmentsRejectForeignAndStaleOwners()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var firstWorld = CreateWorld(domain);
        using var secondWorld = CreateWorld(domain);
        var entity = firstWorld.CreateEntity(new EntityDescription());
        var foreignValue = new ProbeAttachment(1);
        var staleValue = new ProbeAttachment(2);

        Assert.That(
            () => secondWorld.SetAttachment(entity, foreignValue),
            Throws.ArgumentException);
        firstWorld.DestroyEntity(entity, HierarchyDestroyMode.DetachChildrenKeepWorld);
        Assert.That(
            () => firstWorld.SetAttachment(entity, staleValue),
            Throws.ArgumentException);
        Assert.That(
            () => firstWorld.TryGetAttachment<ProbeAttachment>(entity, out _),
            Throws.ArgumentException);
        foreignValue.Dispose();
        staleValue.Dispose();
    }

    [Test]
    public void TransferredOwnershipAndInvalidOwnershipNeverInstallCallerOwnedFallbacks()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = CreateWorld(domain);
        var existingValue = new ProbeAttachment(1);
        var transferredValue = new ProbeAttachment(2);
        var invalidOwnershipValue = new ProbeAttachment(3);
        world.SetAttachment(existingValue);

        _ = Assert.Throws<InvalidOperationException>(() => world.SetAttachment(
            transferredValue,
            AttachmentOwnership.Transferred));
        var ownershipException = Assert.Throws<ArgumentOutOfRangeException>(() => world.SetAttachment(
            invalidOwnershipValue,
            (AttachmentOwnership)0));
        Assert.That(ownershipException!.ParamName, Is.EqualTo("ownership"));
        Assert.That(world.TryGetAttachment<ProbeAttachment>(out var storedValue), Is.True);
        Assert.That(storedValue, Is.SameAs(existingValue));

        world.Dispose();
        Assert.That(existingValue.DisposeCount, Is.Zero);
        Assert.That(transferredValue.DisposeCount, Is.Zero);
        Assert.That(invalidOwnershipValue.DisposeCount, Is.Zero);
        existingValue.Dispose();
        transferredValue.Dispose();
        invalidOwnershipValue.Dispose();
    }

    private sealed class ProbeAttachment : IDisposable
    {
        internal ProbeAttachment(int value)
        {
            Value = value;
        }

        internal int DisposeCount { get; private set; }

        internal int Value { get; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
