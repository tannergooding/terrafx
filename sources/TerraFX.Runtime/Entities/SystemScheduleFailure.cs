// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal enum SystemScheduleFailure
{
    Unknown = 0,
    SystemCountExceeded,
    AccessCountExceeded,
    DependencyCountExceeded,
    GraphBudgetExceeded,
    DuplicateSystemId,
    DuplicateSystemOrder,
    UnknownDependency,
    BackwardStageDependency,
    ConflictBudgetExceeded,
    DependencyCycle,
    ResourceCountExceeded,
    DuplicateResourceId,
    UnknownResource,
    ResourcePartitionRangeExceeded,
    ParallelResourceAccessDenied,
}
