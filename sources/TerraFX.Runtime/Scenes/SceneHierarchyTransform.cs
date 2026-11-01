// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Scenes;

internal static class SceneHierarchyTransform
{
    internal static Matrix4x4 CreateLinearTransform(SceneComponentDefinitions.LocalTransformComponent value)
    {
        var rotation = Quaternion.Normalize(value.Rotation);
        return Matrix4x4.CreateScale(value.Scale) * Matrix4x4.CreateFromQuaternion(rotation);
    }

    internal static ImmutableArray<WorldHierarchyDetachUpdate> PrepareHierarchyDetachment(
        World world,
        SpatialFrame3 frame,
        int ancestorNodeCount,
        ReadOnlyMemory<WorldHierarchyNodeSnapshot> nodeMemory)
    {
        ArgumentNullException.ThrowIfNull(world);
        var nodes = nodeMemory.Span;

        if (ancestorNodeCount <= 0
            || (ancestorNodeCount > (EntitySlotTable.MaximumHierarchyDepth + 1))
            || nodeMemory.IsEmpty
            || (ancestorNodeCount > nodes.Length))
        {
            throw new ArgumentOutOfRangeException(nameof(ancestorNodeCount));
        }

        var childCount = nodes.Length - ancestorNodeCount;

        if (childCount == 0)
        {
            return [];
        }

        Span<SpatialPosition3> worldPositions = stackalloc SpatialPosition3[EntitySlotTable.MaximumHierarchyDepth + 1];
        Span<Matrix4x4> worldLinearTransforms = stackalloc Matrix4x4[EntitySlotTable.MaximumHierarchyDepth + 1];
        var settings = world.SpatialSettings;

        for (var index = 0; index < ancestorNodeCount; index++)
        {
            var node = nodes[index];
            var localTransform = ReadLocalTransform(node.LocalTransform, node.Entity);
            var localLinearTransform = CreateLinearTransform(localTransform);

            if (index == 0)
            {
                if (node.Parent.HasValue
                    || (node.RootPosition is not ComponentInitialization rootPositionInitialization)
                    || !rootPositionInitialization.TryGetValue<SceneComponentDefinitions.RootPositionComponent>(out var rootPosition)
                    || (localTransform.Position != Vector3.Zero))
                {
                    throw new InvalidOperationException("The ancestor path does not begin at a valid Scene root.");
                }

                worldPositions[index] = SpatialMath.Normalize(rootPosition.Position, settings);

                if (!SpatialMath.TryToFrameRelative(worldPositions[index], frame, out _))
                {
                    throw new InvalidOperationException("A Scene hierarchy root is outside the admitted frame.");
                }

                worldLinearTransforms[index] = localLinearTransform;
            }
            else
            {
                if ((node.Parent != nodes[index - 1].Entity)
                    || (node.RootPosition is not null))
                {
                    throw new InvalidOperationException("The ancestor path does not follow canonical Scene parent links.");
                }

                var parentPosition = worldPositions[index - 1];
                var parentLinearTransform = worldLinearTransforms[index - 1];
                var displacement = Vector3.TransformDirection(localTransform.Position, parentLinearTransform);
                worldPositions[index] = Translate(parentPosition, displacement, settings);

                if (!SpatialMath.TryToFrameRelative(worldPositions[index], frame, out _))
                {
                    throw new InvalidOperationException("A Scene hierarchy position is outside the admitted frame.");
                }

                worldLinearTransforms[index] = localLinearTransform * parentLinearTransform;
            }

            if (!worldLinearTransforms[index].IsFinite().All())
            {
                throw new InvalidOperationException("A Scene hierarchy transform is not finite.");
            }
        }

        var parentNode = nodes[ancestorNodeCount - 1];
        var parentPositionForChildren = worldPositions[ancestorNodeCount - 1];
        var parentLinearForChildren = worldLinearTransforms[ancestorNodeCount - 1];
        var updates = ImmutableArray.CreateBuilder<WorldHierarchyDetachUpdate>(childCount);

        for (var childIndex = 0; childIndex < childCount; childIndex++)
        {
            var node = nodes[ancestorNodeCount + childIndex];

            if ((node.Parent != parentNode.Entity) || (node.RootPosition is not null))
            {
                throw new InvalidOperationException("A KeepWorld detach input is not a direct non-root child.");
            }

            var localTransform = ReadLocalTransform(node.LocalTransform, node.Entity);
            var worldPosition = Translate(
                parentPositionForChildren,
                Vector3.TransformDirection(localTransform.Position, parentLinearForChildren),
                settings);

            if (!SpatialMath.TryToFrameRelative(worldPosition, frame, out _))
            {
                throw new InvalidOperationException("A detached Scene child is outside the admitted frame.");
            }

            var worldLinearTransform = CreateLinearTransform(localTransform) * parentLinearForChildren;

            if (!Matrix4x4.TryDecompose(worldLinearTransform, out var scale, out var rotation, out var translation)
                || (translation != Vector3.Zero))
            {
                throw new InvalidOperationException("A detached Scene child does not have an admitted root SRT.");
            }

            var detachedLocalTransform = SceneComponentDefinitions.CreateAssignedLocalTransform(
                Vector3.Zero,
                rotation,
                scale);
            var rootPosition = new SceneComponentDefinitions.RootPositionComponent(worldPosition);
            var localInitialization = ComponentInitialization.Value(in detachedLocalTransform);
            var rootInitialization = ComponentInitialization.Value(in rootPosition);
            updates.Add(new WorldHierarchyDetachUpdate(node.Entity, localInitialization, rootInitialization));
        }

        return updates.MoveToImmutable();
    }

