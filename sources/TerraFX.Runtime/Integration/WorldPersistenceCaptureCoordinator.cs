// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Integration;

internal static class WorldPersistenceCaptureCoordinator
{
    internal static EbinWriteStatus Capture(
        World world,
        WorldPersistenceCaptureLease lease,
        CompiledContentSchemaRegistry schemas,
        out byte[]? worldRecord,
        out int assetIdCount,
        out EbinStructuralDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(schemas);
        worldRecord = null;
        assetIdCount = 0;
        var status = ScenePersistenceCaptureCoordinator.Capture(
            world,
            lease,
            out var scenesRecord,
            out var scenesReservation,
            out diagnostic,
            cancellationToken);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        using var scenesReservationScope = scenesReservation
            ?? throw new InvalidOperationException("The Scene capture reservation was not created.");
        var capturedScenesRecord = scenesRecord
            ?? throw new InvalidOperationException("The Scene capture did not produce its record.");
        return WorldPersistenceWorldRecordCaptureCoordinator.Capture(
            world,
            lease,
            schemas,
            capturedScenesRecord,
            scenesReservationScope,
            out worldRecord,
            out assetIdCount,
            out diagnostic,
            cancellationToken);
    }
}
