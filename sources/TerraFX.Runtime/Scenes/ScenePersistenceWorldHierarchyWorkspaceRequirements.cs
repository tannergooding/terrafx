// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Scenes;

internal readonly struct ScenePersistenceWorldHierarchyWorkspaceRequirements
{
    internal ScenePersistenceWorldHierarchyWorkspaceRequirements(
        int nodeCount,
        int nodeArrayPayloadBytes,
        int rootEntityCount,
        int rootPayloadBytes,
        int recordBytes)
    {
        NodeCount = nodeCount;
        NodeArrayPayloadBytes = nodeArrayPayloadBytes;
        RecordBytes = recordBytes;
        RootEntityCount = rootEntityCount;
        RootPayloadBytes = rootPayloadBytes;
        WorkspaceBytes = checked(
            (long)nodeArrayPayloadBytes
            + (16L * rootEntityCount)
            + rootPayloadBytes
            + (2L * recordBytes));
    }

    internal int NodeArrayPayloadBytes { get; }

    internal int NodeCount { get; }

    internal int RecordBytes { get; }

    internal int RootEntityCount { get; }

    internal int RootPayloadBytes { get; }

    internal long WorkspaceBytes { get; }

    internal bool Matches(ScenePersistenceWorldHierarchyWorkspaceRequirements other)
        => (NodeArrayPayloadBytes == other.NodeArrayPayloadBytes)
        && (NodeCount == other.NodeCount)
        && (RecordBytes == other.RecordBytes)
        && (RootEntityCount == other.RootEntityCount)
        && (RootPayloadBytes == other.RootPayloadBytes)
        && (WorkspaceBytes == other.WorkspaceBytes);
}
