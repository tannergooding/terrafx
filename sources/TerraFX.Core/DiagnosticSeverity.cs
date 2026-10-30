// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Identifies the severity of one diagnostic record.</summary>
public enum DiagnosticSeverity
{
    /// <summary>Detailed diagnostic information.</summary>
    Trace = 0,

    /// <summary>Informational diagnostic information.</summary>
    Information = 1,

    /// <summary>A recoverable warning.</summary>
    Warning = 2,

    /// <summary>An operation error.</summary>
    Error = 3,

    /// <summary>A critical fault.</summary>
    Critical = 4,
}
