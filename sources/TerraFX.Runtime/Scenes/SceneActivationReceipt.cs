// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Scenes;

/// <summary>Reports one atomic Scene activation outcome.</summary>
public sealed class SceneActivationReceipt
{
    internal SceneActivationReceipt(
        SceneActivationStatus status,
        SceneInstance? instance,
        long? committedStructuralRevision,
        ImmutableArray<Diagnostic> diagnostics)
    {
        if (status is SceneActivationStatus.Activated)
        {
            ArgumentNullException.ThrowIfNull(instance);

            if (!committedStructuralRevision.HasValue)
            {
                throw new ArgumentException("An activated Scene must have a committed structural revision.", nameof(committedStructuralRevision));
            }

            if (diagnostics.IsDefault || !diagnostics.IsEmpty)
            {
                throw new ArgumentException("An activated Scene must have initialized empty diagnostics.", nameof(diagnostics));
            }
        }
        else if (status is SceneActivationStatus.Rejected)
        {
            if ((instance is not null) || committedStructuralRevision.HasValue || diagnostics.IsDefaultOrEmpty)
            {
                throw new ArgumentException("A rejected Scene must have diagnostics and no instance or committed revision.");
            }

            foreach (var diagnostic in diagnostics)
            {
                ArgumentNullException.ThrowIfNull(diagnostic);
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        Status = status;
        Instance = instance;
        CommittedStructuralRevision = committedStructuralRevision;
        Diagnostics = [.. diagnostics];
    }

    /// <summary>Gets the committed revision for an activated Scene, or null when activation was rejected.</summary>
    public long? CommittedStructuralRevision { get; }

    /// <summary>Gets the immutable diagnostics for the activation result.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Gets the activated Scene instance, or null when activation was rejected.</summary>
    public SceneInstance? Instance { get; }

    /// <summary>Gets whether the Scene was activated or rejected.</summary>
    public SceneActivationStatus Status { get; }
}
