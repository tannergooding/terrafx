// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Identifies engine-defined system resources.</summary>
public static class WorldSystemResources
{
    /// <summary>Gets the fixed read-only World entity-membership capability identity.</summary>
    public static Guid EntityMembershipId { get; } = new("5ae0d511-7e5f-81df-bb07-e2b259892443");
}
