// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Platform;

/// <summary>Controls whether the host requests native clipboard text.</summary>
public enum ClipboardServicePolicy
{
    /// <summary>Do not request clipboard access.</summary>
    NotRequested,

    /// <summary>Request clipboard access when available, without requiring it for startup.</summary>
    Optional,

    /// <summary>Require clipboard access for host startup.</summary>
    Required,
}
