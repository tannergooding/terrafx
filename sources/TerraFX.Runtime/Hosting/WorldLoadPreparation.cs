// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Hosting;

/// <summary>Contains the result of validating a bounded checkpoint for one target World.</summary>
public sealed class WorldLoadPreparation : IDisposable
{
    internal WorldLoadPreparation(
        WorldLoadPreparationStatus status,
        WorldLoadPlan? plan,
        ImmutableArray<Diagnostic> diagnostics)
    {
        if (!Enum.IsDefined(status)
            || (status is WorldLoadPreparationStatus.Prepared) != (plan is not null)
            || (status is WorldLoadPreparationStatus.Prepared) == !diagnostics.IsEmpty)
        {
            throw new ArgumentException("The World load preparation status, plan, and diagnostics are inconsistent.");
        }

        Status = status;
        Plan = plan;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the nonempty diagnostics when preparation was rejected.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Gets the prepared plan, or null when preparation was rejected.</summary>
    public WorldLoadPlan? Plan { get; }

    /// <summary>Gets whether a complete replacement plan was prepared.</summary>
    public WorldLoadPreparationStatus Status { get; }

    /// <summary>Disposes the preparation and closes acquisition of its plan.</summary>
    public void Dispose()
        => Plan?.Dispose();
}
