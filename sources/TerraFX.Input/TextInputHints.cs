// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Input;

/// <summary>Provides non-authoritative native text-entry hints.</summary>
[Flags]
public enum TextInputHints
{
    /// <summary>No text-entry hints.</summary>
    None = 0,

    /// <summary>Allow native spelling assistance when available.</summary>
    SpellCheck = 1,

    /// <summary>Allow native autocorrection when available.</summary>
    AutoCorrect = 2,

    /// <summary>The text model supports multiple lines.</summary>
    Multiline = 4,

    /// <summary>Redact text payloads from persistent recording.</summary>
    Sensitive = 8
}
