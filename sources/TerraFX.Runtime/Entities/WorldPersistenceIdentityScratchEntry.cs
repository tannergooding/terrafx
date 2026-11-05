// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;

namespace TerraFX.Entities;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WorldPersistenceIdentityScratchEntry : IComparable<WorldPersistenceIdentityScratchEntry>
{
    private const byte SceneControlActivatedFlag = 1;
    private const byte SceneControlActiveFlag = 2;

    private readonly uint _assignmentOrdinal;
    private readonly ushort _branchIndex;
    private readonly Guid _identity;
    private readonly Guid _sourceIdentityNamespace;
    private readonly byte _kind;
    private readonly byte _origin;
    private int _sceneControlSourceLength;
    private int _sceneControlSourceOffset;
    private byte _sceneControlState;

    internal WorldPersistenceIdentityScratchEntry(
        Guid identity,
        byte kind,
        uint assignmentOrdinal,
        ushort branchIndex,
        Guid sourceIdentityNamespace,
        byte origin,
        bool isLive = false)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(identity, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(kind, (byte)2);
        ArgumentOutOfRangeException.ThrowIfZero(kind);
        ArgumentOutOfRangeException.ThrowIfZero(assignmentOrdinal);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(branchIndex, 1023);
        ArgumentOutOfRangeException.ThrowIfEqual(sourceIdentityNamespace, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfZero(origin);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(origin, (byte)3);
        _identity = identity;
        _kind = (byte)(kind | (isLive ? 0x80 : 0));
        _assignmentOrdinal = assignmentOrdinal;
        _branchIndex = branchIndex;
        _sourceIdentityNamespace = sourceIdentityNamespace;
        _origin = origin;
        _sceneControlSourceLength = 0;
        _sceneControlSourceOffset = -1;
        _sceneControlState = 0;
    }

    internal readonly uint AssignmentOrdinal => _assignmentOrdinal;

    internal readonly ushort BranchIndex => _branchIndex;

    internal readonly bool HasSceneControlActivation => (_sceneControlState & SceneControlActivatedFlag) != 0;

    internal readonly bool IsLive => (_kind & 0x80) != 0;

    internal readonly bool IsSceneControlActive => (_sceneControlState & SceneControlActiveFlag) != 0;

    internal readonly byte Kind => (byte)(_kind & 0x7F);

    internal readonly byte Origin => _origin;

    internal readonly int SceneControlSourceLength => _sceneControlSourceLength;

    internal readonly int SceneControlSourceOffset => _sceneControlSourceOffset;

    internal readonly Guid SourceIdentityNamespace => _sourceIdentityNamespace;

    internal readonly Guid StableId => _identity;

    internal static bool ContainsEntity(scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> entries, Guid identity)
        => ContainsKind(entries, identity, kind: 1);

    internal static bool ContainsKind(
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> entries,
        Guid identity,
        byte kind)
    {
        var index = FindIndex(entries, identity);
        return (index >= 0) && (entries[index].Kind == kind);
    }

    internal static int FindIndex(
        scoped ReadOnlySpan<WorldPersistenceIdentityScratchEntry> entries,
        Guid identity)
    {
        var low = 0;
        var high = entries.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var entry = entries[middle];
            var comparison = entry.StableId.CompareTo(identity);

            if (comparison == 0)
            {
                return middle;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }

    public readonly int CompareTo(WorldPersistenceIdentityScratchEntry other)
        => _identity.CompareTo(other._identity);

    internal bool TryMarkSceneControlActive(int sourceOffset, int sourceLength)
    {
        if ((Kind != 2)
            || ((_sceneControlState & SceneControlActivatedFlag) != 0)
            || (sourceOffset < 0)
            || (sourceLength <= 0))
        {
            return false;
        }

        _sceneControlState = SceneControlActivatedFlag | SceneControlActiveFlag;
        _sceneControlSourceOffset = sourceOffset;
        _sceneControlSourceLength = sourceLength;
        return true;
    }

    internal bool TryMarkSceneControlUnloaded()
    {
        if ((Kind != 2) || ((_sceneControlState & SceneControlActiveFlag) == 0))
        {
            return false;
        }

        _sceneControlState &= unchecked((byte)~SceneControlActiveFlag);
        return true;
    }
}
