// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceCaptureAdmissionTests
{
    [Test]
    public void CaptureLeaseReservationOutlivesCaptureAndReleasesIndependently()
    {
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            CpuWorkerCount = 0,
            MaximumPersistenceAdmissionBytes = 8192,
        });
        _ = world.Step();

        WorldPersistenceAdmissionReservation? reservation;

        using (var lease = world.AcquirePersistenceCaptureLease())
        {
            Assert.That(lease.TryReserveAdmissionBytes(8192, out reservation), Is.True);
            Assert.That(reservation, Is.Not.Null);
        }

        Assert.That(world.TryReservePersistenceAdmissionBytes(1, out var refused), Is.False);
        Assert.That(refused, Is.Null);
        refused?.Dispose();
        _ = world.Step();

        reservation!.Dispose();
        Assert.That(world.TryReservePersistenceAdmissionBytes(8192, out var replacement), Is.True);
        Assert.That(replacement, Is.Not.Null);
        replacement!.Dispose();
    }
}
