// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class EntityDescriptionTests
{
    [Test]
    public void DefaultInitializationDoesNotExposeAComponentValue()
    {
        var initialization = ComponentInitialization.Defaults<int>();

        Assert.That(initialization.ComponentType, Is.EqualTo(typeof(int)));
        Assert.That(initialization.IsDefault, Is.True);
        Assert.That(initialization.TryGetValue<int>(out var value), Is.False);
        Assert.That(value, Is.Zero);
        Assert.That(initialization.TryGetValue<float>(out _), Is.False);
    }

    [Test]
    public void DescriptionCopiesItsInputsAndAllowsUuidV8Candidates()
    {
        var persistentId = new Guid("01234567-89ab-8def-8123-456789abcdef");
        var componentValue = 42;
        var components = ImmutableArray.Create<ComponentInitialization>(ComponentInitialization.Value(in componentValue));
        var tagId = Guid.NewGuid();
        var tags = ImmutableArray.Create(tagId);
        var description = new EntityDescription(persistentId, components, tags);
        componentValue = 99;

        Assert.That(componentValue, Is.EqualTo(99));
        Assert.That(description.PersistentId, Is.EqualTo(persistentId));
        Assert.That(description.Components.Length, Is.EqualTo(1));
        Assert.That(description.Components[0].IsDefault, Is.False);
        Assert.That(description.Components[0].TryGetValue<int>(out var copiedValue), Is.True);
        Assert.That(copiedValue, Is.EqualTo(42));
        Assert.That(description.Components[0].TryGetValue<long>(out _), Is.False);
        Assert.That(description.Tags, Is.EqualTo(ImmutableArray.Create(tagId)));
    }

    [Test]
    public void DescriptionDefaultsAbsentCollectionsToInitializedEmptyArrays()
    {
        var description = new EntityDescription();

        Assert.That(description.PersistentId, Is.Null);
        Assert.That(description.Components, Is.Empty);
        Assert.That(description.Tags, Is.Empty);
        Assert.That(description.Components.IsDefault, Is.False);
        Assert.That(description.Tags.IsDefault, Is.False);
    }

    [Test]
    public void DescriptionRejectsEmptyIdentityDuplicateTypesAndDuplicateTags()
    {
        _ = Assert.Throws<ArgumentException>(() => _ = new EntityDescription(Guid.Empty));
        _ = Assert.Throws<ArgumentException>(() => _ = new EntityDescription(
            components: [
                ComponentInitialization.Value(1),
                ComponentInitialization.Defaults<int>(),
            ]));

        var tagId = Guid.NewGuid();
        _ = Assert.Throws<ArgumentException>(() => _ = new EntityDescription(tags: [tagId, tagId]));
        _ = Assert.Throws<ArgumentException>(() => _ = new EntityDescription(tags: [Guid.Empty]));
    }
}
