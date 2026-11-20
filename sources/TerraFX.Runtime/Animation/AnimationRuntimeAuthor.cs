// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Animation;

internal sealed class AnimationRuntimeAuthor
{
    private readonly long _nextCommandSequence;
    private readonly ImmutableArray<AnimationPlayerStateAuthor> _players;

    internal AnimationRuntimeAuthor(long nextCommandSequence, ImmutableArray<AnimationPlayerStateAuthor> players)
        : this(nextCommandSequence, players, takeOwnership: false)
    {
    }

    private AnimationRuntimeAuthor(
        long nextCommandSequence,
        ImmutableArray<AnimationPlayerStateAuthor> players,
        bool takeOwnership)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nextCommandSequence);
        _players = takeOwnership
            ? players
            : AnimationRuntimeAuthorValidation.CopyRequired(players, 0, 4096, nameof(players));

        for (var index = 1; index < _players.Length; index++)
        {
            if (FoundationCatalogue.CompareRfc(_players[index - 1].ActorEntityId, _players[index].ActorEntityId) >= 0)
            {
                throw new ArgumentException("Animation players must be uniquely ordered by RFC ActorEntityId.", nameof(players));
            }
        }

        _nextCommandSequence = nextCommandSequence;
    }

    internal long NextCommandSequence => _nextCommandSequence;

    internal ImmutableArray<AnimationPlayerStateAuthor> Players => _players;

    internal static AnimationRuntimeAuthor CreateOwned(
        long nextCommandSequence,
        AnimationPlayerStateAuthor[] players)
    {
        var ownedPlayers = AnimationRuntimeAuthorValidation.TakeRequired(
            players,
            0,
            4096,
            nameof(players));
        return new AnimationRuntimeAuthor(nextCommandSequence, ownedPlayers, takeOwnership: true);
    }
}
