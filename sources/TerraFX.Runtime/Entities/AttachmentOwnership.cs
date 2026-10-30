// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Identifies the caller or World ownership of one attachment value.</summary>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "The reviewed ownership contract uses only CallerOwned=1 and Transferred=2; zero is intentionally invalid.")]
public enum AttachmentOwnership
{
    /// <summary>The caller retains value lifetime and the attachment is transient.</summary>
    CallerOwned = 1,

    /// <summary>The World owns an immutable registered attachment installation.</summary>
    Transferred = 2,
}
