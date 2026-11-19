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

    internal bool IsCommitCaptured => _control.IsCommitCaptured;

    internal WorldRestoreSourceControl? PreparedSourceControl => _control.PreparedSourceControl;

    internal World TargetWorld => _control.TargetWorld;

    internal void BindPreparedResourceSourceOwner(IWorldRestoreSourceOwner owner)
        => _control.BindPreparedResourceSourceOwner(owner);

    internal void CloseAcquisition()
        => _control.CloseAcquisition();

    internal bool IsBoundTo(WorldConstructionPlan constructionPlan)
        => _control.IsBoundTo(constructionPlan);

    internal bool IsCapturedOperation(WorldReplacementOperation operation)
        => _control.IsCapturedOperation(operation);

    internal void ReleaseUntransferredOperation()
        => _control.ReleaseUntransferredOperation();

    internal void SealInputs(
        WorldRestoreSourceControl.SourceUse sourceUse,
        ReadOnlyMemory<byte> validatedWorldRecord,
        scoped ReadOnlySpan<byte> checkpointDigest,
        scoped ReadOnlySpan<byte> payloadDigest)
        => _control.SealInputs(sourceUse, validatedWorldRecord, checkpointDigest, payloadDigest);

    internal bool TryCaptureCommit(
        out WorldReplacementOperation? operation,
        out WorldGuidRefusal refusal)
        => _control.TryCaptureCommit(out operation, out refusal);
}
