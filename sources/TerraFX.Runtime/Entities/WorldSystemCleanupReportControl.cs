// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldSystemCleanupReportControl
{
    private long _failureCount;
    private readonly WorldSystemCleanupFailure[] _failures;
    private bool _isSealed;
    private bool _isStarted;
    private int _releaseAttemptCount;
    private int _retainedFailureCount;

    internal WorldSystemCleanupReportControl(Guid worldGuid, int maximumReleaseAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumReleaseAttempts);
        WorldGuid = worldGuid;
        _failures = new WorldSystemCleanupFailure[Math.Min(maximumReleaseAttempts, WorldSystemCleanupReport.MaximumRetainedFailures)];

        for (var index = 0; index < _failures.Length; index++)
        {
            _failures[index] = new WorldSystemCleanupFailure(new WorldSystemCleanupFailureControl());
        }

        Report = new WorldSystemCleanupReport(this);
    }

    internal Exception? ConstructionPrimaryException { get; private set; }

    internal long FailureCount => _failureCount;

    internal WorldSystemCleanupOperation Operation { get; private set; }

    internal int ReleaseAttemptCount => _releaseAttemptCount;

    internal WorldSystemCleanupReport Report { get; }

    internal int RetainedFailureCount => _retainedFailureCount;

    internal Guid WorldGuid { get; }

    internal void Begin(WorldSystemCleanupOperation operation, Exception? constructionPrimaryException)
    {
        if (_isStarted || _isSealed)
        {
            throw new InvalidOperationException("The prepared system cleanup report has already been used.");
        }

        if (operation is not WorldSystemCleanupOperation.ConstructionUnwind and not WorldSystemCleanupOperation.Disposal)
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        Operation = operation;
        ConstructionPrimaryException = constructionPrimaryException;
        _isStarted = true;
    }

    internal WorldSystemCleanupFailure GetFailure(int index)
        => _failures[index];

    internal void RecordFailure(
        Guid systemId,
        Guid factoryId,
        int factoryVersion,
        int releaseAttemptIndex,
        Exception originalException)
    {
        if (!_isStarted || _isSealed)
        {
            throw new InvalidOperationException("System cleanup failures can only be recorded during cleanup.");
        }

        _failureCount = checked(_failureCount + 1);

        if (_retainedFailureCount < _failures.Length)
        {
            var failure = _failures[_retainedFailureCount];
            failure.Seal(systemId, factoryId, factoryVersion, releaseAttemptIndex, originalException);
            _retainedFailureCount++;
        }
    }

    internal int RecordReleaseAttempt()
    {
        if (!_isStarted || _isSealed)
        {
            throw new InvalidOperationException("Release attempts can only be recorded during cleanup.");
        }

        var index = _releaseAttemptCount;
        _releaseAttemptCount = checked(_releaseAttemptCount + 1);
        return index;
    }

    internal void Seal()
    {
        if (!_isStarted || _isSealed)
        {
            throw new InvalidOperationException("The prepared system cleanup report cannot be sealed in its current state.");
        }

        if ((_failureCount > _releaseAttemptCount)
            || (_retainedFailureCount != Math.Min(_failureCount, Math.Min(_releaseAttemptCount, _failures.Length))))
        {
            throw new InvalidOperationException("The prepared system cleanup report contains inconsistent counts.");
        }

        _isSealed = true;
    }

    internal void ThrowIfUnsealed()
    {
        if (!_isSealed)
        {
            throw new InvalidOperationException("The system cleanup report has not been sealed.");
        }
    }
}
