// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Entities;

internal readonly struct WorldTimeScaleChangeOutcome
{
    internal WorldTimeScaleChangeOutcome(bool isApplied, float? appliedTimeScale, ImmutableArray<Diagnostic> diagnostics)
    {
        if (diagnostics.IsDefault
            || (isApplied && ((appliedTimeScale is null) || !float.IsFinite(appliedTimeScale.Value) || !diagnostics.IsEmpty))
            || (!isApplied && ((appliedTimeScale is not null) || diagnostics.IsEmpty)))
        {
            throw new ArgumentException("The time-scale outcome, applied value, and diagnostics are inconsistent.");
        }

        IsApplied = isApplied;
        AppliedTimeScale = appliedTimeScale;
        Diagnostics = ImmutableArrayCopy.Create(diagnostics);
    }

    internal float? AppliedTimeScale { get; }

    internal ImmutableArray<Diagnostic> Diagnostics { get; }

    internal bool IsApplied { get; }
}
