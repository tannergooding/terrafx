// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Reports a failure while finalizing a World-system cleanup exception.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "System cleanup finalization exceptions are engine-issued and require their sealed report and fallback causes.")]
public sealed class WorldSystemCleanupReportFinalizationException : InvalidOperationException
{
    private readonly WorldSystemCleanupReportFaultControl _control;

    internal WorldSystemCleanupReportFinalizationException(WorldSystemCleanupReportFaultControl control)
        : base("The World system cleanup exception could not be finalized.")
    {
        ArgumentNullException.ThrowIfNull(control);
        _control = control;
    }

    /// <summary>Gets the construction exception that caused unwind, when present.</summary>
    public Exception? ConstructionPrimaryException => SystemCleanupReport.ConstructionPrimaryException;

    /// <summary>Gets the exact first cleanup exception retained in the report.</summary>
    public Exception OriginalCleanupException
        => _control.OriginalCleanupException
            ?? throw new InvalidOperationException("The prepared cleanup finalization fault has not been sealed.");

    /// <summary>Gets the exact exception raised while finalizing the cleanup exception.</summary>
    public Exception ReportFinalizationException
        => _control.ReportFinalizationException
            ?? throw new InvalidOperationException("The prepared cleanup finalization fault has not been sealed.");

    /// <summary>Gets the sealed cleanup report that could not be wrapped normally.</summary>
    public WorldSystemCleanupReport SystemCleanupReport => _control.Report;
}
