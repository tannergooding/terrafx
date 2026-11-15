// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

internal sealed class WorldDefaultDomainOwnershipTests
{
    [Test]
    public void HandoffTransfersCloseAuthorityOnlyToTheExactSuccessor()
    {
        var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var registry = WorldGuidRegistry.GetSelected();
        WorldGuidGrant currentGrant = default;
        WorldGuidGrant replacementGrant = default;
        WorldDefaultDomainOwner? currentOwner = null;
        WorldDefaultDomainOwner? replacementOwner = null;
        WorldDefaultDomainHandoff? handoff = null;
        WorldDomainOperationCredit? handoffCredit = null;
        WorldDomainReservation? reservation = null;

        try
        {
            Assert.That(registry.TryReserveFresh(Guid.NewGuid(), out currentGrant, out _), Is.True);
            Assert.That(registry.TryCommitFresh(currentGrant), Is.True);
            Assert.That(
                registry.TryReserveReplacement(
                    currentGrant,
                    Guid.NewGuid(),
                    out replacementGrant,
                    out _),
                Is.True);
            Assert.That(
                domain.TryReserveOperationCredit(WorldDefaultDomainHandoff.HandoffBytes, out handoffCredit),
                Is.True);
            currentOwner = new WorldDefaultDomainOwner(domain, currentGrant);
            handoff = new WorldDefaultDomainHandoff(
                currentOwner,
                currentGrant,
                replacementGrant,
                handoffCredit
                    ?? throw new InvalidOperationException("The default-domain handoff credit was not reserved."));
            handoffCredit = null;

            Assert.That(
                handoff.IsBoundTo(currentOwner, domain, currentGrant, replacementGrant),
                Is.True);
            Assert.That(
                handoff.IsBoundTo(currentOwner, domain, replacementGrant, currentGrant),
                Is.False);
            Assert.That(registry.TryCommitReplacement(currentGrant, replacementGrant), Is.True);
            replacementOwner = handoff.Consume(currentOwner, domain, currentGrant, replacementGrant);
            Assert.That(
                handoff.IsBoundTo(currentOwner, domain, currentGrant, replacementGrant),
                Is.False);
            currentOwner.Close();
            Assert.That(domain.TryReserveWorld(4096, out reservation), Is.True);
            reservation?.Dispose();
            reservation = null;

            replacementOwner.Close();
            Assert.That(domain.TryReserveWorld(4096, out var rejectedReservation), Is.False);
            rejectedReservation?.Dispose();
            handoff.Dispose();
            handoff = null;
            Assert.That(domain.Retirement.IsCompleted, Is.True);
        }
        finally
        {
            reservation?.Dispose();
            handoff?.Dispose();
            handoffCredit?.Dispose();
            currentOwner?.Close();
            replacementOwner?.Close();
            domain.Dispose();
            _ = registry.TryReleaseFresh(replacementGrant);
            _ = registry.TryReleaseFresh(currentGrant);
        }
    }

    [Test]
    public void LifetimeRevokesBeforeRetiringItsExactWorldGuidBinding()
    {
        var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        var registry = WorldGuidRegistry.GetSelected();
        WorldGuidGrant grant = default;
        WorldGuidGrant retryGrant = default;
        WorldDefaultDomainOwner? owner = null;
        WorldLifetimeControl? lifetime = null;

        try
        {
            Assert.That(registry.TryReserveFresh(Guid.NewGuid(), out grant, out _), Is.True);
            Assert.That(registry.TryCommitFresh(grant), Is.True);
            owner = new WorldDefaultDomainOwner(domain, grant);
            lifetime = new WorldLifetimeControl(
                registry,
                domain,
                grant,
                owner,
                isReplacementCandidate: false);
            lifetime.PublishFresh();
            Assert.That(registry.IsActive(grant), Is.True);
            Assert.That(lifetime.BeginRetirement(), Is.True);
            Assert.That(registry.IsActive(grant), Is.False);
            Assert.That(registry.TryReserveFresh(grant.WorldGuid, out _, out _), Is.False);
            Assert.That(lifetime.CompleteRetirement(), Is.True);
            Assert.That(registry.TryReserveFresh(grant.WorldGuid, out retryGrant, out _), Is.True);
            Assert.That(registry.TryReleaseFresh(retryGrant), Is.True);
            lifetime.CloseDefaultDomain();
            Assert.That(domain.TryReserveWorld(4096, out var reservation), Is.False);
            reservation?.Dispose();
            Assert.That(domain.Retirement.IsCompleted, Is.True);
        }
        finally
        {
            if (lifetime is not null)
            {
                if (lifetime.BeginRetirement() && lifetime.CompleteRetirement())
                {
                    lifetime.CloseDefaultDomain();
                }
            }
            else
            {
                owner?.Close();
            }

            domain.Dispose();
            _ = registry.TryReleaseFresh(retryGrant);
            _ = registry.TryReleaseFresh(grant);
        }
    }

