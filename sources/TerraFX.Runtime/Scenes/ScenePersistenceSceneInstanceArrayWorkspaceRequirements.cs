// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Scenes;

internal readonly struct ScenePersistenceSceneInstanceArrayWorkspaceRequirements
{
    internal ScenePersistenceSceneInstanceArrayWorkspaceRequirements(
        int instanceCount,
        int recordStorageBytes,
        int arrayPayloadBytes)
    {
        ArrayPayloadBytes = arrayPayloadBytes;
        InstanceCount = instanceCount;
        RecordStorageBytes = recordStorageBytes;
        WorkspaceBytes = checked(
            ((long)instanceCount * IntPtr.Size)
            + (2L * instanceCount * Unsafe.SizeOf<EbinPayloadSlice>())
            + ((long)instanceCount * Unsafe.SizeOf<int>())
            + recordStorageBytes
            + arrayPayloadBytes);
    }

    internal int ArrayPayloadBytes { get; }

    internal int InstanceCount { get; }

    internal int RecordStorageBytes { get; }

    internal long WorkspaceBytes { get; }

    internal bool Matches(ScenePersistenceSceneInstanceArrayWorkspaceRequirements other)
        => (ArrayPayloadBytes == other.ArrayPayloadBytes)
        && (InstanceCount == other.InstanceCount)
        && (RecordStorageBytes == other.RecordStorageBytes)
        && (WorkspaceBytes == other.WorkspaceBytes);
}
