// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Input;

/// <summary>Provides diagnostic flags for one captured physical input event.</summary>
[Flags]
public enum PhysicalInputEventDiagnostic
{
    /// <summary>No diagnostic flags apply.</summary>
    None = 0,

    /// <summary>The native timestamp was invalid, regressing, or wrapped.</summary>
    TimestampUncertain = 1,
}
