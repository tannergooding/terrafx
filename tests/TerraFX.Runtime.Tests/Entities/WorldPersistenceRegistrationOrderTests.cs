// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldPersistenceRegistrationOrderTests
{
    private static void RegisterSystem(WorldRegistrationBuilder builder, Guid systemId, int order)
    {
        var description = new SystemDescription(
            systemId,
            order,
            SimulationStage.Input,
            [],
            [],
            [],
            SystemThreadMode.OwnerThread);
        var sourceState = new WorldSystemSourceStateDeclaration(typeof(PersistenceOrderSystem), []);
        var binding = WorldSystemFactoryBinding.Stateless<
            PersistenceOrderSystem,
            PersistenceOrderSystemCreate,
            PersistenceOrderSystemRelease>(
            Guid.NewGuid(),
            1,
            sourceState,
            default,
            default);
        _ = builder.RegisterSystem(description, new PersistenceOrderSystemFactory(binding));
    }

    [Test]
    public void CopiesSystemsInSignedOrderWithoutChangingRuntimeRegistrationOrder()
    {
        var highId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var lowId = Guid.Parse("90000000-0000-0000-0000-000000000009");
        var middleId = Guid.Parse("50000000-0000-0000-0000-000000000005");
        var builder = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([]));
        RegisterSystem(builder, highId, 17);
        RegisterSystem(builder, lowId, -5);
        RegisterSystem(builder, middleId, 3);
        var registration = builder.Build();
        Span<int> indices = stackalloc int[registration.Systems.Length];

        registration.CopyPersistenceSystemOrder(indices);

        Assert.That(registration.Systems[0].Description.SystemId, Is.EqualTo(highId));
        Assert.That(indices.ToArray(), Is.EqualTo([2, 1, 0]));
        Assert.That(
            registration.Systems[indices[0]].Description.Order,
            Is.LessThan(registration.Systems[indices[1]].Description.Order));
        Assert.That(
            registration.Systems[indices[1]].Description.Order,
            Is.LessThan(registration.Systems[indices[2]].Description.Order));
    }

    [Test]
    public void RequiresExactCallerOwnedIndexWorkspace()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        _ = Assert.Throws<ArgumentException>(() => registration.CopyPersistenceSystemOrder([0]));
    }

    [Test]
    public void SupportsAnEmptyRegistrationWithoutAllocatingOrderEntries()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        registration.CopyPersistenceSystemOrder([]);
    }

    private sealed class PersistenceOrderSystem : IWorldSystem
    {
        public void Execute(WorldSystemContext context)
        {
        }
    }

    private readonly struct PersistenceOrderSystemCreate : IWorldSystemCreate<PersistenceOrderSystem>
    {
        public PersistenceOrderSystem Invoke() => new();
    }

    private sealed class PersistenceOrderSystemFactory : IWorldSystemFactory
    {
        internal PersistenceOrderSystemFactory(WorldSystemFactoryBinding binding)
        {
            Binding = binding;
        }

        public WorldSystemFactoryBinding Binding { get; }
    }

    private readonly struct PersistenceOrderSystemRelease : IWorldSystemRelease<PersistenceOrderSystem>
    {
        public void Invoke(PersistenceOrderSystem system)
            => ArgumentNullException.ThrowIfNull(system);
    }
}
