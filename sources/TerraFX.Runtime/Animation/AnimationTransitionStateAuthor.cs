// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using TerraFX.Entities;

namespace TerraFX.Animation;

internal sealed class AnimationTransitionStateAuthor
{
    private readonly long _elapsedTicks;
    private readonly ushort _fraction;
    private readonly ImmutableArray<float> _sourceWeights;
    private readonly Guid _targetClipSlotId;
    private readonly Guid _transitionId;

    internal AnimationTransitionStateAuthor(
        Guid transitionId,
        long elapsedTicks,
        ushort fraction,
        ImmutableArray<float> sourceWeights,
        Guid targetClipSlotId)
        : this(transitionId, elapsedTicks, fraction, sourceWeights, targetClipSlotId, takeOwnership: false)
    {
    }

    private AnimationTransitionStateAuthor(
        Guid transitionId,
        long elapsedTicks,
        ushort fraction,
        ImmutableArray<float> sourceWeights,
        Guid targetClipSlotId,
        bool takeOwnership)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(transitionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedTicks);

        if (fraction > 999)
        {
            throw new ArgumentOutOfRangeException(nameof(fraction));
        }

        ArgumentOutOfRangeException.ThrowIfEqual(targetClipSlotId, Guid.Empty);

        if (sourceWeights.IsDefault || (sourceWeights.Length is < 1 or > 4))
        {
            throw new ArgumentException("Animation transition source weights must be initialized with one to four entries.", nameof(sourceWeights));
        }

        _sourceWeights = takeOwnership ? sourceWeights : ImmutableArrayCopy.Create(sourceWeights);

        foreach (var weight in _sourceWeights)
        {
            if (!float.IsFinite(weight) || (weight < 0.0f) || (weight > 1.0f))
            {
                throw new ArgumentException("Animation transition source weights must be finite and in [0,1].", nameof(sourceWeights));
            }
        }

        _transitionId = transitionId;
        _elapsedTicks = elapsedTicks;
        _fraction = fraction;
        _targetClipSlotId = targetClipSlotId;
    }

    internal long ElapsedTicks => _elapsedTicks;

    internal ushort Fraction => _fraction;

    internal ImmutableArray<float> SourceWeights => _sourceWeights;

    internal Guid TargetClipSlotId => _targetClipSlotId;

    internal Guid TransitionId => _transitionId;

    internal static AnimationTransitionStateAuthor CreateOwned(
        Guid transitionId,
        long elapsedTicks,
        ushort fraction,
        float[] sourceWeights,
        Guid targetClipSlotId)
    {
        ArgumentNullException.ThrowIfNull(sourceWeights);
        return new AnimationTransitionStateAuthor(
            transitionId,
            elapsedTicks,
            fraction,
            ImmutableCollectionsMarshal.AsImmutableArray(sourceWeights),
            targetClipSlotId,
            takeOwnership: true);
    }
}
