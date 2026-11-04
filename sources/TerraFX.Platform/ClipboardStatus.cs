// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Platform;

/// <summary>Defines the result of one native clipboard text operation.</summary>
public enum ClipboardStatus
{
    /// <summary>The complete text payload was copied successfully.</summary>
    Completed = 0,

    /// <summary>The clipboard has no supported text selection.</summary>
    NoSelection = 1,

    /// <summary>The selected platform does not provide clipboard text.</summary>
    Unsupported = 2,

    /// <summary>The platform refused access to the clipboard selection.</summary>
    PermissionDenied = 3,

    /// <summary>The selection owner changed before the requested transfer completed.</summary>
    SelectionExpired = 4,

    /// <summary>The native transfer exceeded its bounded completion interval.</summary>
    TimedOut = 5,

    /// <summary>The native text payload is malformed.</summary>
    InvalidData = 6,

    /// <summary>The complete text payload exceeds the request's byte limit.</summary>
    TooLarge = 7,

    /// <summary>The native protocol canceled the transfer.</summary>
    Canceled = 8,
}
