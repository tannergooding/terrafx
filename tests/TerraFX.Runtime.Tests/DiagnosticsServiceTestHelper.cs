// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

internal static class DiagnosticsServiceTestHelper
{
    internal static (DiagnosticsService Service, WorldDiagnosticsServiceOwner Owner) CreateHostOwner()
    {
        var configuration = WorldDiagnosticsServiceOwner.GetConfiguration(
            DiagnosticsService.MaximumServiceByteCapacity,
            maximumPendingAttributionReservations: 1);
        var service = new DiagnosticsService(
            configuration.MaximumRecords,
            configuration.DiagnosticsMaximumBytes);
        var owner = new WorldDiagnosticsServiceOwner(
            service,
            configuration.MaximumRecords,
            maximumPendingAttributionReservations: 1,
            ownsService: false);
        return (service, owner);
    }

    internal static WorldSchedulingLimits CreateSchedulingLimitsWithDiagnosticBudget(long maximumDiagnosticBytes)
    {
        var defaults = WorldSchedulingLimits.Default;
        return new WorldSchedulingLimits(
            defaults.MaximumRegistrationBytes,
            defaults.MaximumGraphBytes,
            defaults.MaximumWorkPlanBytes,
            defaults.MaximumCommandStagingBytes,
            defaults.MaximumOwnedStateBytes,
            maximumDiagnosticBytes,
            defaults.MaximumConflictComparisons);
    }
}
