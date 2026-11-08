// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Platform;

/// <summary>Reports whether a native window was created.</summary>
public enum WindowCreationStatus
{
    /// <summary>The native window and its host mapping were created.</summary>
    Created = 0,

    /// <summary>The request was rejected without creating a window.</summary>
    Rejected = 1,
}