    internal static (SpatialPosition3 Position, Quaternion Rotation) ResolveCameraPose(
        World world,
        SpatialFrame3 frame,
        Entity cameraEntity)
    {
        var (position, _, rotation) = ResolveEntityTransform(world, frame, cameraEntity);
        return (position, rotation);
    }

    internal static (SpatialPosition3 Position, Matrix4x4 LinearTransform, Quaternion Rotation) ResolveEntityTransform(
        World world,
        SpatialFrame3 frame,
        Entity renderableEntity)
    {
        ArgumentNullException.ThrowIfNull(world);

        Span<Entity> ancestorPath = stackalloc Entity[EntitySlotTable.MaximumHierarchyDepth + 1];
        var ancestorCount = 0;
        var current = renderableEntity;

        while (true)
        {
            if (ancestorCount == ancestorPath.Length)
            {
                throw new InvalidOperationException("The renderable Entity ancestor path exceeds the World depth bound.");
            }

            for (var index = 0; index < ancestorCount; index++)
            {
                if (ancestorPath[index] == current)
                {
                    throw new InvalidOperationException("The renderable Entity ancestor path contains a cycle.");
                }
            }

            ancestorPath[ancestorCount++] = current;

            if (!world.TryGetHierarchyParent(current, out var parent))
            {
                break;
            }

            current = parent;
        }

        var settings = world.SpatialSettings;
        var worldPosition = default(SpatialPosition3);
        var worldLinearTransform = Matrix4x4.Identity;
        var worldRotationTransform = Matrix4x4.Identity;

        for (var index = ancestorCount - 1; index >= 0; index--)
        {
            var entity = ancestorPath[index];

            if (!world.TryGet<SceneComponentDefinitions.LocalTransformComponent>(entity, out var localTransform))
            {
                throw new InvalidOperationException("A renderable Entity or its ancestor has no LocalTransform component.");
            }

            _ = SceneComponentDefinitions.CreateAssignedLocalTransform(
                localTransform.Position,
                localTransform.Rotation,
                localTransform.Scale);

            if ((localTransform.Scale.X <= 0.0f)
                || (localTransform.Scale.Y <= 0.0f)
                || (localTransform.Scale.Z <= 0.0f))
            {
                throw new InvalidOperationException("Renderable Entity and ancestor scales must be positive.");
            }

            var localLinearTransform = CreateLinearTransform(localTransform);
            var localRotationTransform = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(localTransform.Rotation));

            if (index == (ancestorCount - 1))
            {
                if (!world.TryGet<SceneComponentDefinitions.RootPositionComponent>(entity, out var rootPosition)
                    || (localTransform.Position != Vector3.Zero))
                {
                    throw new InvalidOperationException("The renderable Entity ancestor path does not begin at a valid Scene root.");
                }

                worldPosition = SpatialMath.Normalize(rootPosition.Position, settings);
                worldLinearTransform = localLinearTransform;
                worldRotationTransform = localRotationTransform;
            }
            else
            {
                if (world.TryGet<SceneComponentDefinitions.RootPositionComponent>(entity, out _))
                {
                    throw new InvalidOperationException("A renderable Entity child cannot also carry RootPosition.");
                }

                var displacement = Vector3.TransformDirection(
                    localTransform.Position,
                    worldLinearTransform);
                worldPosition = Translate(worldPosition, displacement, settings);
                worldLinearTransform = localLinearTransform * worldLinearTransform;
                worldRotationTransform = localRotationTransform * worldRotationTransform;
            }

            if (!SpatialMath.TryToFrameRelative(worldPosition, frame, out _)
                || !worldLinearTransform.IsFinite().All()
                || !worldRotationTransform.IsFinite().All())
            {
                throw new InvalidOperationException("The renderable Entity world pose is outside the current frame or nonfinite.");
            }
        }

        if (!Matrix4x4.TryDecompose(worldRotationTransform, out _, out var worldRotation, out var translation)
            || (translation != Vector3.Zero))
        {
            throw new InvalidOperationException("The renderable Entity rigid orientation is not representable.");
        }

        return (worldPosition, worldLinearTransform, Quaternion.Normalize(worldRotation));
    }

    internal static SpatialPosition3 Translate(
        SpatialPosition3 position,
        Vector3 displacement,
        SpatialSettings settings)
    {
        if (!displacement.IsFinite().All())
        {
            throw new InvalidOperationException("A Scene hierarchy displacement is not finite.");
        }

        return SpatialMath.Normalize(
            new SpatialPosition3(position.Chunk, position.Offset + displacement),
            settings);
    }

    private static SceneComponentDefinitions.LocalTransformComponent ReadLocalTransform(
        ComponentInitialization initialization,
        Entity entity)
    {
        if (!initialization.TryGetValue<SceneComponentDefinitions.LocalTransformComponent>(out var value))
        {
            throw new InvalidOperationException($"Scene Entity {entity.Index} has no LocalTransform value for hierarchy resolution.");
        }

        _ = SceneComponentDefinitions.CreateAssignedLocalTransform(value.Position, value.Rotation, value.Scale);
        return value;
    }
}
