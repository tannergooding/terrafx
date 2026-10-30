// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldSystemCleanupReportFaultControl
{
    private bool _isSealed;

    internal WorldSystemCleanupReportFaultControl(WorldSystemCleanupReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Report = report;
    }

    internal Exception? OriginalCleanupException { get; private set; }

    internal WorldSystemCleanupReport Report { get; }

    internal Exception? ReportFinalizationException { get; private set; }

    internal void Seal(Exception originalCleanupException, Exception reportFinalizationException)
    {
        if (_isSealed)
        {
            throw new InvalidOperationException("A cleanup report finalization fault cannot be sealed more than once.");
        }

        ArgumentNullException.ThrowIfNull(originalCleanupException);
        ArgumentNullException.ThrowIfNull(reportFinalizationException);
        OriginalCleanupException = originalCleanupException;
        ReportFinalizationException = reportFinalizationException;
        _isSealed = true;
    }
}
