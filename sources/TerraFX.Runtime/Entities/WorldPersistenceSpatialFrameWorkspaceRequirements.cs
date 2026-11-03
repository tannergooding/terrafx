// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly struct WorldPersistenceSpatialFrameWorkspaceRequirements
{
    internal WorldPersistenceSpatialFrameWorkspaceRequirements(
        int profileRecordBytes,
        int framePayloadStorageBytes,
        int recordBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(profileRecordBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(framePayloadStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordBytes);
        ProfileRecordBytes = profileRecordBytes;
        FramePayloadStorageBytes = framePayloadStorageBytes;
        RecordBytes = recordBytes;
        WorkspaceBytes = checked((2L * profileRecordBytes) + framePayloadStorageBytes + (2L * recordBytes));
    }

    internal int FramePayloadStorageBytes { get; }

    internal int ProfileRecordBytes { get; }

    internal int RecordBytes { get; }

    internal long WorkspaceBytes { get; }
}