    [Test]
    public void WorldBindingRemainsReservedUntilReleaseCompletes()
    {
        var schemas = new CompiledContentSchemaRegistry([]);
        var probe = new WorldGuidRetirementProbe(Guid.NewGuid());
        var description = new SystemDescription(
            Guid.NewGuid(),
            0,
            SimulationStage.Input,
            [],
            [],
            [],
            SystemThreadMode.OwnerThread);
        var binding = WorldSystemFactoryBinding.Stateless<
            WorldGuidRetirementSystem,
            WorldGuidRetirementSystemCreate,
            WorldGuidRetirementSystemRelease>(
            Guid.NewGuid(),
            1,
            new WorldSystemSourceStateDeclaration(typeof(WorldGuidRetirementSystem), []),
            default,
            new WorldGuidRetirementSystemRelease(probe));
        var schedule = SystemExecutionSchedule.Compile([description], WorldSchedulingLimits.Default);
        var registration = new WorldRegistration(
            schemas,
            [],
            [],
            systems: [new SystemRegistration(description, binding)],
            schedule: schedule);
        probe.Registration = registration;
        World? world = null;
        World? replacement = null;

        try
        {
            world = World.Create(new WorldOptions(registration) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = probe.WorldGuid,
            });
            world.Dispose();

            Assert.That(probe.WasRefusedDuringRelease, Is.True);
            Assert.That(probe.WorldCreatedDuringRelease, Is.Null);

            replacement = World.Create(new WorldOptions(registration) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = probe.WorldGuid,
            });
        }
        finally
        {
            replacement?.Dispose();
            probe.WorldCreatedDuringRelease?.Dispose();
            world?.Dispose();
        }
    }

    private sealed class WorldGuidRetirementProbe
    {
        private bool _hasAttemptedRecreation;
        private WorldRegistration? _registration;

        internal WorldGuidRetirementProbe(Guid worldGuid)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
            WorldGuid = worldGuid;
        }

        internal WorldRegistration? Registration
        {
            get
            {
                return _registration;
            }

            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _registration = value;
            }
        }

        internal bool WasRefusedDuringRelease { get; private set; }

        internal World? WorldCreatedDuringRelease { get; private set; }

        internal Guid WorldGuid { get; }

        internal void AttemptWorldCreationDuringRelease()
        {
            if (_hasAttemptedRecreation)
            {
                return;
            }

            _hasAttemptedRecreation = true;

            try
            {
                WorldCreatedDuringRelease = World.Create(new WorldOptions(
                    _registration
                        ?? throw new InvalidOperationException("The retirement probe has no registration.")) {
                    CpuWorkerCount = 0,
                    InitialReplayLineageId = Guid.NewGuid(),
                    WorldGuid = WorldGuid,
                });
            }
            catch (ArgumentException)
            {
                WasRefusedDuringRelease = true;
            }
        }
    }

    private sealed class WorldGuidRetirementSystem : IWorldSystem
    {
        public void Execute(WorldSystemContext context)
        {
        }
    }

    private readonly struct WorldGuidRetirementSystemCreate : IWorldSystemCreate<WorldGuidRetirementSystem>
    {
        public WorldGuidRetirementSystem Invoke() => new();
    }

    private readonly struct WorldGuidRetirementSystemRelease : IWorldSystemRelease<WorldGuidRetirementSystem>
    {
        private readonly WorldGuidRetirementProbe _probe;

        internal WorldGuidRetirementSystemRelease(WorldGuidRetirementProbe probe)
        {
            _probe = probe;
        }

        public void Invoke(WorldGuidRetirementSystem system)
            => _probe.AttemptWorldCreationDuringRelease();
    }
}
