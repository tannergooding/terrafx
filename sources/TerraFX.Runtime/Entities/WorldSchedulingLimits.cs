// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Defines bounded per-World scheduler memory and conflict-work budgets.</summary>
public sealed class WorldSchedulingLimits
{
    private const long MaximumCommandStagingBytesLimit = 32L * 1024 * 1024;
    private const int MaximumConflictComparisonsLimit = 16_777_216;
    private const long MaximumDiagnosticBytesLimit = 8L * 1024 * 1024;
    private const long MaximumGraphBytesLimit = 8L * 1024 * 1024;
    private const long MaximumOwnedStateBytesLimit = 2L * 1024 * 1024 * 1024;
    private const long MaximumRegistrationBytesLimit = 16L * 1024 * 1024;
    private const long MaximumResourceAndSystemStateBytesLimit = 128L * 1024 * 1024;
    private const long MaximumWorkPlanBytesLimit = 64L * 1024 * 1024;
    private const long MinimumByteBudget = 4096;

    /// <summary>Initializes bounded scheduler budgets.</summary>
    /// <param name="maximumRegistrationBytes">The maximum registration metadata bytes.</param>
    /// <param name="maximumGraphBytes">The maximum graph and cycle-diagnostic bytes.</param>
    /// <param name="maximumWorkPlanBytes">The maximum current and candidate work-plan bytes.</param>
    /// <param name="maximumCommandStagingBytes">The maximum command payload and staging bytes.</param>
    /// <param name="maximumOwnedStateBytes">The maximum shared World-owned state bytes.</param>
    /// <param name="maximumDiagnosticBytes">The maximum retained scheduler diagnostic bytes.</param>
    /// <param name="maximumConflictComparisons">The maximum registration conflict comparisons.</param>
    /// <exception cref="ArgumentOutOfRangeException">A budget is outside its supported range.</exception>
    public WorldSchedulingLimits(
        long maximumRegistrationBytes,
        long maximumGraphBytes,
        long maximumWorkPlanBytes,
        long maximumCommandStagingBytes,
        long maximumOwnedStateBytes,
        long maximumDiagnosticBytes,
        int maximumConflictComparisons)
    {
        ValidateBytes(maximumRegistrationBytes, MaximumRegistrationBytesLimit, nameof(maximumRegistrationBytes));
        ValidateBytes(maximumGraphBytes, MaximumGraphBytesLimit, nameof(maximumGraphBytes));
        ValidateBytes(maximumWorkPlanBytes, MaximumWorkPlanBytesLimit, nameof(maximumWorkPlanBytes));
        ValidateBytes(maximumCommandStagingBytes, MaximumCommandStagingBytesLimit, nameof(maximumCommandStagingBytes));
        ValidateBytes(maximumOwnedStateBytes, MaximumOwnedStateBytesLimit, nameof(maximumOwnedStateBytes));
        ValidateBytes(maximumDiagnosticBytes, MaximumDiagnosticBytesLimit, nameof(maximumDiagnosticBytes));
        ArgumentOutOfRangeException.ThrowIfNegative(maximumConflictComparisons);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumConflictComparisons, MaximumConflictComparisonsLimit);

        MaximumRegistrationBytes = maximumRegistrationBytes;
        MaximumGraphBytes = maximumGraphBytes;
        MaximumWorkPlanBytes = maximumWorkPlanBytes;
        MaximumCommandStagingBytes = maximumCommandStagingBytes;
        MaximumOwnedStateBytes = maximumOwnedStateBytes;
        MaximumResourceAndSystemStateBytes = Math.Min(MaximumResourceAndSystemStateBytesLimit, maximumOwnedStateBytes);
        MaximumDiagnosticBytes = maximumDiagnosticBytes;
        MaximumConflictComparisons = maximumConflictComparisons;
    }

    /// <summary>Gets the default budgets, which are also the supported ceilings.</summary>
    public static WorldSchedulingLimits Default { get; } = new(MaximumRegistrationBytesLimit, MaximumGraphBytesLimit, MaximumWorkPlanBytesLimit, MaximumCommandStagingBytesLimit, MaximumOwnedStateBytesLimit, MaximumDiagnosticBytesLimit, MaximumConflictComparisonsLimit);

    /// <summary>Gets the maximum command staging bytes.</summary>
    public long MaximumCommandStagingBytes { get; }

    /// <summary>Gets the maximum conflict comparisons.</summary>
    public int MaximumConflictComparisons { get; }

    /// <summary>Gets the maximum retained scheduler diagnostic bytes.</summary>
    public long MaximumDiagnosticBytes { get; }

    /// <summary>Gets the maximum graph and cycle-diagnostic bytes.</summary>
    public long MaximumGraphBytes { get; }

    /// <summary>Gets the maximum shared World-owned state bytes.</summary>
    public long MaximumOwnedStateBytes { get; }

    /// <summary>Gets the maximum registration metadata bytes.</summary>
    public long MaximumRegistrationBytes { get; }

    /// <summary>Gets the derived resource and system-state subceiling.</summary>
    public long MaximumResourceAndSystemStateBytes { get; }

    /// <summary>Gets the maximum current and candidate work-plan bytes.</summary>
    public long MaximumWorkPlanBytes { get; }

    private static void ValidateBytes(long value, long maximum, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, MinimumByteBudget, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, maximum, parameterName);
    }
}
