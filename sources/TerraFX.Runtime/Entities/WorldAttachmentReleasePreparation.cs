// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

internal sealed class WorldAttachmentReleasePreparation
{
    private const int MaximumRetainedFailures = 64;

    private readonly WorldAttachmentReleaseFailureControl[] _additionalControls;
    private int _additionalFailureCount;
    private readonly WorldAttachmentReleaseFailure[] _additionalFailures;
    private readonly long _committedMutationRevision;
    private long _failureCount;
    private readonly WorldAttachmentReleaseReportFaultControl _faultControl;
    private readonly WorldAttachmentReleaseReportFinalizationException _finalizationException;
    private bool _hasPrimaryFailure;
    private bool _isFinalized;
    private long _omittedFailureCount;
    private readonly WorldAttachmentReleaseFailure _primaryFailure;

    internal WorldAttachmentReleasePreparation(Guid worldGuid, int maximumReleaseAttempts, long committedMutationRevision)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumReleaseAttempts);
        ArgumentOutOfRangeException.ThrowIfNegative(committedMutationRevision);
        _committedMutationRevision = committedMutationRevision;
        _primaryFailure = new WorldAttachmentReleaseFailure(new WorldAttachmentReleaseFailureControl());
        var additionalCapacity = Math.Min(Math.Max(maximumReleaseAttempts - 1, 0), MaximumRetainedFailures - 1);
        _additionalControls = new WorldAttachmentReleaseFailureControl[additionalCapacity];
        _additionalFailures = new WorldAttachmentReleaseFailure[additionalCapacity];

        for (var index = 0; index < additionalCapacity; index++)
        {
            var control = new WorldAttachmentReleaseFailureControl();
            _additionalControls[index] = control;
            _additionalFailures[index] = new WorldAttachmentReleaseFailure(control);
        }

        _faultControl = new WorldAttachmentReleaseReportFaultControl();
        _finalizationException = new WorldAttachmentReleaseReportFinalizationException(_faultControl);
    }

    internal long FailureCount => _failureCount;

    internal bool HasFailures => _failureCount != 0;

    internal InvalidOperationException CreateException(
        WorldSystemCleanupReport? systemCleanupReport,
        Func<
            WorldAttachmentReleaseFailure,
            ImmutableArray<WorldAttachmentReleaseFailure>,
            long,
            WorldSystemCleanupReport?,
            WorldAttachmentReleaseException> finalize)
        => CreateExceptionCore(systemCleanupReport, finalize);

    internal InvalidOperationException CreateException(WorldSystemCleanupReport? systemCleanupReport)
        => CreateExceptionCore(
            systemCleanupReport,
            static (primary, additional, omitted, cleanupReport) =>
                new WorldAttachmentReleaseException(primary, additional, omitted, cleanupReport));

    internal void RecordFailure(WorldAttachmentReleaseBinding binding, Exception originalException)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(originalException);

        if (_isFinalized)
        {
            throw new InvalidOperationException("Attachment release failures cannot be added after report finalization.");
        }

        _failureCount = checked(_failureCount + 1);

        if (!_hasPrimaryFailure)
        {
            _primaryFailure.Seal(binding, originalException);
            _hasPrimaryFailure = true;
        }
        else if (_additionalFailureCount < _additionalFailures.Length)
        {
            _additionalControls[_additionalFailureCount].Seal(binding, originalException);
            _additionalFailureCount++;
        }
        else
        {
            _omittedFailureCount = checked(_omittedFailureCount + 1);
        }
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "The preconstructed finalization carrier preserves the exact primary and finalization causes if bounded report materialization fails.")]
    private InvalidOperationException CreateExceptionCore(
        WorldSystemCleanupReport? systemCleanupReport,
        Func<
            WorldAttachmentReleaseFailure,
            ImmutableArray<WorldAttachmentReleaseFailure>,
            long,
            WorldSystemCleanupReport?,
            WorldAttachmentReleaseException> finalize)
    {
        ArgumentNullException.ThrowIfNull(finalize);

        if (!_hasPrimaryFailure || _isFinalized)
        {
            throw new InvalidOperationException("The prepared attachment release report cannot be finalized in its current state.");
        }

        _isFinalized = true;

        try
        {
            var additional = ImmutableArray.CreateBuilder<WorldAttachmentReleaseFailure>(_additionalFailureCount);

            for (var index = 0; index < _additionalFailureCount; index++)
            {
                additional.Add(_additionalFailures[index]);
            }

            return finalize(_primaryFailure, additional.MoveToImmutable(), _omittedFailureCount, systemCleanupReport);
        }
        catch (Exception exception)
        {
            _faultControl.Seal(
                _primaryFailure,
                _committedMutationRevision,
                _failureCount,
                exception,
                systemCleanupReport);
            return _finalizationException;
        }
    }
}
