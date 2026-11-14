// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Hosting;

internal sealed class WorldLoadPreparedState : IDisposable
{
    private readonly AssetPackageLease _packageLease;
    private readonly IDisposable _sourceUse;
    private readonly Lock _gate = new();
    private bool _isAcquisitionClosed;
    private bool _isCaptured;
    private bool _isDisposed;

    internal WorldLoadPreparedState(
        WorldConstructionPlan constructionPlan,
        WorldGuidReplacementPreparation replacementPreparation,
        AssetPackageLease packageLease,
        IDisposable sourceUse,
        byte[] fileRecord,
        WorldPersistenceCheckpointContents checkpoint,
        WorldRestoreIntent restoreIntent)
    {
        ArgumentNullException.ThrowIfNull(constructionPlan);
        ArgumentNullException.ThrowIfNull(replacementPreparation);
        ArgumentNullException.ThrowIfNull(packageLease);
        ArgumentNullException.ThrowIfNull(sourceUse);
        ArgumentNullException.ThrowIfNull(fileRecord);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ConstructionPlan = constructionPlan;
        ReplacementPreparation = replacementPreparation;
        _packageLease = packageLease;
        _sourceUse = sourceUse;
        FileRecord = fileRecord;
        Checkpoint = checkpoint;
        RestoreIntent = restoreIntent;
    }

    internal WorldPersistenceCheckpointContents Checkpoint { get; }

    internal WorldConstructionPlan ConstructionPlan { get; }

    internal byte[] FileRecord { get; }

    internal WorldGuidReplacementPreparation ReplacementPreparation { get; }

    internal WorldRestoreIntent RestoreIntent { get; }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        try
        {
            ReplacementPreparation.Dispose();
        }
        finally
        {
            try
            {
                ConstructionPlan.ReleaseUntransferredRestoreResources();
            }
            finally
            {
                try
                {
                    _sourceUse.Dispose();
                }
                finally
                {
                    _packageLease.Dispose();
                }
            }
        }
    }

    internal void CloseAcquisition()
    {
        lock (_gate)
        {
            _isAcquisitionClosed = true;

            if (_isCaptured)
            {
                return;
            }
        }

        Dispose();
    }

    internal bool TryCaptureCommit()
    {
        lock (_gate)
        {
            if (_isAcquisitionClosed || _isDisposed || _isCaptured)
            {
                return false;
            }

            _isCaptured = true;
            return true;
        }
    }
}
