// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using TerraFX.Entities;

namespace TerraFX.Animation;

[SuppressMessage(
    "Performance",
    "CA1815:Override equals and comparison operators on value types",
    Justification = "RootMotionSourceAuthor is an immutable symbolic row; value equality is not part of its provider contract.")]
internal readonly struct RootMotionSourceAuthor
{
    private readonly Guid _clipSlotId;
    private readonly WorldAssetReference _clip;
    private readonly long _playSequence;
    private readonly long _loopOrdinal;
    private readonly float _weight;

    internal RootMotionSourceAuthor(
        Guid clipSlotId,
        WorldAssetReference clip,
        long playSequence,
        long loopOrdinal,
        float weight)
    {
        if (clipSlotId == Guid.Empty)
        {
            throw new ArgumentException("A root-motion source ClipSlotId cannot be empty.", nameof(clipSlotId));
        }

        if (!clip.IsValid || (clip.SchemaMajor != 1))
        {
            throw new ArgumentException("A root-motion source must select a valid native/clip major-1 reference.", nameof(clip));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playSequence);

        if (!float.IsFinite(weight) || (weight <= 0.0f) || (weight > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(weight), "A root-motion source weight must be finite and in (0,1].");
        }

        _clipSlotId = clipSlotId;
        _clip = clip;
        _playSequence = playSequence;
        _loopOrdinal = loopOrdinal;
        _weight = weight;
    }

    internal WorldAssetReference Clip
    {
        get
        {
            ThrowIfInvalid();
            return _clip;
        }
    }

    internal Guid ClipSlotId
    {
        get
        {
            ThrowIfInvalid();
            return _clipSlotId;
        }
    }

    internal bool IsValid
        => (_clipSlotId != Guid.Empty)
        && _clip.IsValid
        && (_clip.SchemaMajor == 1)
        && (_playSequence > 0)
        && float.IsFinite(_weight)
        && (_weight > 0.0f)
        && (_weight <= 1.0f);

    internal long LoopOrdinal
    {
        get
        {
            ThrowIfInvalid();
            return _loopOrdinal;
        }
    }

    internal long PlaySequence
    {
        get
        {
            ThrowIfInvalid();
            return _playSequence;
        }
    }

    internal float Weight
    {
        get
        {
            ThrowIfInvalid();
            return _weight;
        }
    }

    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("The RootMotionSourceAuthor is uninitialized or invalid.");
        }
    }
}
