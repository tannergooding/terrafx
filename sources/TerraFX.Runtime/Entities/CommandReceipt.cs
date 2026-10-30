// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Entities;

/// <summary>Contains the immutable results of one admitted command-writer prefix.</summary>
public sealed class CommandReceipt
{
    private readonly ImmutableArray<bool> _canceled;
    private readonly ImmutableArray<Entity> _resolvedEntities;
    private readonly ImmutableArray<EntityToken> _tokens;

    internal CommandReceipt(
        ImmutableArray<EntityToken> tokens,
        ImmutableArray<Entity> resolvedEntities,
        ImmutableArray<bool> canceled,
        ImmutableArray<WorldEntityIdentity> destroyedEntityIdentities)
    {
        if (tokens.IsDefault || resolvedEntities.IsDefault || canceled.IsDefault || destroyedEntityIdentities.IsDefault)
        {
            throw new ArgumentException("Command receipt arrays must be initialized.");
        }

        if ((tokens.Length != resolvedEntities.Length) || (tokens.Length != canceled.Length))
        {
            throw new ArgumentException("Command token results must have matching lengths.", nameof(resolvedEntities));
        }

        _tokens = ImmutableArrayCopy.Create(tokens);
        _resolvedEntities = ImmutableArrayCopy.Create(resolvedEntities);
        _canceled = ImmutableArrayCopy.Create(canceled);
        DestroyedEntityIdentities = ImmutableArrayCopy.Create(destroyedEntityIdentities);
    }

    /// <summary>Gets the qualified identities destroyed by this receipt, in receipt-local structural order.</summary>
    public ImmutableArray<WorldEntityIdentity> DestroyedEntityIdentities { get; }

    /// <summary>Resolves one Create token owned by this exact command receipt.</summary>
    /// <param name="token">The temporary Create token.</param>
    /// <param name="entity">The originally assigned live handle, or exactly default for a canceled Create.</param>
    /// <returns><see langword="true"/> when the token's Create survived playback.</returns>
    /// <exception cref="ArgumentException"><paramref name="token"/> is invalid or belongs to another receipt.</exception>
    public bool TryResolve(EntityToken token, out Entity entity)
    {
        if (!token.IsValid)
        {
            throw new ArgumentException("A valid Create token is required.", nameof(token));
        }

        for (var index = 0; index < _tokens.Length; index++)
        {
            if (_tokens[index] != token)
            {
                continue;
            }

            entity = _canceled[index] ? default : _resolvedEntities[index];
            return !_canceled[index];
        }

        throw new ArgumentException("The Create token belongs to another command receipt.", nameof(token));
    }

    internal bool Contains(EntityToken token)
    {
        foreach (var candidate in _tokens)
        {
            if (candidate == token)
            {
                return true;
            }
        }

        return false;
    }
}
