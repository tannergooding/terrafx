// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Animation;

/// <summary>Identifies one controller transition requested for an Animation player.</summary>
public sealed class AnimationTransitionRequest
{
    /// <summary>Initializes a request for a nonempty transition identity.</summary>
    /// <param name="transitionId">The authored transition identity.</param>
    /// <exception cref="ArgumentException"><paramref name="transitionId"/> is empty.</exception>
    public AnimationTransitionRequest(Guid transitionId)
    {
        if (transitionId == Guid.Empty)
        {
            throw new ArgumentException("An Animation transition identity cannot be empty.", nameof(transitionId));
        }

        TransitionId = transitionId;
    }

    /// <summary>Gets the authored transition identity.</summary>
    public Guid TransitionId { get; }
}
