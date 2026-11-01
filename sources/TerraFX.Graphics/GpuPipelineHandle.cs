// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Identifies one generation of a device-owned GPU pipeline slot.</summary>
public readonly struct GpuPipelineHandle : IEquatable<GpuPipelineHandle>
{
    /// <summary>Initializes an engine-issued GPU pipeline identity.</summary>
    /// <param name="ownerId">The nonzero graphics-owner identity.</param>
    /// <param name="deviceEpoch">The nonzero device generation.</param>
    /// <param name="index">The device-local pipeline slot.</param>
    /// <param name="generation">The nonzero slot generation.</param>
    /// <exception cref="ArgumentOutOfRangeException">An identity generation or owner is zero.</exception>
    internal GpuPipelineHandle(uint ownerId, uint deviceEpoch, uint index, uint generation)
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

    /// <summary>Gets the device-local pipeline slot.</summary>
    public uint Index { get; }

    /// <summary>Gets whether this value carries nonzero owner, device, and slot generations.</summary>
    public bool IsValid => (OwnerId != 0) && (DeviceEpoch != 0) && (Generation != 0);

    /// <summary>Gets the graphics-owner identity.</summary>
    public uint OwnerId { get; }

    /// <summary>Negates GPU pipeline identity equality.</summary>
    public static bool operator !=(GpuPipelineHandle left, GpuPipelineHandle right) => !left.Equals(right);

    /// <summary>Compares GPU pipeline identities including device and slot generations.</summary>
    public static bool operator ==(GpuPipelineHandle left, GpuPipelineHandle right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(GpuPipelineHandle other)
        => (OwnerId == other.OwnerId) && (DeviceEpoch == other.DeviceEpoch) && (Index == other.Index) && (Generation == other.Generation);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is GpuPipelineHandle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(OwnerId, DeviceEpoch, Index, Generation);
}
