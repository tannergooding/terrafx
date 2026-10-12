// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies the kind of resource named by a system access declaration.</summary>
public enum SystemResourceKind
{
    /// <summary>No resource kind; valid only as a default value.</summary>
    None = 0,

    /// <summary>A component schema.</summary>
    Component = 1,

    /// <summary>A World-owned resource.</summary>
    Resource = 2,

    /// <summary>An entity tag schema.</summary>
    Tag = 3,

    /// <summary>The fixed read-only World entity-membership capability.</summary>
    EntityMembership = 4,
}
