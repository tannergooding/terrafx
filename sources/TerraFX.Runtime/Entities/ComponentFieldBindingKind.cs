// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal enum ComponentFieldBindingKind
{
    None = 0,
    Aggregate = 1,
    EntityReference = 2,
    FixedArray = 3,
    Optional = 4,
    OptionalAggregate = 5,
    OptionalEntityReference = 6,
    OptionalStableGuidReference = 7,
    Scalar = 8,
    StableGuidReference = 9,
}
