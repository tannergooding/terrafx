// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Scenes;

/// <summary>Identifies the owner-bound outcome of one SceneTransaction.</summary>
public enum SceneTransactionStatus
{
    /// <summary>All staged operations committed atomically.</summary>
    Committed,

    /// <summary>The staged operations were rejected without observable mutation.</summary>
    Rejected,
}
