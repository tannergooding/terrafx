// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Scenes;

/// <summary>Reports one queued SceneTransaction outcome.</summary>
public sealed class SceneTransactionReceipt
{
    internal SceneTransactionReceipt(
        SceneTransactionStatus status,
        long? committedStructuralRevision,
        ImmutableArray<Diagnostic> diagnostics)
    {
        if (status is SceneTransactionStatus.Committed)
        {
            if (!committedStructuralRevision.HasValue || diagnostics.IsDefault || !diagnostics.IsEmpty)
            {
                throw new ArgumentException("A committed Scene transaction requires a revision and initialized empty diagnostics.");
            }
        }
        else if (status is SceneTransactionStatus.Rejected)
        {
            if (committedStructuralRevision.HasValue || diagnostics.IsDefaultOrEmpty)
            {
                throw new ArgumentException("A rejected Scene transaction requires diagnostics and no committed revision.");
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Status = status;
        CommittedStructuralRevision = committedStructuralRevision;
        Diagnostics = [.. diagnostics];
    }

    /// <summary>Gets the committed revision for a successful transaction, or null when rejected.</summary>
    public long? CommittedStructuralRevision { get; }

    /// <summary>Gets the copied rejection diagnostics, or an initialized empty array on success.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Gets whether the queued transaction committed or was rejected.</summary>
    public SceneTransactionStatus Status { get; }
}
