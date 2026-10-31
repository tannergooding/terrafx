// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Identifies one independent input recipient clock domain.</summary>
public enum InputRecipientKind
{
    /// <summary>A window-scoped recipient evaluated on the host control clock.</summary>
    HostLiveUI = 0,

    /// <summary>A recipient evaluated against one simulation timeline.</summary>
    SimulationTimeline = 1,
}
