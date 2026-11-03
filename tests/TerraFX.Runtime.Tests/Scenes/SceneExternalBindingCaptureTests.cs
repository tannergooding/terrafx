// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneExternalBindingCaptureTests
{
    private static World CreateWorld()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        return World.Create(new WorldOptions(registration) {
            CpuWorkerCount = 0,
            InitialReplayLineageId = Guid.NewGuid(),
            WorldGuid = Guid.NewGuid(),
        });
    }

    [Test]
    public void CapturesExplicitNullAndTargetIdentityInCanonicalSlotOrder()
    {
        using var world = CreateWorld();
        var target = world.CreateEntity(new EntityDescription());
        var targetEntityId = world.GetEntityId(target);
        var earlierSlotId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var laterSlotId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        ImmutableArray<SceneExternalBinding> bindings = [
            new SceneExternalBinding(laterSlotId, target),
            new SceneExternalBinding(earlierSlotId, target: null),
        ];

        var captureRows = SceneInstance.CaptureExternalBindings(world, bindings);

        Assert.That(captureRows.Length, Is.EqualTo(2));
        Assert.That(captureRows[0].BindingId, Is.EqualTo(earlierSlotId));
        Assert.That(captureRows[0].TargetEntityId, Is.Null);
        Assert.That(captureRows[1].BindingId, Is.EqualTo(laterSlotId));
        Assert.That(captureRows[1].TargetEntityId, Is.EqualTo(targetEntityId));
    }

    [Test]
    public void RejectsForeignWorldTarget()
    {
        using var targetWorld = CreateWorld();
        using var ownerWorld = CreateWorld();
        var target = targetWorld.CreateEntity(new EntityDescription());
        var slotId = Guid.Parse("10000000-0000-0000-0000-000000000001");

        Assert.That(
            () => SceneInstance.CaptureExternalBindings(
                ownerWorld,
                [new SceneExternalBinding(slotId, target)]),
            Throws.TypeOf<ArgumentException>());
    }
}
