// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

internal sealed class WorldSystemCleanupPreparation
{
    private readonly WorldSystemCleanupReportFaultControl _faultControl;
    private readonly WorldSystemCleanupReportControl _reportControl;

    internal WorldSystemCleanupPreparation(Guid worldGuid, int maximumReleaseAttempts)
    {
        _reportControl = new WorldSystemCleanupReportControl(worldGuid, maximumReleaseAttempts);
        _faultControl = new WorldSystemCleanupReportFaultControl(_reportControl.Report);
        FinalizationException = new WorldSystemCleanupReportFinalizationException(_faultControl);
    }

    internal WorldSystemCleanupReportFinalizationException FinalizationException { get; }

    internal WorldSystemCleanupReport Report => _reportControl.Report;

    internal void Begin(WorldSystemCleanupOperation operation, Exception? constructionPrimaryException)
        => _reportControl.Begin(operation, constructionPrimaryException);

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "The cleanup aggregate is finalized only after callbacks; a preconstructed fallback must retain the sealed report and exact finalization exception.")]
    internal Exception CreateCleanupException()
        => CreateCleanupException(static report => new WorldSystemCleanupException(report));

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "The cleanup aggregate is finalized only after callbacks; a preconstructed fallback must retain the sealed report and exact finalization exception.")]
    internal Exception CreateCleanupException(Func<WorldSystemCleanupReport, WorldSystemCleanupException> finalize)
    {
        ArgumentNullException.ThrowIfNull(finalize);
        _reportControl.ThrowIfUnsealed();

        try
        {
            return finalize(Report);
        }
        catch (Exception exception)
        {
            _faultControl.Seal(Report.GetFailure(0).OriginalException, exception);
            return FinalizationException;
        }
    }

    internal void RecordFailure(
        Guid systemId,
        Guid factoryId,
        int factoryVersion,
        int releaseAttemptIndex,
        Exception originalException)
        => _reportControl.RecordFailure(systemId, factoryId, factoryVersion, releaseAttemptIndex, originalException);

    internal int RecordReleaseAttempt()
        => _reportControl.RecordReleaseAttempt();

    internal void Seal()
        => _reportControl.Seal();
}
