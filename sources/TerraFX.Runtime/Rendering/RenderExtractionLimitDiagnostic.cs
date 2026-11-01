// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Rendering;

/// <summary>Reports the exact full demand and configured limit for one rejected extraction.</summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and comparison operators on value types",
    Justification = "Callers inspect the exact limit kind, requested count, and limit; the contract does not define diagnostic value equality.")]
public readonly struct RenderExtractionLimitDiagnostic
{
    /// <summary>Initializes one capacity diagnostic.</summary>
    /// <param name="limitKind">The capacity that was exceeded.</param>
    /// <param name="requested">The complete requested count.</param>
    /// <param name="limit">The configured positive capacity.</param>
    /// <exception cref="ArgumentOutOfRangeException">The kind or counts are invalid.</exception>
    public RenderExtractionLimitDiagnostic(RenderExtractionLimitKind limitKind, int requested, int limit)
    {
        if (!Enum.IsDefined(limitKind))
        {
            throw new ArgumentOutOfRangeException(nameof(limitKind));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requested);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        if (requested <= limit)
        {
            throw new ArgumentOutOfRangeException(nameof(requested), "The requested count must exceed the configured limit.");
        }

        LimitKind = limitKind;
        Requested = requested;
        Limit = limit;
    }

    /// <summary>Gets the configured positive capacity.</summary>
    public int Limit { get; }

    /// <summary>Gets the exceeded capacity kind.</summary>
    public RenderExtractionLimitKind LimitKind { get; }

    /// <summary>Gets the complete requested count.</summary>
    public int Requested { get; }
}
