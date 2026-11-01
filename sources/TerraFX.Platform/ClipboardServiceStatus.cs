// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Platform;

/// <summary>Defines the public clipboard capability state of one platform host.</summary>
public enum ClipboardServiceStatus
{
    /// <summary>Clipboard access was not requested.</summary>
    NotRequested = 0,

    /// <summary>The clipboard text service is available.</summary>
    Ready = 1,

    /// <summary>The clipboard text service is unavailable for the selected platform.</summary>
    Unsupported = 2,
}
