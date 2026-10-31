// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneLoadOptionsTests
{
    [Test]
    public void CopiesAndSortsExternalBindingsByRfcSlotId()
    {
        var rfcLater = new Guid("80000000-0000-4000-8000-000000000001");
        var rfcEarlier = new Guid("7fffffff-0000-4000-8000-000000000002");
        var mutableBindings = new[] {
            new SceneExternalBinding(rfcLater, target: null),
            new SceneExternalBinding(rfcEarlier, target: null),
        };
        var options = new SceneLoadOptions {
            ExternalBindings = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(mutableBindings),
        };

        var snapshot = options.ValidateAndFreeze();
        mutableBindings[0] = new SceneExternalBinding(Guid.NewGuid(), target: null);

        Assert.That(snapshot.ExternalBindings, Has.Length.EqualTo(2));
        Assert.That(snapshot.ExternalBindings[0].SlotId, Is.EqualTo(rfcEarlier));
        Assert.That(snapshot.ExternalBindings[1].SlotId, Is.EqualTo(rfcLater));
    }

    [Test]
    public void DefaultsToAnEmptyExternalBindingSet()
    {
        var snapshot = new SceneLoadOptions().ValidateAndFreeze();

        Assert.That(snapshot.SceneInstanceId, Is.Null);
        Assert.That(snapshot.ExternalBindings, Is.Empty);
        Assert.That(snapshot.MaximumExpandedEntities, Is.EqualTo(1_048_576));
        Assert.That(snapshot.MaximumExpandedOccurrences, Is.EqualTo(1_048_576));
        Assert.That(snapshot.MaximumPrefabEntities, Is.EqualTo(65_536));
        Assert.That(snapshot.MaximumPrefabDepth, Is.EqualTo(32));
        Assert.That(snapshot.MaximumHierarchyDepth, Is.EqualTo(256));
    }

    [Test]
    public void DistinguishesOmittedBindingsFromExplicitNull()
    {
        var slotId = new Guid("30000000-0000-4000-8000-000000000071");
        var omitted = new SceneLoadOptions().ValidateAndFreeze();
        var explicitNull = new SceneLoadOptions {
            ExternalBindings = [new SceneExternalBinding(slotId, target: null)],
        }.ValidateAndFreeze();

        Assert.That(omitted.ExternalBindings, Is.Empty);
        Assert.That(explicitNull.ExternalBindings, Has.Length.EqualTo(1));
        Assert.That(explicitNull.ExternalBindings[0].SlotId, Is.EqualTo(slotId));
        Assert.That(explicitNull.ExternalBindings[0].Target, Is.Null);
    }

    [Test]
    public void RejectsDefaultEntityTargets()
    {
        var slotId = new Guid("30000000-0000-4000-8000-000000000072");

        Assert.That(
            () => new SceneExternalBinding(slotId, new Entity()),
            Throws.ArgumentException);
    }

    [Test]
    public void RejectsDuplicateExternalSlotIds()
    {
        var slotId = new Guid("30000000-0000-4000-8000-000000000073");
        var options = new SceneLoadOptions {
            ExternalBindings = [
                new SceneExternalBinding(slotId, target: null),
                new SceneExternalBinding(slotId, target: null),
            ],
        };

        Assert.That(options.ValidateAndFreeze, Throws.ArgumentException);
    }

    [Test]
    public void RejectsEmptyExternalSlotIds()
    {
        Assert.That(
            () => new SceneExternalBinding(Guid.Empty, target: null),
            Throws.ArgumentException);
    }

    [Test]
    public void RejectsInvalidLimitsAndSceneInstanceIds()
    {
        Assert.That(
            () => new SceneLoadOptions { MaximumExpandedEntities = 0 }.ValidateAndFreeze(),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            () => new SceneLoadOptions { MaximumPrefabDepth = 33 }.ValidateAndFreeze(),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(
            () => new SceneLoadOptions { SceneInstanceId = Guid.Empty }.ValidateAndFreeze(),
            Throws.TypeOf<ArgumentException>());
        Assert.That(
            () => new SceneLoadOptions {
                SceneInstanceId = new Guid("10000000-0000-8000-8000-000000000001"),
            }.ValidateAndFreeze(),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void RejectsUninitializedExternalBindingArray()
    {
        var options = new SceneLoadOptions {
            ExternalBindings = default,
        };

        Assert.That(options.ValidateAndFreeze, Throws.ArgumentException);
    }
}
