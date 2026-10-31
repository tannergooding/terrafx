// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Scenes;

internal sealed class SceneHierarchyValidationWorkspace
{
    private readonly ushort[] _depths;
    private readonly Guid[] _entityIds;
    private readonly byte[] _entityMembershipCounts;
    private readonly int[] _parentIndices;
    private readonly byte[] _placementMembershipCounts;
    private readonly SceneHierarchyPlacement[] _placements;
    private readonly int[] _traversalPath;
    private readonly byte[] _traversalStates;

    internal SceneHierarchyValidationWorkspace(int entityCount, int placementCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);
        ArgumentOutOfRangeException.ThrowIfNegative(placementCount);

        EntityCount = entityCount;
        PlacementCount = placementCount;
        ByteReservation = GetRequiredByteReservation(entityCount, placementCount);
        _entityIds = new Guid[entityCount];
        _placements = new SceneHierarchyPlacement[placementCount];
        _entityMembershipCounts = new byte[entityCount];
        _placementMembershipCounts = new byte[placementCount];
        _parentIndices = new int[entityCount];
        _traversalStates = new byte[entityCount];
        _depths = new ushort[entityCount];
        _traversalPath = new int[entityCount];
    }

    internal long ByteReservation { get; }

    internal Span<ushort> Depths => _depths;

    internal int EntityCount { get; }

    internal Span<Guid> EntityIds => _entityIds;

    internal Span<byte> EntityMembershipCounts => _entityMembershipCounts;

    internal Span<int> ParentIndices => _parentIndices;

    internal int PlacementCount { get; }

    internal Span<byte> PlacementMembershipCounts => _placementMembershipCounts;

    internal Span<SceneHierarchyPlacement> Placements => _placements;

    internal Span<int> TraversalPath => _traversalPath;

    internal Span<byte> TraversalStates => _traversalStates;

    internal static long GetRequiredByteReservation(int entityCount, int placementCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entityCount);
        ArgumentOutOfRangeException.ThrowIfNegative(placementCount);

        return checked(
            64L
            + ArrayBytes(entityCount, Unsafe.SizeOf<Guid>())
            + ArrayBytes(placementCount, Unsafe.SizeOf<SceneHierarchyPlacement>())
            + ArrayBytes(entityCount, sizeof(byte))
            + ArrayBytes(placementCount, sizeof(byte))
            + ArrayBytes(entityCount, sizeof(int))
            + ArrayBytes(entityCount, sizeof(byte))
            + ArrayBytes(entityCount, sizeof(ushort))
            + ArrayBytes(entityCount, sizeof(int)));
    }

    private static long ArrayBytes(int count, int elementSize)
    {
        var payloadBytes = checked((long)count * elementSize);
        return checked(64L + ((payloadBytes + 7L) & ~7L));
    }

    internal void Reset()
    {
        Array.Clear(_entityMembershipCounts);
        Array.Clear(_placementMembershipCounts);
        Array.Fill(_parentIndices, -1);
        Array.Clear(_traversalStates);
        Array.Clear(_depths);
    }
}
