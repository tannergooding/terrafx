// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldStepTests
{
    private static WorldSystemFactoryBinding CreateBinding(StepProbe probe, int systemIndex)
    {
        var sourceState = new WorldSystemSourceStateDeclaration(typeof(ProbeSystem), []);
        return WorldSystemFactoryBinding.Stateless<ProbeSystem, ProbeSystemCreate, ProbeSystemRelease>(
            Guid.NewGuid(),
            1,
            sourceState,
            new ProbeSystemCreate(probe, systemIndex),
            default);
    }

    private static WorldRegistration CreateRegistration(StepProbe probe, int systemCount, SystemThreadMode threadMode)
    {
        var builder = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([]));

        for (var index = 0; index < systemCount; index++)
        {
            _ = builder.RegisterSystem(
                new SystemDescription(
                    Guid.NewGuid(),
                    index,
                    SimulationStage.Input,
                    [],
                    [],
                    [],
                    threadMode),
                new ProbeSystemFactory(CreateBinding(probe, index)));
        }

        return builder.Build();
    }

    [Test]
    public void ParallelSystemsOverlapAndStepJoinsWorkers()
    {
        using var entered = new CountdownEvent(4);
        using var release = new ManualResetEventSlim();
        var probe = new StepProbe(entered, release);
        using var world = World.Create(new WorldOptions(CreateRegistration(probe, 4, SystemThreadMode.Parallel)) {
            CpuWorkerCount = 4,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
        var releaseWorkers = Task.Run(() => {
            try
            {
                Assert.That(entered.Wait(TimeSpan.FromSeconds(15)), Is.True);
                Assert.That(probe.WorkerThreadIds.Distinct().Count(), Is.EqualTo(4));
            }
            finally
            {
                release.Set();
            }
        });

        var receipt = world.Step();
        releaseWorkers.GetAwaiter().GetResult();

        Assert.That(receipt.CompletedTick, Is.Zero);
        Assert.That(receipt.Publication.PublicationSequence, Is.EqualTo(1));
        Assert.That(probe.Invocations, Has.Length.EqualTo(4));
    }

    [Test]
    public void StepPublishesFixedTicksWithRetainedReceipts()
    {
        var probe = new StepProbe();
        using var world = World.Create(new WorldOptions(CreateRegistration(probe, 1, SystemThreadMode.OwnerThread)) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });

        var firstReceipt = world.Step();
        var secondReceipt = world.Step();

        Assert.That(firstReceipt.CompletedTick, Is.Zero);
        Assert.That(firstReceipt.NextTickIndex, Is.EqualTo(1));
        Assert.That(firstReceipt.Publication.PublicationSequence, Is.EqualTo(1));
        Assert.That(firstReceipt.Publication.CommandReceipts, Is.Empty);
        Assert.That(firstReceipt.Publication.Events, Is.Empty);
        Assert.That(firstReceipt.DeliveryReport.Publication, Is.SameAs(firstReceipt.Publication));
        Assert.That(secondReceipt.CompletedTick, Is.EqualTo(1));
        Assert.That(secondReceipt.NextTickIndex, Is.EqualTo(2));
        Assert.That(secondReceipt.Publication.PublicationSequence, Is.EqualTo(2));
        Assert.That(firstReceipt.Publication.NextTickIndex, Is.EqualTo(1));
        Assert.That(probe.Invocations.Select(static invocation => invocation.Step.TickIndex), Is.EqualTo([0, 1]));
        Assert.That(probe.Invocations.Select(static invocation => invocation.Step.TickRate), Is.All.EqualTo(60));
    }

    [Test]
    public void SystemFailureFaultsTickWithoutPublishing()
    {
        var expected = new InvalidOperationException("system execution failed");
        var probe = new StepProbe(failure: expected);
        using var world = World.Create(new WorldOptions(CreateRegistration(probe, 1, SystemThreadMode.OwnerThread)) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });

        var exception = Assert.Throws<WorldStepException>(() => world.Step());

        Assert.That(exception!.Tick, Is.Zero);
        Assert.That(exception.InnerException, Is.SameAs(expected));
        Assert.That(exception.Diagnostic.Code, Is.EqualTo("WORLD_STEP_FAILED"));
        Assert.That(world.LastFault, Is.SameAs(exception.Diagnostic));
        Assert.That(probe.Invocations, Has.Length.EqualTo(1));
    }

    private sealed class ProbeSystem : IWorldSystem
    {
        private readonly StepProbe _probe;
        private readonly int _systemIndex;

        internal ProbeSystem(StepProbe probe, int systemIndex)
        {
            _probe = probe;
            _systemIndex = systemIndex;
        }

        public void Execute(WorldSystemContext context)
            => _probe.Execute(_systemIndex, context);
    }

    private readonly struct ProbeSystemCreate : IWorldSystemCreate<ProbeSystem>
    {
        private readonly StepProbe _probe;
        private readonly int _systemIndex;

        internal ProbeSystemCreate(StepProbe probe, int systemIndex)
        {
            _probe = probe;
            _systemIndex = systemIndex;
        }

        public ProbeSystem Invoke() => new(_probe, _systemIndex);
    }

    private sealed class ProbeSystemFactory : IWorldSystemFactory
    {
        private readonly WorldSystemFactoryBinding _binding;

        internal ProbeSystemFactory(WorldSystemFactoryBinding binding)
        {
            _binding = binding;
        }

        public WorldSystemFactoryBinding Binding => _binding;
    }

    private readonly struct ProbeSystemRelease : IWorldSystemRelease<ProbeSystem>
    {
        public void Invoke(ProbeSystem system)
            => ArgumentNullException.ThrowIfNull(system);
    }

    private readonly record struct StepInvocation(int SystemIndex, SimulationStepContext Step, int ThreadId);

    private sealed class StepProbe
    {
        private readonly CountdownEvent? _entered;
        private readonly Exception? _failure;
        private readonly ManualResetEventSlim? _release;
        private readonly ConcurrentBag<int> _workerThreadIds = [];
        private readonly List<StepInvocation> _invocations = [];
        private readonly Lock _gate = new();

        internal StepProbe(
            CountdownEvent? entered = null,
            ManualResetEventSlim? release = null,
            Exception? failure = null)
        {
            _entered = entered;
            _release = release;
            _failure = failure;
        }

        internal ImmutableArray<StepInvocation> Invocations
        {
            get
            {
                lock (_gate)
                {
                    return [.. _invocations];
                }
            }
        }

        internal int[] WorkerThreadIds => [.. _workerThreadIds];

        internal void Execute(int systemIndex, WorldSystemContext context)
        {
            var threadId = Environment.CurrentManagedThreadId;

            lock (_gate)
            {
                _invocations.Add(new StepInvocation(systemIndex, context.Step, threadId));
            }

            _workerThreadIds.Add(threadId);

            if (_failure is not null)
            {
                throw _failure;
            }

            if (_entered is not null)
            {
                _ = _entered.Signal();

                if (!(_release?.Wait(TimeSpan.FromSeconds(15)) ?? false))
                {
                    throw new TimeoutException("The scheduler did not release a held system worker.");
                }
            }
        }
    }
}
