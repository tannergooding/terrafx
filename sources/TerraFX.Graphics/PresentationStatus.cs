// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Reports the terminal outcome of one native presentation.</summary>
public enum PresentationStatus
{
    /// <summary>The selected image was presented to the display.</summary>
    Presented,

    /// <summary>The presentation was occluded and was not displayed.</summary>
    Occluded,

    /// <summary>The Surface was suspended before the image could be presented.</summary>
    Suspended,

    /// <summary>The GraphicsDevice was lost before presentation completed.</summary>
    DeviceLost,

    /// <summary>The native Surface was lost before presentation completed.</summary>
    SurfaceLost,

    /// <summary>The swapchain generation was lost before presentation completed.</summary>
    SwapchainLost,
}
