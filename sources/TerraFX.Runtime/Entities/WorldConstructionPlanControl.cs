// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldConstructionPlanControl : IWorldGuidRetentionOwner
{
    private readonly WorldConstructionPlan _constructionPlan;
    private readonly Lock _gate = new();
    private bool _isAcquisitionClosed;
    private bool _isCaptured;
    private bool _isInputsSealed;
    private readonly WorldReplacementOperation _operation;
    private WorldRestoreSourceControl? _preparedSourceControl;
    private readonly WorldGuidReplacementPreparation _replacementPreparation;
    private readonly World _targetWorld;

    internal WorldConstructionPlanControl(
        WorldConstructionPlan constructionPlan,
        World targetWorld,
        WorldGuidReplacementPreparation replacementPreparation)
    {
        ArgumentNullException.ThrowIfNull(constructionPlan);
        ArgumentNullException.ThrowIfNull(targetWorld);
        ArgumentNullException.ThrowIfNull(replacementPreparation);
        _constructionPlan = constructionPlan;
        _targetWorld = targetWorld;
        _replacementPreparation = replacementPreparation;
        _operation = new WorldReplacementOperation(constructionPlan, targetWorld, replacementPreparation);
    }

    internal bool IsCommitCaptured
    {
        get
        {
            lock (_gate)
            {
                return _isCaptured;
            }
        }
    }

    internal WorldRestoreSourceControl? PreparedSourceControl => _preparedSourceControl;

    internal World TargetWorld => _targetWorld;

    internal void BindPreparedResourceSourceOwner(IWorldRestoreSourceOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        using var lease = owner.AcquireRead();
        var identity = _constructionPlan.RestoreIdentityState
            ?? throw new InvalidOperationException("A fresh construction plan cannot bind a prepared restore source.");

        lock (_gate)
        {
            if (_isAcquisitionClosed || _isCaptured || (_preparedSourceControl is not null)
                || !ReferenceEquals(owner.Registration, _constructionPlan.Options.Registration)
                || !ReferenceEquals(owner.IdentityHistory.LineageMembership, identity.LineageMembership)
                || (owner.IdentityHistory.WorldGuid != identity.WorldGuid)
                || (owner.IdentityHistory.ReplayLineageId != identity.ReplayLineageId)
                || (owner.IdentityHistory.NextAssignmentOrdinal != identity.NextAssignmentOrdinal)
                || (owner.CompletedTick != checked(identity.NextTick - 1)))
            {
                throw lease.RecordFailure(new InvalidOperationException("The prepared source cannot bind or rebind this exact registration, history, and completed tick."));
            }

            _preparedSourceControl = lease.Control;
        }
    }

    internal void CloseAcquisition()
    {
        lock (_gate)
        {
            _isAcquisitionClosed = true;
        }
    }

    internal bool IsBoundTo(WorldConstructionPlan constructionPlan)
    {
        ArgumentNullException.ThrowIfNull(constructionPlan);
        return ReferenceEquals(_constructionPlan, constructionPlan);
    }

    internal bool IsCapturedOperation(WorldReplacementOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_gate)
        {
            return _isCaptured && ReferenceEquals(_operation, operation);
        }
    }

    internal void ReleaseUntransferredOperation()
    {
        lock (_gate)
        {
            if (_isCaptured)
            {
                return;
            }
        }

        _operation.ReleaseUntransferred();
    }

    internal void SealInputs(
        WorldRestoreSourceControl.SourceUse sourceUse,
        ReadOnlyMemory<byte> validatedWorldRecord,
        scoped ReadOnlySpan<byte> checkpointDigest,
        scoped ReadOnlySpan<byte> payloadDigest)
    {
        ArgumentNullException.ThrowIfNull(sourceUse);

        lock (_gate)
        {
            if (_isAcquisitionClosed || _isCaptured || _isInputsSealed
                || !ReferenceEquals(sourceUse.Control, _preparedSourceControl))
            {
                throw new InvalidOperationException("The prepared replacement cannot seal foreign, closed, captured, or previously sealed source inputs.");
            }

            _operation.SealInputs(sourceUse, validatedWorldRecord, checkpointDigest, payloadDigest);
            _isInputsSealed = true;
        }
    }

    internal bool TryCaptureCommit(
        out WorldReplacementOperation? operation,
        out WorldGuidRefusal refusal)
    {
        lock (_gate)
        {
            operation = null;

            if (_isAcquisitionClosed || _isCaptured || !_isInputsSealed)
            {
                refusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                return false;
            }

            if (!_targetWorld.TryCaptureLoadReplacement(
                    _replacementPreparation,
                    out var operationCredits,
                    out var defaultDomainHandoff,
                    out var bindingRefusal,
                    out var admissionRefusal))
            {
                refusal = (bindingRefusal.Code != WorldGuidRefusal.None)
                    ? bindingRefusal
                    : WorldGuidRefusal.ForAdmission(admissionRefusal);
                return false;
            }

            if (operationCredits is null)
            {
                throw new InvalidOperationException("The captured World replacement omitted its operation credits.");
            }

            _operation.Capture(operationCredits, defaultDomainHandoff);
            _isCaptured = true;
            operation = _operation;
            refusal = default;
            return true;
        }
    }
}
