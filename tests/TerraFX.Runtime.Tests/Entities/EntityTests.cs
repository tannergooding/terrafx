// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class EntityTests
{
    [TestCase(0u, 1u, 1u, "ownerId")]
    [TestCase(1u, 0u, 1u, "index")]
    [TestCase(1u, 1u, 0u, "generation")]
    public void ConstructorRejectsReservedZeroIdentityParts(uint ownerId, uint index, uint generation, string parameterName)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new Entity(ownerId, index, generation));

        Assert.That(exception!.ParamName, Is.EqualTo(parameterName));
    }

    [Test]
    public void DefaultEntityIsInvalid()
    {
        Assert.That(default(Entity).IsValid, Is.False);
    }

    [Test]
    public void EqualityUsesTheCompleteOwnerIndexGenerationTuple()
    {
        var entity = new Entity(1, 2, 3);
        var sameEntity = new Entity(1, 2, 3);

        Assert.That(entity.IsValid, Is.True);
        Assert.That(entity, Is.EqualTo(sameEntity));
        Assert.That(entity == sameEntity, Is.True);
        Assert.That(entity.GetHashCode(), Is.EqualTo(sameEntity.GetHashCode()));

        Assert.That(entity == new Entity(4, 2, 3), Is.False);
        Assert.That(entity == new Entity(1, 4, 3), Is.False);
        Assert.That(entity == new Entity(1, 2, 4), Is.False);
        Assert.That(entity != sameEntity, Is.False);
    }
}
