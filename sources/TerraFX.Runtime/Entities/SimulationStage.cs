// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Identifies an ordered stage in one simulation tick.</summary>
public enum SimulationStage
{
    /// <summary>Drains accepted owner commands and freezes tick inputs/control state.</summary>
    Begin = 0,

    /// <summary>Consumes normalized events and evaluates gameplay actions.</summary>
    Input = 1,

    /// <summary>Samples animation and application motion requests.</summary>
    Animation = 2,

    /// <summary>Resolves dirty transform hierarchies.</summary>
    Transforms = 3,

    /// <summary>Admits body commands and solves staged physics state.</summary>
    Physics = 4,

    /// <summary>Copies solved body poses into owned transforms and resolves descendants.</summary>
    PoseSync = 5,

    /// <summary>Delivers copied tick-local events to late systems.</summary>
    Events = 6,

    /// <summary>Preflights and commits deferred structural commands.</summary>
    Structural = 7,

    /// <summary>Prepares immutable observation outputs without external publication.</summary>
    Extract = 8,

    /// <summary>Publishes the successful tick and enables external observation delivery.</summary>
    Commit = 9,
}
