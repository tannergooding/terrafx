// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneOccurrenceIdentityTests
{
    [Test]
    public void DerivesOccurrenceAndExpandedKeysFromContentSetAddresses()
    {
        var rootOccurrence = SceneOccurrenceIdentity.CreateRootOccurrence(new AssetId(3));
        Assert.That(rootOccurrence, Is.EqualTo(new Guid("707e6491-a3ad-8277-a8f1-f4698b596cce")));

        var firstPlacement = new Guid("40000000-0000-4000-8000-000000000001");
        var secondPlacement = new Guid("40000000-0000-4000-8000-000000000002");
        var firstNestedPlacement = new Guid("50000000-0000-4000-8000-000000000001");
        var secondNestedPlacement = new Guid("50000000-0000-4000-8000-000000000002");
        var authoredEntityId = new Guid("60000000-0000-4000-8000-000000000003");

        var firstOccurrence = SceneOccurrenceIdentity.CreatePrefabOccurrence(rootOccurrence, firstPlacement, new AssetId(2));
        var secondOccurrence = SceneOccurrenceIdentity.CreatePrefabOccurrence(rootOccurrence, secondPlacement, new AssetId(2));

        Assert.That(firstOccurrence, Is.EqualTo(new Guid("74870312-0067-811c-97c4-291c417b2d62")));
        Assert.That(secondOccurrence, Is.EqualTo(new Guid("409298d6-12b6-88ef-83db-b1d6b05d89c7")));

        var expected = new[]
        {
            (firstOccurrence, firstNestedPlacement, new Guid("09297c61-1872-8733-8abd-d0c2f03f5002"), new Guid("642ba622-c01a-8c49-9fd4-4e4064cf728d")),
            (firstOccurrence, secondNestedPlacement, new Guid("70a0fdec-191f-8b48-b1db-6d33143aa8ba"), new Guid("b573915b-3053-8023-8576-34caae81a8a2")),
            (secondOccurrence, firstNestedPlacement, new Guid("177b54ae-5d1f-8daa-9d13-4f099e610026"), new Guid("27bc2516-bd90-82d6-8226-0394934f96b5")),
            (secondOccurrence, secondNestedPlacement, new Guid("a27f7760-1606-8e8e-ad01-2ad6c60e19c6"), new Guid("1b623ada-c0bb-814c-bb08-9c49c54b33e9")),
        };

        foreach (var (parentOccurrence, placementId, expectedOccurrence, expectedEntityKey) in expected)
        {
            var occurrence = SceneOccurrenceIdentity.CreatePrefabOccurrence(parentOccurrence, placementId, new AssetId(1));

            Assert.That(occurrence, Is.EqualTo(expectedOccurrence));
            Assert.That(
                SceneOccurrenceIdentity.CreateExpandedEntityKey(occurrence, authoredEntityId),
                Is.EqualTo(expectedEntityKey));
        }
    }

    [Test]
    public void RejectsInvalidSceneIdentityOperands()
    {
        Assert.That(
            () => SceneOccurrenceIdentity.CreateRootOccurrence(default),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("rootAssetId"));

        Assert.That(
            () => SceneOccurrenceIdentity.CreatePrefabOccurrence(Guid.Empty, Guid.NewGuid(), new AssetId(1)),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("parentOccurrence"));

        Assert.That(
            () => SceneOccurrenceIdentity.CreatePrefabOccurrence(Guid.NewGuid(), Guid.Empty, new AssetId(1)),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("placementId"));

        Assert.That(
            () => SceneOccurrenceIdentity.CreatePrefabOccurrence(Guid.NewGuid(), Guid.NewGuid(), default),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("targetAssetId"));

        Assert.That(
            () => SceneOccurrenceIdentity.CreateExpandedEntityKey(Guid.Empty, Guid.NewGuid()),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("occurrence"));

        Assert.That(
            () => SceneOccurrenceIdentity.CreateExpandedEntityKey(Guid.NewGuid(), Guid.Empty),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("authoredEntityId"));
    }

    [Test]
    public void RejectsSceneIdentityDuplicatesAndEmptyValues()
    {
        var duplicate = new Guid("7fffffff-0000-0000-0000-000000000000");
        Guid[] emptyOccurrence = [Guid.Empty];
        Guid[] duplicateOccurrences = [duplicate, duplicate];
        Guid[] emptyEntityKey = [Guid.Empty];
        Guid[] duplicateEntityKeys = [duplicate, duplicate];

        Assert.That(
            () => SceneOccurrenceIdentity.SortOccurrenceIdsAndValidateUnique(emptyOccurrence),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("occurrenceIds"));

        Assert.That(
            () => SceneOccurrenceIdentity.SortOccurrenceIdsAndValidateUnique(duplicateOccurrences),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("occurrenceIds"));

        Assert.That(
            () => SceneOccurrenceIdentity.SortExpandedEntityKeysForAssignment(emptyEntityKey),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("expandedEntityKeys"));

        Assert.That(
            () => SceneOccurrenceIdentity.SortExpandedEntityKeysForAssignment(duplicateEntityKeys),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("expandedEntityKeys"));
    }

    [Test]
    public void SortsUniqueOccurrenceAndEntityKeysByUnsignedRfcBytes()
    {
        var lower = new Guid("7fffffff-0000-0000-0000-000000000000");
        var upper = new Guid("80000000-0000-0000-0000-000000000000");
        Guid[] expected = [lower, upper];
        var occurrences = new[] { upper, lower };
        var entityKeys = new[] { upper, lower };

        SceneOccurrenceIdentity.SortOccurrenceIdsAndValidateUnique(occurrences);
        SceneOccurrenceIdentity.SortExpandedEntityKeysForAssignment(entityKeys);

        Assert.That(occurrences, Is.EqualTo(expected));
        Assert.That(entityKeys, Is.EqualTo(expected));
    }
}
