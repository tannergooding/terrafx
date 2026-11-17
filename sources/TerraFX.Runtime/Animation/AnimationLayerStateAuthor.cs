// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Animation;

internal sealed class AnimationLayerStateAuthor
{
    private readonly ImmutableArray<AnimationActiveClipAuthor> _clips;
    private readonly byte _index;
    private readonly AnimationTransitionStateAuthor? _transition;
    private readonly float _weight;

    internal AnimationLayerStateAuthor(
        byte index,
        float weight,
        ImmutableArray<AnimationActiveClipAuthor> clips,
        AnimationTransitionStateAuthor? transition)
    {
        if (index > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (!float.IsFinite(weight) || (weight < 0.0f) || (weight > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(weight), "Animation layer weight must be finite and in [0,1].");
        }

        _clips = AnimationRuntimeAuthorValidation.CopyRequired(clips, 0, 4, nameof(clips));

        for (var clipIndex = 0; clipIndex < _clips.Length; clipIndex++)
        {
            for (var previousIndex = 0; previousIndex < clipIndex; previousIndex++)
            {
                if (_clips[previousIndex].ClipSlotId == _clips[clipIndex].ClipSlotId)
                {
                    throw new ArgumentException("An Animation layer cannot contain the same ClipSlotId more than once.", nameof(clips));
                }
            }
        }

        _index = index;
        _weight = weight;
        _transition = transition;
    }

    internal ImmutableArray<AnimationActiveClipAuthor> Clips => _clips;

    internal byte Index => _index;

    internal AnimationTransitionStateAuthor? Transition => _transition;

    internal float Weight => _weight;
}
