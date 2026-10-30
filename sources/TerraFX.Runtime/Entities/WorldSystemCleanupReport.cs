// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Contains bounded immutable facts from one World-system cleanup operation.</summary>
public sealed class WorldSystemCleanupReport
{
    internal const int MaximumRetainedFailures = 64;

    private readonly WorldSystemCleanupReportControl _control;

    internal WorldSystemCleanupReport(WorldSystemCleanupReportControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _control = control;
    }

    /// <summary>Gets the system construction exception that caused unwind, when present.</summary>
    public Exception? ConstructionPrimaryException
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.ConstructionPrimaryException;
        }
    }

    /// <summary>Gets the total number of Release callbacks that failed.</summary>
    public long FailureCount
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.FailureCount;
        }
    }

    /// <summary>Gets the number of failures omitted from the bounded detail array.</summary>
    public long OmittedFailureCount => FailureCount - RetainedFailureCount;

    /// <summary>Gets the cleanup operation represented by this report.</summary>
    public WorldSystemCleanupOperation Operation
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.Operation;
        }
    }

    /// <summary>Gets the number of Release attempts completed by the cleanup operation.</summary>
    public int ReleaseAttemptCount
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.ReleaseAttemptCount;
        }
    }

    /// <summary>Gets the exact retained-failure capacity, min(system count, 64).</summary>
    public int RetainedFailureCapacity => Math.Min(ReleaseAttemptCount, MaximumRetainedFailures);

    /// <summary>Gets the bounded number of retained Release failures.</summary>
    public int RetainedFailureCount
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.RetainedFailureCount;
        }
    }

    /// <summary>Gets the logical World identity.</summary>
    public Guid WorldGuid
    {
        get
        {
            _control.ThrowIfUnsealed();
            return _control.WorldGuid;
        }
    }

    /// <summary>Gets one retained Release failure.</summary>
    /// <param name="index">The zero-based retained failure index.</param>
    /// <returns>The immutable failure facts.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the retained range.</exception>
    public WorldSystemCleanupFailure GetFailure(int index)
    {
        _control.ThrowIfUnsealed();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _control.RetainedFailureCount);
        return _control.GetFailure(index);
    }
}
