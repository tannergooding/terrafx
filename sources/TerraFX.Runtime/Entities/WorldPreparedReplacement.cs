// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldPreparedReplacement
{
    private readonly WorldConstructionPlanControl _control;

    internal WorldPreparedReplacement(WorldConstructionPlanControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _control = control;
    }

    internal WorldRestoreSourceControl? PreparedSourceControl => _control.PreparedSourceControl;

    internal World TargetWorld => _control.TargetWorld;

    internal void BindPreparedResourceSourceOwner(IWorldRestoreSourceOwner owner)
        => _control.BindPreparedResourceSourceOwner(owner);

    internal void CloseAcquisition()
        => _control.CloseAcquisition();

    internal bool IsBoundTo(WorldConstructionPlan constructionPlan)
        => _control.IsBoundTo(constructionPlan);

    internal bool TryCaptureCommit(
        out WorldDomainOperationCreditBundle? operationCredits,
        out WorldDefaultDomainHandoff? defaultDomainHandoff,
        out WorldGuidRefusal bindingRefusal,
        out WorldAdmissionRefusal admissionRefusal)
        => _control.TryCaptureCommit(
            out operationCredits,
            out defaultDomainHandoff,
            out bindingRefusal,
            out admissionRefusal);
}
