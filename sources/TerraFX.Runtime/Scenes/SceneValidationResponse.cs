// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Scenes;

/// <summary>Reports a non-reserving preview of one SceneTransaction.</summary>
public sealed class SceneValidationResponse
{
    internal SceneValidationResponse(
        SceneValidationStatus status,
        ImmutableArray<Diagnostic> diagnostics,
        long baseStructuralRevision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseStructuralRevision);

        if (status is SceneValidationStatus.Valid)
        {
            if (diagnostics.IsDefault || !diagnostics.IsEmpty)
            {
                throw new ArgumentException("A valid Scene transaction preview requires initialized empty diagnostics.", nameof(diagnostics));
            }
        }
        else if (status is SceneValidationStatus.Rejected)
        {
            if (diagnostics.IsDefaultOrEmpty)
            {
                throw new ArgumentException("A rejected Scene transaction preview requires diagnostics.", nameof(diagnostics));
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Status = status;
        Diagnostics = [.. diagnostics];
        BaseStructuralRevision = baseStructuralRevision;
    }

    /// <summary>Gets the World revision against which this non-reserving preview was evaluated.</summary>
    public long BaseStructuralRevision { get; }

    /// <summary>Gets the copied validation diagnostics.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Gets whether the transaction is valid against the current World state.</summary>
    public SceneValidationStatus Status { get; }
}
