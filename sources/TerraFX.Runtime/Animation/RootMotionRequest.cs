// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Animation;

/// <summary>Contains copied root-motion data produced for one Animation tick.</summary>
[SuppressMessage(
    "Performance",
    "CA1815:Override equals and comparison operators on value types",
    Justification = "RootMotionRequest is an opaque issued request; its public contract does not define value equality.")]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct RootMotionRequest
{
    private const float MaximumUnitQuaternionLengthSquaredError = 2.0e-6f;

    private readonly byte _present;
    private readonly Guid _worldGuid;
    private readonly Guid _playerId;
    private readonly Guid _playerCreationLineageId;
    private readonly Guid _playerCreationBranchId;
    private readonly Guid _actorId;
    private readonly Guid _actorCreationLineageId;
    private readonly Guid _actorCreationBranchId;
    private readonly long _tickIndex;
    private readonly float _translationX;
    private readonly float _translationY;
    private readonly float _translationZ;
    private readonly float _rotationX;
    private readonly float _rotationY;
    private readonly float _rotationZ;
    private readonly float _rotationW;
    private readonly byte _extractionPolicy;
    private readonly byte _sourceCount;
    private readonly RootMotionSource _source0;
    private readonly RootMotionSource _source1;
    private readonly RootMotionSource _source2;
    private readonly RootMotionSource _source3;
    private readonly int _controllerAssetId;

    internal RootMotionRequest(
        WorldEntityIdentity player,
        WorldEntityIdentity actor,
        long tickIndex,
        Vector3 translation,
        Quaternion rotation,
        RootMotionExtractionPolicy extractionPolicy,
        scoped ReadOnlySpan<RootMotionSource> sources,
        int controllerAssetId)
    {
        if (!player.IsValid)
        {
            throw new ArgumentException("The root-motion Player identity must be valid.", nameof(player));
        }

        if (!actor.IsValid || (actor.WorldGuid != player.WorldGuid))
        {
            throw new ArgumentException("The root-motion Actor must be valid and belong to the Player's World.", nameof(actor));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(tickIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(controllerAssetId);

        if (!translation.IsFinite().All())
        {
            throw new ArgumentOutOfRangeException(nameof(translation), "Root-motion translation must be finite.");
        }

        var rotationLengthSquared = Quaternion.Dot(rotation, rotation);

        if (!rotation.IsFinite().All()
            || !float.IsFinite(rotationLengthSquared)
            || (MathF.Abs(rotationLengthSquared - 1.0f) > MaximumUnitQuaternionLengthSquaredError))
        {
            throw new ArgumentOutOfRangeException(nameof(rotation), "Root-motion rotation must be a finite unit quaternion.");
        }

        if (extractionPolicy is not RootMotionExtractionPolicy.Extract)
        {
            throw new ArgumentOutOfRangeException(nameof(extractionPolicy));
        }

        if (sources.Length > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(sources), "A root-motion request may contain at most four Clip sources.");
        }

        if ((sources.Length == 0)
            && (!IsPositiveZero(translation) || !IsIdentity(rotation)))
        {
            throw new ArgumentException("A request without contributing sources must contain explicit identity motion.");
        }

        _present = 1;
        _worldGuid = player.WorldGuid;
        _playerId = player.EntityId;
        _playerCreationLineageId = player.CreationLineageId;
        _playerCreationBranchId = player.CreationBranchId;
        _actorId = actor.EntityId;
        _actorCreationLineageId = actor.CreationLineageId;
        _actorCreationBranchId = actor.CreationBranchId;
        _tickIndex = tickIndex;
        _translationX = translation.X;
        _translationY = translation.Y;
        _translationZ = translation.Z;
        _rotationX = rotation.X;
        _rotationY = rotation.Y;
        _rotationZ = rotation.Z;
        _rotationW = rotation.W;
        _extractionPolicy = (byte)extractionPolicy;
        _sourceCount = checked((byte)sources.Length);
        _source0 = default;
        _source1 = default;
        _source2 = default;
        _source3 = default;
        _controllerAssetId = controllerAssetId;

        for (var index = 0; index < sources.Length; index++)
        {
            var source = sources[index];

            if (!source.IsValid)
            {
                throw new ArgumentException("Root-motion sources must be initialized and valid.", nameof(sources));
            }

            switch (index)
            {
                case 0:
                {
                    _source0 = source;
                    break;
                }

                case 1:
                {
                    _source1 = source;
                    break;
                }

                case 2:
                {
                    _source2 = source;
                    break;
                }

                case 3:
                {
                    _source3 = source;
                    break;
                }
            }
        }
    }

    /// <summary>Gets the qualified Actor that receives the copied motion.</summary>
    /// <exception cref="InvalidOperationException">This request is the absent default value.</exception>
    public WorldEntityIdentity Actor
    {
        get
        {
            ThrowIfAbsent();
            return new WorldEntityIdentity(
                _worldGuid,
                _actorCreationLineageId,
                _actorCreationBranchId,
                _actorId);
        }
    }

    /// <summary>Gets the policy applied when the request was extracted.</summary>
    /// <exception cref="InvalidOperationException">This request is the absent default value.</exception>
    public RootMotionExtractionPolicy ExtractionPolicy
    {
        get
        {
            ThrowIfAbsent();
            return (RootMotionExtractionPolicy)_extractionPolicy;
        }
    }

    /// <summary>Gets whether this value contains an issued root-motion request.</summary>
    public bool IsValid => _present == 1;

    /// <summary>Gets the qualified Player that produced the request.</summary>
    /// <exception cref="InvalidOperationException">This request is the absent default value.</exception>
    public WorldEntityIdentity Player
    {
        get
        {
            ThrowIfAbsent();
            return new WorldEntityIdentity(
                _worldGuid,
                _playerCreationLineageId,
                _playerCreationBranchId,
                _playerId);
        }
    }

    /// <summary>Gets the copied actor-local rotation delta.</summary>
    /// <exception cref="InvalidOperationException">This request is the absent default value.</exception>
    public Quaternion Rotation
    {
        get
        {
            ThrowIfAbsent();
            return new Quaternion(_rotationX, _rotationY, _rotationZ, _rotationW);
        }
    }

    /// <summary>Gets the number of ordered positive-weight Clip contributions.</summary>
    /// <exception cref="InvalidOperationException">This request is the absent default value.</exception>
    public int SourceCount
    {
        get
        {
            ThrowIfAbsent();
            return _sourceCount;
        }
    }

    /// <summary>Gets the tick for which this copied request was produced.</summary>
    /// <exception cref="InvalidOperationException">This request is the absent default value.</exception>
    public long TickIndex
    {
        get
        {
            ThrowIfAbsent();
            return _tickIndex;
        }
    }

    /// <summary>Gets the copied actor-local translation delta.</summary>
    /// <exception cref="InvalidOperationException">This request is the absent default value.</exception>
    public Vector3 Translation
    {
        get
        {
            ThrowIfAbsent();
            return new Vector3(_translationX, _translationY, _translationZ);
        }
    }

    internal int ControllerAssetId
    {
        get
        {
            ThrowIfAbsent();
            return _controllerAssetId;
        }
    }

    internal bool HasCanonicalStorage
    {
        get
        {
            if (_present == 0)
            {
                return (_worldGuid == Guid.Empty) && (_playerId == Guid.Empty)
                    && (_playerCreationLineageId == Guid.Empty) && (_playerCreationBranchId == Guid.Empty)
                    && (_actorId == Guid.Empty) && (_actorCreationLineageId == Guid.Empty)
                    && (_actorCreationBranchId == Guid.Empty) && (_tickIndex == 0)
                    && (BitConverter.SingleToInt32Bits(_translationX) == 0)
                    && (BitConverter.SingleToInt32Bits(_translationY) == 0)
                    && (BitConverter.SingleToInt32Bits(_translationZ) == 0)
                    && (BitConverter.SingleToInt32Bits(_rotationX) == 0)
                    && (BitConverter.SingleToInt32Bits(_rotationY) == 0)
                    && (BitConverter.SingleToInt32Bits(_rotationZ) == 0)
                    && (BitConverter.SingleToInt32Bits(_rotationW) == 0)
                    && (_extractionPolicy == 0) && (_sourceCount == 0) && (_controllerAssetId == 0)
                    && _source0.IsZeroStorage && _source1.IsZeroStorage && _source2.IsZeroStorage && _source3.IsZeroStorage;
            }

            if ((_present != 1) || (_worldGuid == Guid.Empty) || (_playerId == Guid.Empty)
                || (_playerCreationLineageId == Guid.Empty) || (_playerCreationBranchId == Guid.Empty)
                || (_actorId == Guid.Empty) || (_actorCreationLineageId == Guid.Empty)
                || (_actorCreationBranchId == Guid.Empty) || (_tickIndex < 0)
                || (_controllerAssetId <= 0) || (_sourceCount > 4)
                || (_extractionPolicy != (byte)RootMotionExtractionPolicy.Extract)
                || !Translation.IsFinite().All() || !Rotation.IsFinite().All())
            {
                return false;
            }

            var rotationLengthSquared = Quaternion.Dot(Rotation, Rotation);

            if (!float.IsFinite(rotationLengthSquared)
                || (MathF.Abs(rotationLengthSquared - 1.0f) > MaximumUnitQuaternionLengthSquaredError)
                || ((_sourceCount == 0) && (!IsPositiveZero(Translation) || !IsIdentity(Rotation))))
            {
                return false;
            }

            return ((_sourceCount > 0) ? _source0.IsValid : _source0.IsZeroStorage)
                && ((_sourceCount > 1) ? _source1.IsValid : _source1.IsZeroStorage)
                && ((_sourceCount > 2) ? _source2.IsValid : _source2.IsZeroStorage)
                && ((_sourceCount > 3) ? _source3.IsValid : _source3.IsZeroStorage);
        }
    }

    private static bool IsIdentity(Quaternion value)
        => (BitConverter.SingleToInt32Bits(value.X) == 0)
        && (BitConverter.SingleToInt32Bits(value.Y) == 0)
        && (BitConverter.SingleToInt32Bits(value.Z) == 0)
        && (BitConverter.SingleToInt32Bits(value.W) == BitConverter.SingleToInt32Bits(1.0f));

    private static bool IsPositiveZero(Vector3 value)
        => (BitConverter.SingleToInt32Bits(value.X) == 0)
        && (BitConverter.SingleToInt32Bits(value.Y) == 0)
        && (BitConverter.SingleToInt32Bits(value.Z) == 0);

    /// <summary>Gets the source at the specified controller-slot order.</summary>
    /// <param name="index">An index in the half-open interval <c>[0, SourceCount)</c>.</param>
    /// <returns>The copied Clip contribution.</returns>
    /// <exception cref="InvalidOperationException">This request is the absent default value.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the source range.</exception>
    public RootMotionSource GetSource(int index)
    {
        ThrowIfAbsent();
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, (int)_sourceCount);
        return index switch {
            0 => _source0,
            1 => _source1,
            2 => _source2,
            3 => _source3,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
    }

    private void ThrowIfAbsent()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("The RootMotionRequest is absent or invalid.");
        }
    }
}
