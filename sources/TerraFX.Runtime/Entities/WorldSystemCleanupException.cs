// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Reports one system cleanup operation that retained one or more Release failures.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "System cleanup exceptions are engine-issued and require their sealed report.")]
public sealed class WorldSystemCleanupException : AggregateException
{
    internal WorldSystemCleanupException(WorldSystemCleanupReport report)
        : base("One or more World system instances failed to release.", CreateInnerExceptions(report))
    {
        ArgumentNullException.ThrowIfNull(report);
        SystemCleanupReport = report;
        ConstructionPrimaryException = report.ConstructionPrimaryException;
    }

    /// <summary>Gets the construction exception that caused cleanup unwind, when present.</summary>
    public Exception? ConstructionPrimaryException { get; }

    /// <summary>Gets the exact sealed cleanup report carried by this exception.</summary>
    public WorldSystemCleanupReport SystemCleanupReport { get; }

    private static List<Exception> CreateInnerExceptions(WorldSystemCleanupReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var exceptions = new List<Exception>(report.RetainedFailureCount + (report.ConstructionPrimaryException is null ? 0 : 1));

        if (report.ConstructionPrimaryException is { } primary)
        {
            exceptions.Add(primary);
        }

        for (var index = 0; index < report.RetainedFailureCount; index++)
        {
            exceptions.Add(report.GetFailure(index).OriginalException);
        }

        return exceptions;
    }
}
