// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldPreparedReplacement
{
    private readonly WorldConstructionPlan _constructionPlan;
    private readonly Lock _gate = new();
    private bool _isAcquisitionClosed;
    private bool _isCaptured;
    private readonly WorldGuidReplacementPreparation _replacementPreparation;
    private readonly World _targetWorld;

    internal WorldPreparedReplacement(
        World targetWorld,
        WorldConstructionPlan constructionPlan,
        WorldGuidReplacementPreparation replacementPreparation)
    {
        ArgumentNullException.ThrowIfNull(targetWorld);
        ArgumentNullException.ThrowIfNull(constructionPlan);
        ArgumentNullException.ThrowIfNull(replacementPreparation);
        _targetWorld = targetWorld;
        _constructionPlan = constructionPlan;
        _replacementPreparation = replacementPreparation;
    }

    internal World TargetWorld => _targetWorld;

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
        out WorldGuidRefusal bindingRefusal,
        out WorldAdmissionRefusal admissionRefusal)
    {
        lock (_gate)
        {
            if (_isAcquisitionClosed || _isCaptured)
            {
                bindingRefusal = new WorldGuidRefusal(WorldGuidRefusal.TargetBindingStale);
                admissionRefusal = WorldAdmissionRefusal.None;
                return false;
            }

            if (!_targetWorld.TryCaptureLoadReplacement(
                    _replacementPreparation,
                    out bindingRefusal,
                    out admissionRefusal))
            {
                return false;
            }

            _isCaptured = true;
            return true;
        }
    }
}
