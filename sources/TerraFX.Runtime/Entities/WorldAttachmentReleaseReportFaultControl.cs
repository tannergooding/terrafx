// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldAttachmentReleaseReportFaultControl
{
    private bool _isSealed;
    private Exception? _originalReleaseException;
    private Exception? _reportFinalizationException;

    internal long CommittedMutationRevision { get; private set; }

    internal Exception OriginalReleaseException
    {
        get
        {
            ThrowIfUnsealed();
            return _originalReleaseException
                ?? throw new InvalidOperationException("The attachment release fallback has no original exception.");
        }
    }

    internal Guid? OwnerEntityId { get; private set; }

    internal long ReleaseFailureCount { get; private set; }

    internal ulong ReleasedInstallationId { get; private set; }

    internal ulong? ReplacementInstallationId { get; private set; }

    internal Exception ReportFinalizationException
    {
        get
        {
            ThrowIfUnsealed();
            return _reportFinalizationException
                ?? throw new InvalidOperationException("The attachment release fallback has no finalization exception.");
        }
    }

    internal ContentSchemaReference Schema { get; private set; }

    internal AttachmentScope Scope { get; private set; }

    internal WorldSystemCleanupReport? SystemCleanupReport { get; private set; }

    internal Guid WorldGuid { get; private set; }

    internal void Seal(
        WorldAttachmentReleaseFailure primaryFailure,
        long committedMutationRevision,
        long releaseFailureCount,
        Exception reportFinalizationException,
        WorldSystemCleanupReport? systemCleanupReport)
    {
        ArgumentNullException.ThrowIfNull(primaryFailure);
        ArgumentOutOfRangeException.ThrowIfNegative(committedMutationRevision);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(releaseFailureCount);
        ArgumentNullException.ThrowIfNull(reportFinalizationException);

        if (_isSealed)
        {
            throw new InvalidOperationException("The prepared attachment release fallback is already sealed.");
        }

        WorldGuid = primaryFailure.WorldGuid;
        Scope = primaryFailure.Scope;
        Schema = primaryFailure.Schema;
        OwnerEntityId = primaryFailure.OwnerEntityId;
        ReleasedInstallationId = primaryFailure.ReleasedInstallationId;
        ReplacementInstallationId = primaryFailure.ReplacementInstallationId;
        _originalReleaseException = primaryFailure.OriginalException;
        _reportFinalizationException = reportFinalizationException;
        CommittedMutationRevision = committedMutationRevision;
        ReleaseFailureCount = releaseFailureCount;
        SystemCleanupReport = systemCleanupReport;
        _isSealed = true;
    }

    internal void ThrowIfUnsealed()
    {
        if (!_isSealed)
        {
            throw new InvalidOperationException("The attachment release finalization fallback has not been sealed.");
        }
    }
}
