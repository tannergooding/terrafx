// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Entities;

namespace TerraFX.Hosting;

internal sealed class WorldPersistenceWorldRecordContents
{
    internal WorldPersistenceWorldRecordContents(
        WorldPersistenceConfiguration configuration,
        WorldPersistenceClockContents clock,
        WorldPersistenceCursorContents cursors,
        WorldPersistenceRegistrationContents registration,
        ReadOnlyMemory<byte> configurationRecord,
        ReadOnlyMemory<byte> clockRecord,
        ReadOnlyMemory<byte> frame2Record,
        ReadOnlyMemory<byte> frame3Record,
        ReadOnlyMemory<byte> ecsRecord,
        ReadOnlyMemory<byte> scenesRecord,
        ReadOnlyMemory<byte> assetIdsPayload,
        int assetIdCount,
        ReadOnlyMemory<byte> controlRecordsPayload,
        int controlRecordCount,
        ReadOnlyMemory<byte> cursorsRecord,
        ReadOnlyMemory<byte> randomStreamsPayload,
        int randomStreamCount,
        ReadOnlyMemory<byte> strictLayoutRecord,
        ReadOnlyMemory<byte> registrationRecord)
    {
        Configuration = configuration;
        Clock = clock;
        Cursors = cursors;
        Registration = registration;
        ConfigurationRecord = configurationRecord;
        ClockRecord = clockRecord;
        Frame2Record = frame2Record;
        Frame3Record = frame3Record;
        EcsRecord = ecsRecord;
        ScenesRecord = scenesRecord;
        AssetIdsPayload = assetIdsPayload;
        AssetIdCount = assetIdCount;
        ControlRecordsPayload = controlRecordsPayload;
        ControlRecordCount = controlRecordCount;
        CursorsRecord = cursorsRecord;
        RandomStreamsPayload = randomStreamsPayload;
        RandomStreamCount = randomStreamCount;
        StrictLayoutRecord = strictLayoutRecord;
        RegistrationRecord = registrationRecord;
    }

    internal int AssetIdCount { get; }

    internal ReadOnlyMemory<byte> AssetIdsPayload { get; }

    internal WorldPersistenceClockContents Clock { get; }

    internal ReadOnlyMemory<byte> ClockRecord { get; }

    internal WorldPersistenceConfiguration Configuration { get; }

    internal ReadOnlyMemory<byte> ConfigurationRecord { get; }

    internal int ControlRecordCount { get; }

    internal ReadOnlyMemory<byte> ControlRecordsPayload { get; }

    internal WorldPersistenceCursorContents Cursors { get; }

    internal ReadOnlyMemory<byte> CursorsRecord { get; }

    internal ReadOnlyMemory<byte> EcsRecord { get; }

    internal ReadOnlyMemory<byte> Frame2Record { get; }

    internal ReadOnlyMemory<byte> Frame3Record { get; }

    internal int RandomStreamCount { get; }

    internal ReadOnlyMemory<byte> RandomStreamsPayload { get; }

    internal WorldPersistenceRegistrationContents Registration { get; }

    internal ReadOnlyMemory<byte> RegistrationRecord { get; }

    internal ReadOnlyMemory<byte> ScenesRecord { get; }

    internal ReadOnlyMemory<byte> StrictLayoutRecord { get; }
}
