// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldOptionsTests
{
    [Test]
    public void CpuWorkerCountDefaultsToTheDocumentedBoundedPoolSize()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var options = new WorldOptions(registration);
        var expected = Math.Min(64, Math.Max(1, Environment.ProcessorCount - 2));

        Assert.That(options.ValidateAndResolve().CpuWorkerCount, Is.EqualTo(expected));
    }

    [Test]
    public void CpuWorkerCountUsesTheExactRequestedValue()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var inlineOptions = new WorldOptions(registration) { CpuWorkerCount = 0 };
        var explicitOptions = new WorldOptions(registration) { CpuWorkerCount = 4 };

        Assert.That(inlineOptions.ValidateAndResolve().CpuWorkerCount, Is.Zero);
        Assert.That(explicitOptions.ValidateAndResolve().CpuWorkerCount, Is.EqualTo(4));
    }

    [TestCase(4096L)]
    [TestCase(8_589_934_592L)]
    public void MaximumPersistenceAdmissionBytesAcceptsItsInclusiveBounds(long maximumPersistenceAdmissionBytes)
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var options = new WorldOptions(registration) {
            MaximumPersistenceAdmissionBytes = maximumPersistenceAdmissionBytes,
        };

        Assert.That(
            options.ValidateAndResolve().Options.MaximumPersistenceAdmissionBytes,
            Is.EqualTo(maximumPersistenceAdmissionBytes));
    }

    [TestCase(4095L)]
    [TestCase(8_589_934_593L)]
    public void MaximumPersistenceAdmissionBytesRejectsValuesOutsideItsBounds(long maximumPersistenceAdmissionBytes)
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var options = new WorldOptions(registration) {
            MaximumPersistenceAdmissionBytes = maximumPersistenceAdmissionBytes,
        };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.ValidateAndResolve());
        Assert.That(exception!.ParamName, Is.EqualTo(nameof(WorldOptions.MaximumPersistenceAdmissionBytes)));
    }

    [TestCase(64L)]
    [TestCase(2_147_483_584L)]
    public void MaximumSnapshotBytesAcceptsItsInclusiveBounds(long maximumSnapshotBytes)
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var options = new WorldOptions(registration) { MaximumSnapshotBytes = maximumSnapshotBytes };

        Assert.That(options.ValidateAndResolve().Options.MaximumSnapshotBytes, Is.EqualTo(maximumSnapshotBytes));
    }

    [TestCase(63L)]
    [TestCase(2_147_483_585L)]
    public void MaximumSnapshotBytesRejectsValuesOutsideItsBounds(long maximumSnapshotBytes)
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var options = new WorldOptions(registration) { MaximumSnapshotBytes = maximumSnapshotBytes };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => options.ValidateAndResolve());
        Assert.That(exception!.ParamName, Is.EqualTo(nameof(WorldOptions.MaximumSnapshotBytes)));
    }

    [Test]
    public void PersistenceLimitsDefaultToTheDocumentedWorldBounds()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var options = new WorldOptions(registration);

        Assert.That(options.MaximumSnapshotBytes, Is.EqualTo(2_147_483_584L));
        Assert.That(options.MaximumPersistenceAdmissionBytes, Is.EqualTo(8_589_934_592L));
        _ = options.ValidateAndResolve();
    }

    [Test]
    public void WorldFreezesThePersistenceLimitsFromItsOptions()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        using var world = World.Create(new WorldOptions(registration) {
            MaximumSnapshotBytes = 1024,
            MaximumPersistenceAdmissionBytes = 8192,
        });

        Assert.That(world.MaximumSnapshotBytes, Is.EqualTo(1024));
        Assert.That(world.MaximumPersistenceAdmissionBytes, Is.EqualTo(8192));
    }
}
