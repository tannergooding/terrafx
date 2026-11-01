// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Identifies one generation of a device-owned GPU buffer slot.</summary>
public readonly struct GpuBufferHandle : IEquatable<GpuBufferHandle>
{
    /// <summary>Initializes an engine-issued GPU buffer identity.</summary>
    /// <param name="ownerId">The nonzero graphics-owner identity.</param>
    /// <param name="deviceEpoch">The nonzero device generation.</param>
    /// <param name="index">The device-local buffer slot.</param>
    /// <param name="generation">The nonzero slot generation.</param>
    /// <exception cref="ArgumentOutOfRangeException">An identity generation or owner is zero.</exception>
    internal GpuBufferHandle(uint ownerId, uint deviceEpoch, uint index, uint generation)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentOutOfRangeException.ThrowIfZero(generation);
        OwnerId = ownerId;
        DeviceEpoch = deviceEpoch;
        Index = index;
        Generation = generation;
    }

    /// <summary>Gets the device generation.</summary>
    public uint DeviceEpoch { get; }

    /// <summary>Gets the slot generation, or zero for the invalid default value.</summary>
    public uint Generation { get; }

    /// <summary>Gets the device-local buffer slot.</summary>
    public uint Index { get; }

    /// <summary>Gets whether this value carries nonzero owner, device, and slot generations.</summary>
    public bool IsValid => (OwnerId != 0) && (DeviceEpoch != 0) && (Generation != 0);

    /// <summary>Gets the graphics-owner identity.</summary>
    public uint OwnerId { get; }

    /// <summary>Negates GPU buffer identity equality.</summary>
    public static bool operator !=(GpuBufferHandle left, GpuBufferHandle right) => !left.Equals(right);

    /// <summary>Compares GPU buffer identities including device and slot generations.</summary>
    public static bool operator ==(GpuBufferHandle left, GpuBufferHandle right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(GpuBufferHandle other)
        => (OwnerId == other.OwnerId) && (DeviceEpoch == other.DeviceEpoch) && (Index == other.Index) && (Generation == other.Generation);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GpuBufferHandle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(OwnerId, DeviceEpoch, Index, Generation);
}
