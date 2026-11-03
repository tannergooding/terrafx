// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Scenes;

internal readonly struct ScenePersistenceWorldHierarchyNodeArrayWorkspaceRequirements
{
    internal ScenePersistenceWorldHierarchyNodeArrayWorkspaceRequirements(
        int nodeCount,
        int childCount,
        int nodePayloadBytes,
        int recordStorageBytes,
        int maximumCandidateBytes,
        int arrayPayloadBytes)
    {
        ArrayPayloadBytes = arrayPayloadBytes;
        ChildCount = childCount;
        MaximumCandidateBytes = maximumCandidateBytes;
        NodeCount = nodeCount;
        NodePayloadBytes = nodePayloadBytes;
        RecordStorageBytes = recordStorageBytes;
        WorkspaceBytes = checked(
            ((long)nodeCount * Unsafe.SizeOf<EbinPayloadSlice>())
            + ((long)nodeCount * Unsafe.SizeOf<SceneWorldHierarchyCaptureRow>())
            + ((long)childCount * Unsafe.SizeOf<Guid>())
            + nodePayloadBytes
            + recordStorageBytes
            + maximumCandidateBytes
            + arrayPayloadBytes);
    }

    internal int ArrayPayloadBytes { get; }

    internal int ChildCount { get; }

    internal int MaximumCandidateBytes { get; }

    internal int NodeCount { get; }

    internal int NodePayloadBytes { get; }

    internal int RecordStorageBytes { get; }

    internal long WorkspaceBytes { get; }

    internal bool Matches(ScenePersistenceWorldHierarchyNodeArrayWorkspaceRequirements other)
        => (ArrayPayloadBytes == other.ArrayPayloadBytes)
        && (ChildCount == other.ChildCount)
        && (MaximumCandidateBytes == other.MaximumCandidateBytes)
        && (NodeCount == other.NodeCount)
        && (NodePayloadBytes == other.NodePayloadBytes)
        && (RecordStorageBytes == other.RecordStorageBytes)
        && (WorkspaceBytes == other.WorkspaceBytes);
}
