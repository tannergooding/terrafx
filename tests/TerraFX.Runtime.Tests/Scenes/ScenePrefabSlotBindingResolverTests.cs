// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class ScenePrefabSlotBindingResolverTests
{
    private static SceneReferenceTarget CreateExternalSlotTarget(Guid slotId)
        => new(SceneReferenceTargetKind.ExternalSlot, entity: null, externalSlotId: slotId);

    private static SceneReferenceTarget CreateInternalTarget(Guid entityId, SceneEntitySelectorScope scope)
        => new(
            SceneReferenceTargetKind.Internal,
            new SceneEntitySelector(scope, [], entityId),
            externalSlotId: null);

    private static ScenePrefabPlacement CreatePlacement(
        AssetId childPrefabId,
        ImmutableArray<ScenePlacementSlotBinding> bindings)
        => new(
            new Guid("20000000-0000-4000-8000-000000000041"),
            childPrefabId,
            parentEntityId: null,
            rootPoses: [],
            overrides: [],
            slotBindings: bindings,
            encodedRecord: [1]);

    private static ScenePrefabAsset CreatePrefabAsset(
        AssetId assetId,
        ImmutableArray<SceneExternalSlot> externalSlots)
        => new(
            assetId,
            entities: [],
            hierarchy: new SceneHierarchy([], []),
            placements: [],
            references: [],
            externalSlots,
            encodedRecord: [1]);

    private static ContentSchemaReference CreateSchemaReference(string schemaName)
        => new(
            new ContentSchemaIdentityAssignment(schemaName).SchemaId,
            1,
            0,
            ContentSchemaScope.Content);

    private static SceneExternalSlot CreateSlot(
        Guid slotId,
        bool required,
        bool allowNull,
        ImmutableArray<ContentSchemaReference> requiredComponents)
        => new(slotId, required, allowNull, requiredComponents);

    private static ImmutableArray<ScenePlacementSlotBinding> Resolve(
        ScenePrefabPlacement placement,
        ScenePrefabAsset childPrefab,
        ImmutableArray<SceneExternalSlot> containingSlots,
        Func<SceneEntitySelector, ImmutableArray<ContentSchemaReference>?> resolveInternalTargetSchemas,
        bool allowInstanceAbsoluteInternalTargets)
        => ScenePrefabSlotBindingResolver.Resolve(
            placement,
            childPrefab,
            containingSlots,
            resolveInternalTargetSchemas,
            allowInstanceAbsoluteInternalTargets);

    [Test]
    public void AllowsExplicitNullForNullableOptionalSlot()
    {
        var slotId = new Guid("30000000-0000-4000-8000-000000000041");
        var childSlot = CreateSlot(slotId, required: false, allowNull: true, requiredComponents: []);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var placement = CreatePlacement(
            childPrefab.AssetId,
            [new ScenePlacementSlotBinding(slotId, target: null)]);

        var resolved = Resolve(
            placement,
            childPrefab,
            containingSlots: [],
            resolveInternalTargetSchemas: static _ => null,
            allowInstanceAbsoluteInternalTargets: false);

        Assert.That(resolved, Has.Length.EqualTo(1));
        Assert.That(resolved[0].SlotId, Is.EqualTo(slotId));
        Assert.That(resolved[0].Target, Is.Null);
    }

    [Test]
    public void AllowsUnboundOptionalSlot()
    {
        var slotId = new Guid("30000000-0000-4000-8000-000000000042");
        var childSlot = CreateSlot(slotId, required: false, allowNull: false, requiredComponents: []);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var placement = CreatePlacement(childPrefab.AssetId, []);

        var resolved = Resolve(
            placement,
            childPrefab,
            containingSlots: [],
            resolveInternalTargetSchemas: static _ => null,
            allowInstanceAbsoluteInternalTargets: false);

        Assert.That(resolved, Has.Length.EqualTo(1));
        Assert.That(resolved[0].SlotId, Is.EqualTo(slotId));
        Assert.That(resolved[0].Target, Is.Null);
    }

    [Test]
    public void RejectsDuplicatePlacementSlotBindings()
    {
        var slotId = new Guid("30000000-0000-4000-8000-000000000043");
        var childSlot = CreateSlot(slotId, required: false, allowNull: true, requiredComponents: []);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var binding = new ScenePlacementSlotBinding(slotId, target: null);
        var placement = CreatePlacement(childPrefab.AssetId, [binding, binding]);

        Assert.That(
            () => Resolve(placement, childPrefab, [], static _ => null, allowInstanceAbsoluteInternalTargets: false),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsExternalSlotForwardingToUndeclaredContainingSlot()
    {
        var childSlotId = new Guid("30000000-0000-4000-8000-000000000044");
        var parentSlotId = new Guid("30000000-0000-4000-8000-000000000045");
        var childSlot = CreateSlot(childSlotId, required: false, allowNull: true, requiredComponents: []);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var placement = CreatePlacement(
            childPrefab.AssetId,
            [new ScenePlacementSlotBinding(childSlotId, CreateExternalSlotTarget(parentSlotId))]);

        Assert.That(
            () => Resolve(placement, childPrefab, [], static _ => null, allowInstanceAbsoluteInternalTargets: false),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsExternalSlotForwardingWhenRequiredComponentsAreNotGuaranteed()
    {
        var childSlotId = new Guid("30000000-0000-4000-8000-000000000046");
        var parentSlotId = new Guid("30000000-0000-4000-8000-000000000047");
        var cameraSchema = CreateSchemaReference("scene/camera");
        var childSlot = CreateSlot(childSlotId, required: false, allowNull: true, [cameraSchema]);
        var containingSlot = CreateSlot(parentSlotId, required: false, allowNull: true, []);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var placement = CreatePlacement(
            childPrefab.AssetId,
            [new ScenePlacementSlotBinding(childSlotId, CreateExternalSlotTarget(parentSlotId))]);

        Assert.That(
            () => Resolve(placement, childPrefab, [containingSlot], static _ => null, allowInstanceAbsoluteInternalTargets: false),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsInstanceAbsoluteInternalTargetForPrefab()
    {
        var childSlotId = new Guid("30000000-0000-4000-8000-000000000048");
        var targetEntityId = new Guid("40000000-0000-4000-8000-000000000048");
        var childSlot = CreateSlot(childSlotId, required: false, allowNull: true, requiredComponents: []);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var placement = CreatePlacement(
            childPrefab.AssetId,
            [new ScenePlacementSlotBinding(
                childSlotId,
                CreateInternalTarget(targetEntityId, SceneEntitySelectorScope.InstanceAbsolute))]);

        Assert.That(
            () => Resolve(placement, childPrefab, [], static _ => [], allowInstanceAbsoluteInternalTargets: false),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsInternalTargetMissingRequiredComponent()
    {
        var childSlotId = new Guid("30000000-0000-4000-8000-000000000049");
        var targetEntityId = new Guid("40000000-0000-4000-8000-000000000049");
        var cameraSchema = CreateSchemaReference("scene/camera");
        var childSlot = CreateSlot(childSlotId, required: false, allowNull: true, [cameraSchema]);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var placement = CreatePlacement(
            childPrefab.AssetId,
            [new ScenePlacementSlotBinding(
                childSlotId,
                CreateInternalTarget(targetEntityId, SceneEntitySelectorScope.OccurrenceRelative))]);

        Assert.That(
            () => Resolve(placement, childPrefab, [], static _ => ImmutableArray<ContentSchemaReference>.Empty, allowInstanceAbsoluteInternalTargets: false),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsMissingRequiredChildSlotBinding()
    {
        var slotId = new Guid("30000000-0000-4000-8000-000000000050");
        var childSlot = CreateSlot(slotId, required: true, allowNull: false, requiredComponents: []);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var placement = CreatePlacement(childPrefab.AssetId, []);

        Assert.That(
            () => Resolve(placement, childPrefab, [], static _ => null, allowInstanceAbsoluteInternalTargets: false),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsNullForNonNullableOptionalSlot()
    {
        var slotId = new Guid("30000000-0000-4000-8000-000000000051");
        var childSlot = CreateSlot(slotId, required: false, allowNull: false, requiredComponents: []);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var placement = CreatePlacement(
            childPrefab.AssetId,
            [new ScenePlacementSlotBinding(slotId, target: null)]);

        Assert.That(
            () => Resolve(placement, childPrefab, [], static _ => null, allowInstanceAbsoluteInternalTargets: false),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsRequiredChildSlotForwardedThroughOptionalContainingSlot()
    {
        var childSlotId = new Guid("30000000-0000-4000-8000-000000000052");
        var parentSlotId = new Guid("30000000-0000-4000-8000-000000000053");
        var childSlot = CreateSlot(childSlotId, required: true, allowNull: false, requiredComponents: []);
        var containingSlot = CreateSlot(parentSlotId, required: false, allowNull: false, requiredComponents: []);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var placement = CreatePlacement(
            childPrefab.AssetId,
            [new ScenePlacementSlotBinding(childSlotId, CreateExternalSlotTarget(parentSlotId))]);

        Assert.That(
            () => Resolve(placement, childPrefab, [containingSlot], static _ => null, allowInstanceAbsoluteInternalTargets: false),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsUnknownChildSlotBinding()
    {
        var childSlotId = new Guid("30000000-0000-4000-8000-000000000059");
        var unknownSlotId = new Guid("30000000-0000-4000-8000-000000000060");
        var childSlot = CreateSlot(childSlotId, required: false, allowNull: true, requiredComponents: []);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var placement = CreatePlacement(
            childPrefab.AssetId,
            [new ScenePlacementSlotBinding(unknownSlotId, target: null)]);

        Assert.That(
            () => Resolve(placement, childPrefab, [], static _ => null, allowInstanceAbsoluteInternalTargets: false),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RejectsUnresolvedInternalTarget()
    {
        var childSlotId = new Guid("30000000-0000-4000-8000-000000000058");
        var targetEntityId = new Guid("40000000-0000-4000-8000-000000000058");
        var childSlot = CreateSlot(childSlotId, required: false, allowNull: true, requiredComponents: []);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var placement = CreatePlacement(
            childPrefab.AssetId,
            [new ScenePlacementSlotBinding(
                childSlotId,
                CreateInternalTarget(targetEntityId, SceneEntitySelectorScope.OccurrenceRelative))]);

        Assert.That(
            () => Resolve(placement, childPrefab, [], static _ => null, allowInstanceAbsoluteInternalTargets: false),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ResolvesExternalSlotForwardingWithCompatibleRequirements()
    {
        var childSlotId = new Guid("30000000-0000-4000-8000-000000000054");
        var parentSlotId = new Guid("30000000-0000-4000-8000-000000000055");
        var cameraSchema = CreateSchemaReference("scene/camera");
        var childSlot = CreateSlot(childSlotId, required: false, allowNull: true, [cameraSchema]);
        var containingSlot = CreateSlot(parentSlotId, required: false, allowNull: true, [cameraSchema]);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var placement = CreatePlacement(
            childPrefab.AssetId,
            [new ScenePlacementSlotBinding(childSlotId, CreateExternalSlotTarget(parentSlotId))]);

        var resolved = Resolve(
            placement,
            childPrefab,
            [containingSlot],
            static _ => null,
            allowInstanceAbsoluteInternalTargets: false);

        Assert.That(resolved, Has.Length.EqualTo(1));
        Assert.That(resolved[0].SlotId, Is.EqualTo(childSlotId));
        Assert.That(resolved[0].Target?.ExternalSlotId, Is.EqualTo(parentSlotId));
    }

    [Test]
    public void ResolvesInstanceAbsoluteInternalTargetForScene()
    {
        var childSlotId = new Guid("30000000-0000-4000-8000-000000000056");
        var targetEntityId = new Guid("40000000-0000-4000-8000-000000000056");
        var childSlot = CreateSlot(childSlotId, required: false, allowNull: true, requiredComponents: []);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var target = CreateInternalTarget(targetEntityId, SceneEntitySelectorScope.InstanceAbsolute);
        var placement = CreatePlacement(
            childPrefab.AssetId,
            [new ScenePlacementSlotBinding(childSlotId, target)]);

        var resolved = Resolve(
            placement,
            childPrefab,
            [],
            _ => ImmutableArray<ContentSchemaReference>.Empty,
            allowInstanceAbsoluteInternalTargets: true);

        Assert.That(resolved[0].Target?.Entity?.AuthoredEntityId, Is.EqualTo(targetEntityId));
    }

    [Test]
    public void ResolvesInternalTargetWithRequiredComponents()
    {
        var childSlotId = new Guid("30000000-0000-4000-8000-000000000057");
        var targetEntityId = new Guid("40000000-0000-4000-8000-000000000057");
        var cameraSchema = CreateSchemaReference("scene/camera");
        var childSlot = CreateSlot(childSlotId, required: false, allowNull: true, [cameraSchema]);
        var childPrefab = CreatePrefabAsset(new AssetId(1), [childSlot]);
        var target = CreateInternalTarget(targetEntityId, SceneEntitySelectorScope.OccurrenceRelative);
        var placement = CreatePlacement(
            childPrefab.AssetId,
            [new ScenePlacementSlotBinding(childSlotId, target)]);

        var resolved = Resolve(
            placement,
            childPrefab,
            [],
            _ => ImmutableArray.Create(cameraSchema),
            allowInstanceAbsoluteInternalTargets: false);

        Assert.That(resolved[0].Target?.Entity?.AuthoredEntityId, Is.EqualTo(targetEntityId));
    }
}
