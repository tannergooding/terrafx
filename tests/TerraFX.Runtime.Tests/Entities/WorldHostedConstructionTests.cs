// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldHostedConstructionTests
{
    private static WorldOptions CreateHostedOptions(
        WorldRegistration registration,
        WorldAdmissionDomain admissionDomain)
        => new WorldOptions(registration) {
            AdmissionDomain = admissionDomain,
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        };

    [Test]
    public void HostedWorldsRetainHostOwnedDiagnosticsAndWorkerPool()
    {
        var schemas = new CompiledContentSchemaRegistry([]);
        var registration = new WorldRegistrationBuilder(schemas).Build();
        using var admissionDomain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var workerPool = new WorldWorkerPool(workerCount: 0);
        using var diagnosticsOwner = World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var sharedDiagnostics = diagnosticsOwner.Diagnostics;

        var firstHostedWorld = World.CreateHosted(
            CreateHostedOptions(registration, admissionDomain),
            workerPool,
            sharedDiagnostics,
            admissionDomain);
        _ = firstHostedWorld.Step();
        firstHostedWorld.Dispose();

        var workerCallbackRan = false;
        workerPool.RunBatch([() => workerCallbackRan = true]);
        Assert.That(workerCallbackRan, Is.True);
        Assert.That(sharedDiagnostics.TryGetLatestFault(out var firstFault), Is.False);
        Assert.That(firstFault, Is.Null);

        var secondHostedWorld = World.CreateHosted(
            CreateHostedOptions(registration, admissionDomain),
            workerPool,
            sharedDiagnostics,
            admissionDomain);
        _ = secondHostedWorld.Step();
        secondHostedWorld.Dispose();

        Assert.That(sharedDiagnostics.TryGetLatestFault(out var secondFault), Is.False);
        Assert.That(secondFault, Is.Null);
    }
}
