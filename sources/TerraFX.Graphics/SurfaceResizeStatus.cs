// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Reports how a native surface resize request was applied.</summary>
public enum SurfaceResizeStatus
{
    /// <summary>A drawable swapchain was resized or created.</summary>
    Resized = 0,

    /// <summary>The surface has a zero drawable extent and no active swapchain.</summary>
    Suspended = 1,

    /// <summary>The request was rejected without changing the active epochs.</summary>
    Rejected = 2,
}
