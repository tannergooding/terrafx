// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Scenes;

/// <summary>Addresses one authored Entity relative to the root occurrence of a Scene instance.</summary>
public sealed class SceneEntityAddress
{
    private readonly Guid _authoredEntityId;
    private readonly ImmutableArray<Guid> _placementPath;

    /// <summary>Initializes an immutable root-relative Scene Entity address.</summary>
    /// <param name="placementPath">The downward placement path from the root Scene occurrence.</param>
    /// <param name="authoredEntityId">The nonempty authored Entity identity in that occurrence.</param>
    /// <exception cref="ArgumentException">The path is default or contains an empty placement identity.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The path exceeds the supported nesting depth.</exception>
    public SceneEntityAddress(ImmutableArray<Guid> placementPath, Guid authoredEntityId)
    {
        if (placementPath.IsDefault)
        {
            throw new ArgumentException("The placement path must be initialized; use an empty array for a root Entity.", nameof(placementPath));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(placementPath.Length, 32, nameof(placementPath));
        ArgumentOutOfRangeException.ThrowIfEqual(authoredEntityId, Guid.Empty);

        foreach (var placementId in placementPath)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(placementId, Guid.Empty);
        }

        _placementPath = [.. placementPath];
        _authoredEntityId = authoredEntityId;
    }

    /// <summary>Gets the authored Entity identity.</summary>
    public Guid AuthoredEntityId => _authoredEntityId;

    /// <summary>Gets the copied root-relative downward placement path.</summary>
    public ImmutableArray<Guid> PlacementPath => _placementPath;
}
