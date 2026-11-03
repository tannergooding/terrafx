// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly ref struct WorldPersistenceWorldRecordSources
{
    internal WorldPersistenceWorldRecordSources(
        WorldPersistenceCaptureLease lease,
        ReadOnlySpan<uint> assetIds,
        ReadOnlySpan<byte> clockRecord,
        ReadOnlySpan<byte> configurationRecord,
        int controlRecordCount,
        ReadOnlySpan<byte> controlsArrayPayload,
        ReadOnlySpan<byte> cursorsRecord,
        ReadOnlySpan<byte> ecsRecord,
        ReadOnlySpan<byte> frame2Record,
        ReadOnlySpan<byte> frame3Record,
        ReadOnlySpan<byte> randomArrayPayload,
        int randomStreamCount,
        ReadOnlySpan<byte> registrationRecord,
        ReadOnlySpan<byte> scenesRecord,
        ReadOnlySpan<byte> strictLayoutRecord)
    {
        lease.ValidateCurrent();
        ArgumentOutOfRangeException.ThrowIfNegative(controlRecordCount);
        ArgumentOutOfRangeException.ThrowIfNegative(randomStreamCount);
        AssetIds = assetIds;
        ClockRecord = clockRecord;
        ConfigurationRecord = configurationRecord;
        ControlRecordCount = controlRecordCount;
        ControlsArrayPayload = controlsArrayPayload;
        CursorsRecord = cursorsRecord;
        EcsRecord = ecsRecord;
        Frame2Record = frame2Record;
        Frame3Record = frame3Record;
        LeaseSequence = lease.Sequence;
        RandomArrayPayload = randomArrayPayload;
        RandomStreamCount = randomStreamCount;
        RegistrationRecord = registrationRecord;
        ScenesRecord = scenesRecord;
        StrictLayoutRecord = strictLayoutRecord;
        WorldGuid = lease.WorldGuid;
    }

    internal ReadOnlySpan<uint> AssetIds { get; }

    internal ReadOnlySpan<byte> ClockRecord { get; }

    internal ReadOnlySpan<byte> ConfigurationRecord { get; }

    internal int ControlRecordCount { get; }

    internal ReadOnlySpan<byte> ControlsArrayPayload { get; }

    internal ReadOnlySpan<byte> CursorsRecord { get; }

    internal ReadOnlySpan<byte> EcsRecord { get; }

    internal ReadOnlySpan<byte> Frame2Record { get; }

    internal ReadOnlySpan<byte> Frame3Record { get; }

    internal long LeaseSequence { get; }

    internal ReadOnlySpan<byte> RandomArrayPayload { get; }

    internal int RandomStreamCount { get; }

    internal ReadOnlySpan<byte> RegistrationRecord { get; }

    internal ReadOnlySpan<byte> ScenesRecord { get; }

    internal ReadOnlySpan<byte> StrictLayoutRecord { get; }

    internal Guid WorldGuid { get; }
}
