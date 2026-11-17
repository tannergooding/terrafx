// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using TerraFX.Assets;

namespace TerraFX.Animation;

/// <summary>Identifies one clip contribution to a copied root-motion request.</summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and comparison operators on value types",
    Justification = "RootMotionSource is an opaque issued contribution; its public contract does not define value equality.")]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct RootMotionSource
{
    private readonly Guid _clipSlotId;
    private readonly int _clipAssetId;
    private readonly long _playSequence;
    private readonly long _loopOrdinal;
    private readonly float _weight;

    internal RootMotionSource(
        Guid clipSlotId,
        int clipAssetId,
        long playSequence,
        long loopOrdinal,
        float weight)
    {
        if (clipSlotId == Guid.Empty)
        {
            throw new ArgumentException("An Animation clip-slot identity cannot be empty.", nameof(clipSlotId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(clipAssetId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playSequence);

        if (!float.IsFinite(weight) || (weight <= 0.0f) || (weight > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(weight), "A contributing root-motion weight must be finite and in (0, 1].");
        }

        _clipSlotId = clipSlotId;
        _clipAssetId = clipAssetId;
        _playSequence = playSequence;
        _loopOrdinal = loopOrdinal;
        _weight = weight;
    }

    /// <summary>Gets the versioned Clip identity.</summary>
    /// <exception cref="InvalidOperationException">This source is the uninitialized default value.</exception>
    public AssetId Clip
    {
        get
        {
            ThrowIfInvalid();
            return new AssetId(_clipAssetId);
        }
    }

    /// <summary>Gets the controller slot identity for the Clip.</summary>
    /// <exception cref="InvalidOperationException">This source is the uninitialized default value.</exception>
    public Guid ClipSlotId
    {
        get
        {
            ThrowIfInvalid();
            return _clipSlotId;
        }
    }

    /// <summary>Gets the Clip loop ordinal, which may be negative during reverse playback.</summary>
    /// <exception cref="InvalidOperationException">This source is the uninitialized default value.</exception>
    public long LoopOrdinal
    {
        get
        {
            ThrowIfInvalid();
            return _loopOrdinal;
        }
    }

    /// <summary>Gets the positive play sequence that owns this Clip contribution.</summary>
    /// <exception cref="InvalidOperationException">This source is the uninitialized default value.</exception>
    public long PlaySequence
    {
        get
        {
            ThrowIfInvalid();
            return _playSequence;
        }
    }

    /// <summary>Gets the positive normalized contribution weight.</summary>
    /// <exception cref="InvalidOperationException">This source is the uninitialized default value.</exception>
    public float Weight
    {
        get
        {
            ThrowIfInvalid();
            return _weight;
        }
    }

    internal bool IsValid
        => (_clipSlotId != Guid.Empty)
        && (_clipAssetId > 0)
        && (_playSequence > 0)
        && float.IsFinite(_weight)
        && (_weight > 0.0f)
        && (_weight <= 1.0f);

    internal bool IsZeroStorage
        => (_clipSlotId == Guid.Empty) && (_clipAssetId == 0) && (_playSequence == 0)
            && (_loopOrdinal == 0) && (BitConverter.SingleToInt32Bits(_weight) == 0);

    private void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("The RootMotionSource is uninitialized or invalid.");
        }
    }
}
