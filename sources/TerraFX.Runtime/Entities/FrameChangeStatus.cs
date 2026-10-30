// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Reports whether a standalone frame-change transaction installed its candidate.</summary>
public enum FrameChangeStatus
{
    /// <summary>The complete frame cohort was installed.</summary>
    Applied = 0,

    /// <summary>The candidate was refused without changing the current frame.</summary>
    Rejected = 1,
}
