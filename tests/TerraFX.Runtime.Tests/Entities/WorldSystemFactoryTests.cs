// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldSystemFactoryTests
{
    private static readonly ProbeSystem s_sharedProbeSystem = new();

    [Test]
    public void RegistrationBuildRejectsSystemDependencyCycles()
    {
        var sourceState = new WorldSystemSourceStateDeclaration(typeof(ProbeSystem), []);
        var binding = WorldSystemFactoryBinding.Stateless<ProbeSystem, ProbeSystemCreate, ProbeSystemRelease>(
            Guid.NewGuid(),
            1,
            sourceState,
            default,
            default);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var builder = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([]));
        _ = builder.RegisterSystem(new SystemDescription(
            firstId,
            0,
            SimulationStage.Input,
            [],
            [secondId],
            [],
            SystemThreadMode.OwnerThread), new ProbeSystemFactory(binding));
        _ = builder.RegisterSystem(new SystemDescription(
            secondId,
            1,
            SimulationStage.Input,
            [],
            [firstId],
            [],
            SystemThreadMode.OwnerThread), new ProbeSystemFactory(binding));

        var exception = Assert.Throws<SystemScheduleException>(() => builder.Build());
        Assert.That(exception!.Failure, Is.EqualTo(SystemScheduleFailure.DependencyCycle));
    }

    [Test]
    public void ReportFinalizationFallbackPreservesExactFailureCauses()
    {
        var worldGuid = Guid.NewGuid();
        var systemId = Guid.NewGuid();
        var factoryId = Guid.NewGuid();
        var constructionFailure = new InvalidOperationException("The system construction failed.");
        var cleanupFailure = new InvalidOperationException("The system release failed.");
        var finalizationFailure = new InvalidOperationException("The cleanup exception could not be finalized.");
        var preparation = new WorldSystemCleanupPreparation(worldGuid, 1);
        preparation.Begin(WorldSystemCleanupOperation.ConstructionUnwind, constructionFailure);
        var releaseAttemptIndex = preparation.RecordReleaseAttempt();
        preparation.RecordFailure(systemId, factoryId, 1, releaseAttemptIndex, cleanupFailure);
        preparation.Seal();

        var exception = preparation.CreateCleanupException(_ => throw finalizationFailure);

        Assert.That(exception, Is.TypeOf<WorldSystemCleanupReportFinalizationException>());
        var fallback = (WorldSystemCleanupReportFinalizationException)exception;
        Assert.That(fallback.SystemCleanupReport, Is.SameAs(preparation.Report));
        Assert.That(fallback.ConstructionPrimaryException, Is.SameAs(constructionFailure));
        Assert.That(fallback.OriginalCleanupException, Is.SameAs(cleanupFailure));
        Assert.That(fallback.ReportFinalizationException, Is.SameAs(finalizationFailure));
        Assert.That(fallback.InnerException, Is.Null);
        Assert.That(fallback.SystemCleanupReport.GetFailure(0).ReleaseAttemptIndex, Is.Zero);
    }

    [Test]
    public void StatelessSystemFactoryCapturesExactClosedTypeAndFreezesScheduleRegistration()
    {
        var sourceState = new WorldSystemSourceStateDeclaration(typeof(ProbeSystem), []);
        var binding = WorldSystemFactoryBinding.Stateless<ProbeSystem, ProbeSystemCreate, ProbeSystemRelease>(
            Guid.NewGuid(),
            1,
            sourceState,
            default,
            default);
        var factory = new ProbeSystemFactory(binding);
        var systemId = Guid.NewGuid();
        var description = new SystemDescription(
            systemId,
            -17,
            SimulationStage.Input,
            [],
            [],
            [],
            SystemThreadMode.OwnerThread);
        var builder = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([]));
        _ = builder.RegisterSystem(description, factory);

        Assert.That(factory.BindingReadCount, Is.EqualTo(1));
        Assert.That(() => builder.RegisterSystem(description, factory), Throws.ArgumentException);
        var registration = builder.Build();

        Assert.That(registration.Systems, Has.Length.EqualTo(1));
        Assert.That(registration.Systems[0].Description, Is.SameAs(description));
        Assert.That(registration.Systems[0].Factory.Descriptor.SystemType, Is.EqualTo(typeof(ProbeSystem)));
        Assert.That(registration.Schedule.ExecutionCount, Is.EqualTo(1));
        Assert.That(registration.Schedule.GetSystemAtExecutionIndex(0), Is.SameAs(description));
        Assert.That(registration.Schedule.GetStageStart(SimulationStage.Input), Is.Zero);
        Assert.That(registration.Schedule.GetStageEnd(SimulationStage.Input), Is.EqualTo(1));
    }

    [Test]
    public void StatelessSystemFactoryRejectsSourceLeavesAndWrongSystemType()
    {
        var sourcePath = new WorldSystemSourcePath(["State"]);
        var sourceLeaf = new WorldSystemSourceLeaf(
            sourcePath,
            typeof(int),
            WorldSystemSourcePurpose.ImmutableConfiguration);
        var sourceState = new WorldSystemSourceStateDeclaration(typeof(ProbeSystem), [sourceLeaf]);

        Assert.That(
            () => WorldSystemFactoryBinding.Stateless<ProbeSystem, ProbeSystemCreate, ProbeSystemRelease>(
                Guid.NewGuid(),
                1,
                sourceState,
                default,
                default),
            Throws.ArgumentException);

        var foreignSourceState = new WorldSystemSourceStateDeclaration(typeof(ForeignProbeSystem), []);
        Assert.That(
            () => WorldSystemFactoryBinding.Stateless<ProbeSystem, ProbeSystemCreate, ProbeSystemRelease>(
                Guid.NewGuid(),
                1,
                foreignSourceState,
                default,
                default),
            Throws.ArgumentException);
    }

    [Test]
    public void WorldDisposeAttemptsSystemReleaseOnceAndQuarantinesFailure()
    {
        var sourceState = new WorldSystemSourceStateDeclaration(typeof(ProbeSystem), []);
        var factoryId = Guid.NewGuid();
        var releaseFailure = new ReleaseFailureCounter();
        var binding = WorldSystemFactoryBinding.Stateless<ProbeSystem, ProbeSystemCreate, ProbeSystemReleaseFailure>(
            factoryId,
            1,
            sourceState,
            default,
            new ProbeSystemReleaseFailure(releaseFailure));
        var builder = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([]));
        var systemId = Guid.NewGuid();
        _ = builder.RegisterSystem(
            new SystemDescription(
                systemId,
                0,
                SimulationStage.Input,
                [],
                [],
                [],
                SystemThreadMode.OwnerThread),
            new ProbeSystemFactory(binding));
        var registration = builder.Build();
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });

        Assert.That(world.LastSystemCleanupReport, Is.Null);
        var exception = Assert.Throws<WorldSystemCleanupException>(world.Dispose);
        var cleanupReport = exception!.SystemCleanupReport;
        Assert.That(world.LastSystemCleanupReport, Is.SameAs(cleanupReport));
        Assert.That(cleanupReport.FailureCount, Is.EqualTo(1));
        Assert.That(cleanupReport.ReleaseAttemptCount, Is.EqualTo(1));
        Assert.That(cleanupReport.RetainedFailureCapacity, Is.EqualTo(1));
        Assert.That(cleanupReport.GetFailure(0).SystemId, Is.EqualTo(systemId));
        Assert.That(cleanupReport.GetFailure(0).FactoryId, Is.EqualTo(factoryId));
        Assert.That(cleanupReport.GetFailure(0).OriginalException, Is.SameAs(releaseFailure.Exception));
        Assert.That(cleanupReport.GetFailure(0).ReleaseAttemptIndex, Is.Zero);
        Assert.That(cleanupReport.OmittedFailureCount, Is.Zero);
        Assert.That(exception.InnerExceptions[0], Is.SameAs(releaseFailure.Exception));
        world.Dispose();
        Assert.That(releaseFailure.ReleaseCount, Is.EqualTo(1));
        domain.Dispose();
        Assert.That(domain.Retirement.IsCompleted, Is.False);
    }

    [Test]
    public void WorldDisposeRetainsFirstSixtyFourFailuresAndCountsOmittedFailures()
    {
        const int systemCount = 65;
        var sourceState = new WorldSystemSourceStateDeclaration(typeof(ProbeSystem), []);
        var releaseFailure = new ReleaseFailureCounter();
        var builder = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([]));

        for (var index = 0; index < systemCount; index++)
        {
            var binding = WorldSystemFactoryBinding.Stateless<ProbeSystem, ProbeSystemCreate, ProbeSystemReleaseFailure>(
                Guid.NewGuid(),
                1,
                sourceState,
                default,
                new ProbeSystemReleaseFailure(releaseFailure));
            _ = builder.RegisterSystem(
                new SystemDescription(
                    Guid.NewGuid(),
                    index,
                    SimulationStage.Input,
                    [],
                    [],
                    [],
                    SystemThreadMode.OwnerThread),
                new ProbeSystemFactory(binding));
        }

        var registration = builder.Build();
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using var world = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });

        var exception = Assert.Throws<WorldSystemCleanupException>(world.Dispose);
        var report = exception!.SystemCleanupReport;
        Assert.That(world.LastSystemCleanupReport, Is.SameAs(report));
        Assert.That(releaseFailure.ReleaseCount, Is.EqualTo(systemCount));
        Assert.That(report.ReleaseAttemptCount, Is.EqualTo(systemCount));
        Assert.That(report.RetainedFailureCapacity, Is.EqualTo(64));
        Assert.That(report.FailureCount, Is.EqualTo(systemCount));
        Assert.That(report.RetainedFailureCount, Is.EqualTo(64));
        Assert.That(report.OmittedFailureCount, Is.EqualTo(1));

        for (var index = 0; index < report.RetainedFailureCount; index++)
        {
            Assert.That(report.GetFailure(index).ReleaseAttemptIndex, Is.EqualTo(index));
            Assert.That(report.GetFailure(index).OriginalException, Is.SameAs(releaseFailure.Exception));
        }

        var rangeException = Assert.Throws<ArgumentOutOfRangeException>(() => report.GetFailure(64));
        Assert.That(rangeException!.ParamName, Is.EqualTo("index"));
    }

    [Test]
    public void WorldsRejectAReleasedSystemObjectWhileItsFactoryRetainsIt()
    {
        var sourceState = new WorldSystemSourceStateDeclaration(typeof(ProbeSystem), []);
        var binding = WorldSystemFactoryBinding.Stateless<ProbeSystem, SharedProbeSystemCreate, ProbeSystemRelease>(
            Guid.NewGuid(),
            1,
            sourceState,
            new SharedProbeSystemCreate(s_sharedProbeSystem),
            default);
        var builder = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([]));
        _ = builder.RegisterSystem(
            new SystemDescription(
                Guid.NewGuid(),
                0,
                SimulationStage.Input,
                [],
                [],
                [],
                SystemThreadMode.OwnerThread),
            new ProbeSystemFactory(binding));
        var registration = builder.Build();
        using var domain = new WorldAdmissionDomain(16 * 1024 * 1024);
        using (var firstWorld = World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        }))
        {
            _ = Assert.Throws<InvalidOperationException>(() => World.Create(new WorldOptions(registration) {
                AdmissionDomain = domain,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = Guid.NewGuid(),
            }));
        }

        _ = Assert.Throws<InvalidOperationException>(() => World.Create(new WorldOptions(registration) {
            AdmissionDomain = domain,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        }));
    }

    private sealed class ForeignProbeSystem : IWorldSystem
    {
        public void Execute(WorldSystemContext context)
        {
        }
    }

    private sealed class ProbeSystem : IWorldSystem
    {
        public void Execute(WorldSystemContext context)
        {
        }
    }

    private readonly struct ProbeSystemCreate : IWorldSystemCreate<ProbeSystem>
    {
        public ProbeSystem Invoke() => new();
    }

    private sealed class ProbeSystemFactory : IWorldSystemFactory
    {
        private readonly WorldSystemFactoryBinding _binding;
        private int _bindingReadCount;

        internal ProbeSystemFactory(WorldSystemFactoryBinding binding)
        {
            _binding = binding;
        }

        public WorldSystemFactoryBinding Binding
        {
            get
            {
                _bindingReadCount++;
                return _binding;
            }
        }

        internal int BindingReadCount => _bindingReadCount;
    }

    private readonly struct ProbeSystemRelease : IWorldSystemRelease<ProbeSystem>
    {
        public void Invoke(ProbeSystem system)
            => ArgumentNullException.ThrowIfNull(system);
    }

    private readonly struct ProbeSystemReleaseFailure : IWorldSystemRelease<ProbeSystem>
    {
        private readonly ReleaseFailureCounter _counter;

        internal ProbeSystemReleaseFailure(ReleaseFailureCounter counter)
        {
            _counter = counter;
        }

        public void Invoke(ProbeSystem system)
        {
            ArgumentNullException.ThrowIfNull(system);
            _counter.ReleaseCount++;
            throw _counter.Exception;
        }
    }

    private sealed class ReleaseFailureCounter
    {
        internal InvalidOperationException Exception { get; } = new("The test system Release callback failed.");

        internal int ReleaseCount { get; set; }
    }

    private readonly struct SharedProbeSystemCreate : IWorldSystemCreate<ProbeSystem>
    {
        private readonly ProbeSystem _system;

        internal SharedProbeSystemCreate(ProbeSystem system)
        {
            _system = system;
        }

        public ProbeSystem Invoke() => _system;
    }
}
