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

    internal bool TryCaptureCommit(
        out WorldDomainOperationCreditBundle? operationCredits,
        out WorldDefaultDomainHandoff? defaultDomainHandoff,
        out WorldGuidRefusal bindingRefusal,
        out WorldAdmissionRefusal admissionRefusal)
    {
        lock (_gate)
        {
            operationCredits = null;
            defaultDomainHandoff = null;

            if (_isAcquisitionClosed || _isCaptured)
            {
                bindingRefusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                admissionRefusal = WorldAdmissionRefusal.None;
                return false;
            }

            if (!_targetWorld.TryCaptureLoadReplacement(
                    _replacementPreparation,
                    out operationCredits,
                    out defaultDomainHandoff,
                    out bindingRefusal,
                    out admissionRefusal))
            {
                return false;
            }

            if (operationCredits is null)
            {
                throw new InvalidOperationException("The captured World replacement omitted its operation credits.");
            }

            _isCaptured = true;
            return true;
        }
    }
}
