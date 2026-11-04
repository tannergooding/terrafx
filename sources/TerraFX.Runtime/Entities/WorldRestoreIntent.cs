// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Defines the explicit identity and replay behavior selected for a persistence restore.</summary>
public enum WorldRestoreIntent
{
    /// <summary>No restore intent was supplied.</summary>
    None = 0,

    /// <summary>Continues an admitted finite StrictLocal replay interval.</summary>
    RecordedContinuation = 1,

    /// <summary>Retains the checkpoint identity history and selects a future identity branch.</summary>
    Branch = 2,

    /// <summary>Creates a fresh World identity and transforms the complete source identity history.</summary>
    IndependentFork = 3,
}
