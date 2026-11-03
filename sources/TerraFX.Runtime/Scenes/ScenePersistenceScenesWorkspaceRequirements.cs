// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Scenes;

internal readonly struct ScenePersistenceScenesWorkspaceRequirements
{
    internal ScenePersistenceScenesWorkspaceRequirements(
        int instanceCount,
        int instancesArrayPayloadBytes,
        int hierarchyRecordBytes,
        int rootPayloadBytes,
        int recordBytes)
    {
        HierarchyRecordBytes = hierarchyRecordBytes;
        InstanceCount = instanceCount;
        InstancesArrayPayloadBytes = instancesArrayPayloadBytes;
        RecordBytes = recordBytes;
        RootPayloadBytes = rootPayloadBytes;
        WorkspaceBytes = checked(
            (long)instancesArrayPayloadBytes
            + hierarchyRecordBytes
            + rootPayloadBytes
            + (2L * recordBytes));
    }

    internal int HierarchyRecordBytes { get; }

    internal int InstanceCount { get; }

    internal int InstancesArrayPayloadBytes { get; }

    internal int RecordBytes { get; }

    internal int RootPayloadBytes { get; }

    internal long WorkspaceBytes { get; }

    internal bool Matches(ScenePersistenceScenesWorkspaceRequirements other)
        => (HierarchyRecordBytes == other.HierarchyRecordBytes)
        && (InstanceCount == other.InstanceCount)
        && (InstancesArrayPayloadBytes == other.InstancesArrayPayloadBytes)
        && (RecordBytes == other.RecordBytes)
        && (RootPayloadBytes == other.RootPayloadBytes)
        && (WorkspaceBytes == other.WorkspaceBytes);
}
