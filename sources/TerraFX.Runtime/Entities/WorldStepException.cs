// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Reports a World tick that failed before its publication committed.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "WorldStepException is engine-issued and requires the failed tick and its retained diagnostic.")]
public sealed class WorldStepException : InvalidOperationException
{
    internal WorldStepException(long tick, Diagnostic diagnostic, Exception innerException)
        : base("The World tick failed before publication.", innerException)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentNullException.ThrowIfNull(innerException);
        Tick = tick;
        Diagnostic = diagnostic;
    }

    /// <summary>Gets the diagnostic that faulted the tick.</summary>
    public Diagnostic Diagnostic { get; }

    /// <summary>Gets the zero-based tick index that failed.</summary>
    public long Tick { get; }
}
