// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Hosting;

/// <summary>Reports the terminal outcome of one hosted time-scale request.</summary>
public sealed class TimeScaleReceipt
{
    internal TimeScaleReceipt(
        TimeScaleChangeStatus status,
        float? appliedTimeScale,
        ImmutableArray<Diagnostic> diagnostics)
    {
        if (diagnostics.IsDefault
            || ((status is TimeScaleChangeStatus.Applied)
                && ((appliedTimeScale is null)
                    || !float.IsFinite(appliedTimeScale.Value)
                    || (appliedTimeScale.Value < 0)
                    || (appliedTimeScale.Value > 4)
                    || !diagnostics.IsEmpty))
            || ((status is TimeScaleChangeStatus.Rejected)
                && ((appliedTimeScale is not null) || diagnostics.IsEmpty))
            || !Enum.IsDefined(status))
        {
            throw new ArgumentException("The time-scale status, applied value, and diagnostics are inconsistent.");
        }

        Status = status;
        AppliedTimeScale = appliedTimeScale;
        Diagnostics = [.. diagnostics];
    }

    /// <summary>Gets the successfully applied scale, or null when the operation was rejected.</summary>
    public float? AppliedTimeScale { get; }

    /// <summary>Gets initialized diagnostics for a rejected request.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Gets whether the requested scale was applied.</summary>
    public TimeScaleChangeStatus Status { get; }
}
