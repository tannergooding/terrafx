// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Scenes;

/// <summary>Reports one queued Scene unload outcome.</summary>
public sealed class SceneUnloadReceipt
{
    internal SceneUnloadReceipt(
        SceneUnloadStatus status,
        Guid sceneInstanceId,
        long? structuralRevision,
        ImmutableArray<Diagnostic> diagnostics)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);

        if (status is SceneUnloadStatus.Unloaded or SceneUnloadStatus.AlreadyUnloaded)
        {
            if (!structuralRevision.HasValue || diagnostics.IsDefault || !diagnostics.IsEmpty)
            {
                throw new ArgumentException("A completed Scene unload must report its structural revision and initialized empty diagnostics.");
            }
        }
        else if (status is SceneUnloadStatus.Rejected)
        {
            if (structuralRevision.HasValue || diagnostics.IsDefaultOrEmpty)
            {
                throw new ArgumentException("A rejected Scene unload must report diagnostics and no structural revision.");
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
        SceneInstanceId = sceneInstanceId;
        StructuralRevision = structuralRevision;
        Diagnostics = [.. diagnostics];
    }

    /// <summary>Gets the diagnostics for a rejected unload, or an initialized empty array otherwise.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    /// <summary>Gets the stable identity of the Scene instance targeted by the unload.</summary>
    public Guid SceneInstanceId { get; }

    /// <summary>Gets whether the Scene was unloaded, already absent, or rejected.</summary>
    public SceneUnloadStatus Status { get; }

    /// <summary>Gets the committed revision for a completed unload, or null when it was rejected.</summary>
    public long? StructuralRevision { get; }
}
