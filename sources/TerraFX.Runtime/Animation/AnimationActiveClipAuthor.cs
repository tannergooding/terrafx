// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Animation;

internal sealed class AnimationActiveClipAuthor
{
    private readonly Guid _clipSlotId;
    private readonly bool _completed;
    private readonly uint _forwardCursor;
    private readonly AnimationTickRemainder192 _fraction;
    private readonly sbyte _lastDirection;
    private readonly long _localTicks;
    private readonly long _loopOrdinal;
    private readonly long _playSequence;
    private readonly long _previousLoopOrdinal;
    private readonly long _previousTicks;
    private readonly uint _reverseCursor;
    private readonly bool _startDelivered;
    private readonly float _weight;

    internal AnimationActiveClipAuthor(
        Guid clipSlotId,
        long playSequence,
        long localTicks,
        long previousTicks,
        AnimationTickRemainder192 fraction,
        long loopOrdinal,
        long previousLoopOrdinal,
        float weight,
        uint forwardCursor,
        uint reverseCursor,
        bool startDelivered,
        bool completed,
        sbyte lastDirection)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(clipSlotId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playSequence);
        ArgumentOutOfRangeException.ThrowIfNegative(localTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(previousTicks);

        if (!float.IsFinite(weight) || (weight < 0.0f) || (weight > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(weight), "Animation clip weight must be finite and in [0,1].");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(forwardCursor, 1_048_576u);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(reverseCursor, 1_048_576u);

        if (lastDirection is not -1 and not 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lastDirection));
        }

        _clipSlotId = clipSlotId;
        _playSequence = playSequence;
        _localTicks = localTicks;
        _previousTicks = previousTicks;
        _fraction = fraction;
        _loopOrdinal = loopOrdinal;
        _previousLoopOrdinal = previousLoopOrdinal;
        _weight = weight;
        _forwardCursor = forwardCursor;
        _reverseCursor = reverseCursor;
        _startDelivered = startDelivered;
        _completed = completed;
        _lastDirection = lastDirection;
    }

    internal Guid ClipSlotId => _clipSlotId;

    internal bool Completed => _completed;

    internal uint ForwardCursor => _forwardCursor;

    internal AnimationTickRemainder192 Fraction => _fraction;

    internal sbyte LastDirection => _lastDirection;

    internal long LocalTicks => _localTicks;

    internal long LoopOrdinal => _loopOrdinal;

    internal long PlaySequence => _playSequence;

    internal long PreviousLoopOrdinal => _previousLoopOrdinal;

    internal long PreviousTicks => _previousTicks;

    internal uint ReverseCursor => _reverseCursor;

    internal bool StartDelivered => _startDelivered;

    internal float Weight => _weight;

    internal byte GetFractionByte(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, AnimationTickRemainder192.ByteLength);
        var word = index switch {
            < sizeof(ulong) => _fraction.Low,
            < (2 * sizeof(ulong)) => _fraction.Middle,
            _ => _fraction.High,
        };
        var shift = index % sizeof(ulong) * 8;
        return unchecked((byte)(word >> shift));
    }
}
