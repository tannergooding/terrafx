// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Scenes;

internal static class SceneHierarchyTransactionPlanner
{
    internal static bool TryBuild(
        World world,
        SceneWorldRegistry registry,
        SceneInstance instance,
        Guid sceneInstanceId,
        uint firstEntityIdentityOrdinal,
        int entityCount,
        ImmutableArray<SceneTransactionOperation> operations,
        out SceneHierarchyTransactionPlan? plan,
        out string? failure)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(instance);

        try
        {
            if (operations.IsDefault)
            {
                throw new ArgumentException("The Scene transaction operation array must be initialized.", nameof(operations));
            }

            registry.EnsureBoundTo(world);

            if (!registry.ContainsInstance(instance)
                || (instance.SceneInstanceId != sceneInstanceId)
                || (instance.FirstEntityIdentityOrdinal != firstEntityIdentityOrdinal)
                || (instance.EntityCount != entityCount)
                || (sceneInstanceId == Guid.Empty)
                || (entityCount < 0))
            {
                throw new InvalidOperationException("The Scene transaction instance is no longer active in its exact World.");
            }

            var shadow = new Shadow(world, firstEntityIdentityOrdinal, entityCount);

            for (var index = 0; index < operations.Length; index++)
            {
                shadow.Apply(operations[index], index);
            }

            plan = shadow.CreatePlan(world.CanonicalMutationRevision);
            failure = null;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            plan = null;
            failure = exception.Message;
            return false;
        }
    }

    private sealed class NodeState
    {
        internal NodeState(
            Entity entity,
            Entity? parent,
            SceneComponentDefinitions.LocalTransformComponent localTransform,
            SceneComponentDefinitions.RootPositionComponent? rootPosition)
        {
            Entity = entity;
            Parent = parent;
            OriginalParent = parent;
            LocalTransform = localTransform;
            OriginalLocalTransform = localTransform;
            RootPosition = rootPosition;
            OriginalRootPosition = rootPosition;
            LastParentOperationOrder = -1;
        }

        internal Entity Entity { get; }

        internal int LastParentOperationOrder { get; set; }

        internal SceneComponentDefinitions.LocalTransformComponent LocalTransform { get; set; }

        internal SceneComponentDefinitions.LocalTransformComponent OriginalLocalTransform { get; }

        internal Entity? OriginalParent { get; }

        internal SceneComponentDefinitions.RootPositionComponent? OriginalRootPosition { get; }

        internal Entity? Parent { get; set; }

        internal SceneComponentDefinitions.RootPositionComponent? RootPosition { get; set; }
    }

    private sealed class Shadow
    {
        private readonly Dictionary<Entity, List<Entity>> _addedChildren;
        private readonly SpatialFrame3 _frame;
        private readonly uint _instanceFirstEntityIdentityOrdinal;
        private readonly uint _instanceLastEntityIdentityOrdinalExclusive;
        private readonly SpatialSettings _spatialSettings;
        private readonly Dictionary<Entity, NodeState> _states;
        private readonly HashSet<Entity> _traversalPath;
        private readonly World _world;

        internal Shadow(World world, uint firstEntityIdentityOrdinal, int entityCount)
        {
            _world = world;
            _instanceFirstEntityIdentityOrdinal = firstEntityIdentityOrdinal;
            _instanceLastEntityIdentityOrdinalExclusive = checked(
                _instanceFirstEntityIdentityOrdinal + (uint)entityCount);
            _frame = world.CurrentFrame3;
            _spatialSettings = world.SpatialSettings;
            _addedChildren = [];
            _states = [];
            _traversalPath = [];
        }

        private static bool AreEqual(
            SceneComponentDefinitions.LocalTransformComponent left,
            SceneComponentDefinitions.LocalTransformComponent right)
            => (left.Position == right.Position)
            && (left.Rotation == right.Rotation)
            && (left.Scale == right.Scale);

        private static void DecomposeLocalTransform(
            Matrix4x4 linearTransform,
            Vector3 localPosition,
            out SceneComponentDefinitions.LocalTransformComponent localTransform)
        {
            if (!Matrix4x4.TryDecompose(linearTransform, out var scale, out var rotation, out var translation)
                || (translation != Vector3.Zero))
            {
                throw new InvalidOperationException("The Scene reparent operation has no admitted local SRT.");
            }

            localTransform = SceneComponentDefinitions.CreateAssignedLocalTransform(
                localPosition,
                rotation,
                scale);
        }

        private static bool HasAdmittedDisplacementResidual(Vector3 original, Vector3 recomposed)
        {
            if (!original.IsFinite().All() || !recomposed.IsFinite().All())
            {
                return false;
            }

            var maximumOriginal = MathF.Max(
                float.Abs(original.X),
                MathF.Max(float.Abs(original.Y), float.Abs(original.Z)));
            var maximumResidual = MathF.Max(
                float.Abs(original.X - recomposed.X),
                MathF.Max(
                    float.Abs(original.Y - recomposed.Y),
                    float.Abs(original.Z - recomposed.Z)));
            var tolerance = 1.0e-5f * MathF.Max(1.0f, maximumOriginal);
            return maximumResidual <= tolerance;
        }

        internal void Apply(SceneTransactionOperation operation, int operationOrder)
        {
            switch (operation.Kind)
            {
                case SceneTransactionOperationKind.Reparent:
                {
                    ApplyReparent(operation, operationOrder);
                    break;
                }

                case SceneTransactionOperationKind.SetRootPosition:
                {
                    ApplySetRootPosition(operation);
                    break;
                }

                default:
                {
                    throw new ArgumentOutOfRangeException(nameof(operation));
                }
            }
        }

        internal SceneHierarchyTransactionPlan CreatePlan(long baseStructuralRevision)
        {
            var componentMutations = ImmutableArray.CreateBuilder<WorldHierarchyComponentMutation>();
            var parentMutations = ImmutableArray.CreateBuilder<WorldHierarchyParentMutation>();

            foreach (var state in _states.Values)
            {
                if (!AreEqual(state.LocalTransform, state.OriginalLocalTransform))
                {
                    var localTransform = state.LocalTransform;
                    componentMutations.Add(new WorldHierarchyComponentMutation(
                        state.Entity,
                        typeof(SceneComponentDefinitions.LocalTransformComponent),
                        WorldHierarchyComponentMutationKind.Set,
                        ComponentInitialization.Value(in localTransform)));
                }

                if (state.OriginalRootPosition is not { } originalRootPosition)
                {
                    if (state.RootPosition is { } addedRootPosition)
                    {
                        var rootPosition = addedRootPosition;
                        componentMutations.Add(new WorldHierarchyComponentMutation(
                            state.Entity,
                            typeof(SceneComponentDefinitions.RootPositionComponent),
                            WorldHierarchyComponentMutationKind.Add,
                            ComponentInitialization.Value(in rootPosition)));
                    }
                }
                else if (state.RootPosition is not { } currentRootPosition)
                {
                    componentMutations.Add(new WorldHierarchyComponentMutation(
                        state.Entity,
                        typeof(SceneComponentDefinitions.RootPositionComponent),
                        WorldHierarchyComponentMutationKind.Remove,
                        null));
                }
                else if (originalRootPosition.Position != currentRootPosition.Position)
                {
                    var rootPosition = currentRootPosition;
                    componentMutations.Add(new WorldHierarchyComponentMutation(
                        state.Entity,
                        typeof(SceneComponentDefinitions.RootPositionComponent),
                        WorldHierarchyComponentMutationKind.Set,
                        ComponentInitialization.Value(in rootPosition)));
                }

                if (state.Parent != state.OriginalParent)
                {
                    var finalDepth = GetDepth(state.Entity);
                    var finalSubtreeHeight = GetSubtreeHeight(state.Entity);

                    parentMutations.Add(new WorldHierarchyParentMutation(
                        state.Entity,
                        state.Parent,
                        finalDepth,
                        finalSubtreeHeight,
                        state.LastParentOperationOrder));
                }
            }

            var componentChanges = componentMutations.ToArray();
            Array.Sort(componentChanges, static (left, right) => {
                var comparison = left.Entity.Index.CompareTo(right.Entity.Index);

                if (comparison == 0)
                {
                    var leftOrder = left.ComponentType == typeof(SceneComponentDefinitions.LocalTransformComponent) ? 0 : 1;
                    var rightOrder = right.ComponentType == typeof(SceneComponentDefinitions.LocalTransformComponent) ? 0 : 1;
                    comparison = leftOrder.CompareTo(rightOrder);
                }

                return comparison;
            });

            var parentChanges = parentMutations.ToArray();
            Array.Sort(parentChanges, static (left, right) => {
                var comparison = left.FinalDepth.CompareTo(right.FinalDepth);

                if (comparison == 0)
                {
                    comparison = left.OperationOrder.CompareTo(right.OperationOrder);
                }

                if (comparison == 0)
                {
                    comparison = left.Child.Index.CompareTo(right.Child.Index);
                }

                return comparison;
            });

            return new SceneHierarchyTransactionPlan(
                baseStructuralRevision,
                [.. componentChanges],
                [.. parentChanges]);
        }

        private void ApplyKeepWorld(
            NodeState child,
            (SpatialPosition3 Position, Matrix4x4 LinearTransform) childWorldPose,
            (SpatialPosition3 Position, Matrix4x4 LinearTransform) newParentWorldPose,
            bool newIsRoot)
        {
            if (newIsRoot)
            {
                DecomposeLocalTransform(
                    childWorldPose.LinearTransform,
                    Vector3.Zero,
                    out var localTransform);
                child.LocalTransform = localTransform;
                child.RootPosition = new SceneComponentDefinitions.RootPositionComponent(childWorldPose.Position);
                return;
            }

            var localPosition = GetParentLocalDisplacement(
                newParentWorldPose,
                childWorldPose.Position,
                out var originalWorldDisplacement,
                out var inverseParent);
            var relativeLinearTransform = childWorldPose.LinearTransform * inverseParent;
            DecomposeLocalTransform(relativeLinearTransform, localPosition, out var reparentedLocal);

            var recomposedWorldDisplacement = Vector3.TransformDirection(
                reparentedLocal.Position,
                newParentWorldPose.LinearTransform);

            if (!HasAdmittedDisplacementResidual(originalWorldDisplacement, recomposedWorldDisplacement))
            {
                throw new InvalidOperationException("The Scene reparent operation exceeds the admitted spatial-position recomposition residual.");
            }

            child.LocalTransform = reparentedLocal;
            child.RootPosition = null;
        }

        private void ApplyReparent(SceneTransactionOperation operation, int operationOrder)
        {
            var child = operation.Entity;

            if (!IsInstanceMember(child))
            {
                throw new InvalidOperationException("A Scene transaction child is not a live member of this exact SceneInstance.");
            }

            if (operation.Mode is not ReparentMode.KeepLocal and not ReparentMode.KeepWorld)
            {
                throw new ArgumentOutOfRangeException(nameof(operation));
            }

            var childState = GetState(child);
            var newParent = operation.Parent;

            if (newParent is Entity parentEntity)
            {
                if (!IsLiveInWorld(parentEntity))
                {
                    throw new InvalidOperationException("A Scene transaction parent is not a live Entity in this exact World.");
                }

                if (parentEntity == child)
                {
                    throw new InvalidOperationException("A Scene transaction Entity cannot parent itself.");
                }

                _ = GetState(parentEntity);
            }

            if (childState.Parent == newParent)
            {
                return;
            }

            var oldWasRoot = childState.Parent is null;
            var newIsRoot = newParent is null;

            if (operation.Mode == ReparentMode.KeepLocal)
            {
                if (oldWasRoot != newIsRoot)
                {
                    var childPosition = GetWorldPose(child).Position;

                    if (newIsRoot)
                    {
                        childState.LocalTransform = new SceneComponentDefinitions.LocalTransformComponent(
                            Vector3.Zero,
                            childState.LocalTransform.Rotation,
                            childState.LocalTransform.Scale);
                        childState.RootPosition = new SceneComponentDefinitions.RootPositionComponent(childPosition);
                    }
                    else
                    {
                        var selectedParent = newParent
                            ?? throw new InvalidOperationException("The selected non-root endpoint requires a parent Entity.");
                        var newParentPose = GetWorldPose(selectedParent);
                        var localPosition = GetParentLocalDisplacement(
                            newParentPose,
                            childPosition,
                            out _,
                            out _);
                        childState.LocalTransform = new SceneComponentDefinitions.LocalTransformComponent(
                            localPosition,
                            childState.LocalTransform.Rotation,
                            childState.LocalTransform.Scale);
                        childState.RootPosition = null;
                    }
                }
            }
            else
            {
                var childWorldPose = GetWorldPose(child);
                var newParentWorldPose = newParent is Entity selectedParent
                    ? GetWorldPose(selectedParent)
                    : default;
                ApplyKeepWorld(childState, childWorldPose, newParentWorldPose, newIsRoot);
            }

            UpdateParent(childState, newParent, operationOrder);
            ValidateMovedSubtree(child);
        }

        private void ApplySetRootPosition(SceneTransactionOperation operation)
        {
            if (!IsInstanceMember(operation.Entity))
            {
                throw new InvalidOperationException("A SetRootPosition target is not a live member of this exact SceneInstance.");
            }

            var state = GetState(operation.Entity);

            if (state.Parent.HasValue || (state.RootPosition is null))
            {
                throw new InvalidOperationException("SetRootPosition requires a Scene root in the sequential transaction state.");
            }

            if (state.LocalTransform.Position != Vector3.Zero)
            {
                throw new InvalidOperationException("A Scene root LocalTransform must have zero translation.");
            }

            var position = SpatialMath.Normalize(operation.RootPosition, _spatialSettings);

            if (!SpatialMath.TryToFrameRelative(position, _frame, out _))
            {
                throw new InvalidOperationException("The requested RootPosition is outside the current World frame.");
            }

            state.RootPosition = new SceneComponentDefinitions.RootPositionComponent(position);
        }

        private ImmutableArray<Entity> GetChildren(Entity parent)
        {
            var children = ImmutableArray.CreateBuilder<Entity>();
            var seenChildren = new HashSet<Entity>();

            if (_world.TryGetFirstHierarchyChild(parent, out var child))
            {
                while (true)
                {
                    if (GetParent(child) == parent && seenChildren.Add(child))
                    {
                        children.Add(child);
                    }

                    if (!_world.TryGetNextHierarchySibling(child, out child))
                    {
                        break;
                    }
                }
            }

            if (_addedChildren.TryGetValue(parent, out var addedChildren))
            {
                foreach (var addedChild in addedChildren)
                {
                    if (GetParent(addedChild) == parent && seenChildren.Add(addedChild))
                    {
                        children.Add(addedChild);
                    }
                }
            }

            return children.ToImmutable();
        }

        private int GetDepth(Entity entity)
        {
            var depth = 0;
            _traversalPath.Clear();
            var current = entity;

            while (true)
            {
                if (!_traversalPath.Add(current))
                {
                    throw new InvalidOperationException("The Scene hierarchy transaction would create a cycle.");
                }

                if (depth > EntitySlotTable.MaximumHierarchyDepth)
                {
                    throw new InvalidOperationException("The Scene hierarchy transaction exceeds the World depth bound.");
                }

                if (GetParent(current) is not Entity parent)
                {
                    return depth;
                }

                current = parent;
                depth++;
            }
        }

        private Entity? GetParent(Entity entity)
        {
            if (_states.TryGetValue(entity, out var state))
            {
                return state.Parent;
            }

            return _world.TryGetHierarchyParent(entity, out var parent)
                ? parent
                : null;
        }

        private Vector3 GetParentLocalDisplacement(
            (SpatialPosition3 Position, Matrix4x4 LinearTransform) parentPose,
            SpatialPosition3 childPosition,
            out Vector3 worldDisplacement,
            out Matrix4x4 inverseParent)
        {
            if (!Matrix4x4.TryInvert(parentPose.LinearTransform, out inverseParent)
                || !SpatialMath.TryGetDisplacement(
                    parentPose.Position,
                    childPosition,
                    _spatialSettings,
                    float.MaxValue,
                    out worldDisplacement))
            {
                throw new InvalidOperationException("A Scene hierarchy translation cannot be represented relative to its new parent.");
            }

            var localPosition = Vector3.TransformDirection(worldDisplacement, inverseParent);

            if (!localPosition.IsFinite().All())
            {
                throw new InvalidOperationException("A Scene hierarchy local translation is not finite.");
            }

            return localPosition;
        }

        private NodeState GetState(Entity entity)
        {
            if (_states.TryGetValue(entity, out var state))
            {
                return state;
            }

            if (!IsLiveInWorld(entity))
            {
                throw new InvalidOperationException("A Scene hierarchy operation targets a stale or foreign Entity.");
            }

            Entity? parent = _world.TryGetHierarchyParent(entity, out var currentParent)
                ? currentParent
                : null;

            if (!_world.TryGet<SceneComponentDefinitions.LocalTransformComponent>(entity, out var localTransform))
            {
                throw new InvalidOperationException("A Scene hierarchy Entity has no LocalTransform component.");
            }

            _ = SceneComponentDefinitions.CreateAssignedLocalTransform(
                localTransform.Position,
                localTransform.Rotation,
                localTransform.Scale);
            SceneComponentDefinitions.RootPositionComponent? rootPosition = _world.TryGet<
                SceneComponentDefinitions.RootPositionComponent>(entity, out var currentRootPosition)
                ? currentRootPosition
                : null;

            if ((parent is null) != rootPosition.HasValue)
            {
                throw new InvalidOperationException("A Scene hierarchy Entity has inconsistent root and RootPosition state.");
            }

            if (parent is null && (localTransform.Position != Vector3.Zero))
            {
                throw new InvalidOperationException("A Scene root LocalTransform must have zero translation.");
            }

            state = new NodeState(entity, parent, localTransform, rootPosition);
            _states.Add(entity, state);
            return state;
        }

        private int GetSubtreeHeight(Entity entity)
        {
            _traversalPath.Clear();
            return GetSubtreeHeight(entity, 0);
        }

        private int GetSubtreeHeight(Entity entity, int depth)
        {
            if (depth > EntitySlotTable.MaximumHierarchyDepth)
            {
                throw new InvalidOperationException("The Scene hierarchy transaction exceeds the World depth bound.");
            }

            if (!_traversalPath.Add(entity))
            {
                throw new InvalidOperationException("The Scene hierarchy transaction would create a cycle.");
            }

            try
            {
                var height = 0;

                foreach (var child in GetChildren(entity))
                {
                    height = Math.Max(height, checked(GetSubtreeHeight(child, depth + 1) + 1));
                }

                return height;
            }
            finally
            {
                _ = _traversalPath.Remove(entity);
            }
        }

        private (SpatialPosition3 Position, Matrix4x4 LinearTransform) GetWorldPose(Entity entity)
        {
            _traversalPath.Clear();
            return GetWorldPose(entity, depth: 0);
        }

        private (SpatialPosition3 Position, Matrix4x4 LinearTransform) GetWorldPose(Entity entity, int depth)
        {
            if (depth > EntitySlotTable.MaximumHierarchyDepth)
            {
                throw new InvalidOperationException("The Scene transform ancestor path exceeds the World depth bound.");
            }

            if (!_traversalPath.Add(entity))
            {
                throw new InvalidOperationException("The Scene transform parent chain contains a cycle.");
            }

            try
            {
                var state = GetState(entity);
                var localLinearTransform = SceneHierarchyTransform.CreateLinearTransform(state.LocalTransform);
                SpatialPosition3 position;
                Matrix4x4 linearTransform;

                if (state.Parent is not Entity parent)
                {
                    if (state.RootPosition is not { } rootPosition
                        || (state.LocalTransform.Position != Vector3.Zero))
                    {
                        throw new InvalidOperationException("A Scene root has no valid RootPosition or nonzero LocalTransform translation.");
                    }

                    position = SpatialMath.Normalize(rootPosition.Position, _spatialSettings);
                    linearTransform = localLinearTransform;
                }
                else
                {
                    var (parentPosition, parentLinearTransform) = GetWorldPose(parent, checked(depth + 1));
                    var displacement = Vector3.TransformDirection(
                        state.LocalTransform.Position,
                        parentLinearTransform);
                    position = SceneHierarchyTransform.Translate(
                        parentPosition,
                        displacement,
                        _spatialSettings);
                    linearTransform = localLinearTransform * parentLinearTransform;
                }

                if (!SpatialMath.TryToFrameRelative(position, _frame, out _)
                    || !linearTransform.IsFinite().All())
                {
                    throw new InvalidOperationException("A Scene hierarchy world pose is outside the current frame or nonfinite.");
                }

                return (position, linearTransform);
            }
            finally
            {
                _ = _traversalPath.Remove(entity);
            }
        }

        private bool IsInstanceMember(Entity entity)
        {
            if (!IsLiveInWorld(entity))
            {
                return false;
            }

            var identityOrdinal = _world.IdentityLedger.GetLiveOrdinal(IdentityKind.Entity, entity.Index);
            return (identityOrdinal >= _instanceFirstEntityIdentityOrdinal)
                && (identityOrdinal < _instanceLastEntityIdentityOrdinalExclusive);
        }

        private bool IsLiveInWorld(Entity entity)
        {
            if (!entity.IsValid
                || (entity.OwnerId != _world.OwnerId)
                || !_world.TryGetLiveEntityIdentityOrdinal(entity, out _))
            {
                return false;
            }

            return true;
        }

        private void UpdateParent(NodeState state, Entity? parent, int operationOrder)
        {
            if (state.Parent is Entity oldParent
                && _addedChildren.TryGetValue(oldParent, out var oldAddedChildren))
            {
                _ = oldAddedChildren.Remove(state.Entity);
            }

            state.Parent = parent;
            state.LastParentOperationOrder = operationOrder;

            if ((parent is Entity newParent) && (newParent != state.OriginalParent))
            {
                if (!_addedChildren.TryGetValue(newParent, out var newAddedChildren))
                {
                    newAddedChildren = [];
                    _addedChildren.Add(newParent, newAddedChildren);
                }

                newAddedChildren.Add(state.Entity);
            }
        }

        private void ValidateMovedSubtree(Entity entity)
        {
            var depth = GetDepth(entity);
            _traversalPath.Clear();
            ValidateSubtree(entity, depth);
        }

        private void ValidateSubtree(Entity entity, int depth)
        {
            if (depth > EntitySlotTable.MaximumHierarchyDepth)
            {
                throw new InvalidOperationException("The Scene hierarchy transaction exceeds the World depth bound.");
            }

            if (!_traversalPath.Add(entity))
            {
                throw new InvalidOperationException("The Scene hierarchy transaction would create a cycle.");
            }

            try
            {
                foreach (var child in GetChildren(entity))
                {
                    ValidateSubtree(child, checked(depth + 1));
                }
            }
            finally
            {
                _ = _traversalPath.Remove(entity);
            }
        }
    }
}
