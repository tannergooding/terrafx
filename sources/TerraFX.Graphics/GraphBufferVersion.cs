// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Identifies one immutable graph version and range of a GPU buffer.</summary>
public sealed class GraphBufferVersion
{
    internal GraphBufferVersion(
        uint deviceEpoch,
        ulong resourceId,
        int version,
        BufferDescription description,
        GraphBufferRange range)
    {
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentOutOfRangeException.ThrowIfZero(resourceId);
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        ArgumentNullException.ThrowIfNull(description);

        if (!range.IsWithin(new GraphBufferRange(0, description.ByteLength)))
        {
            throw new ArgumentException("The graph buffer range must fit the resource description.", nameof(range));
        }

        DeviceEpoch = deviceEpoch;
        Description = description;
        Range = range;
        ResourceId = resourceId;
        Version = version;
    }

    /// <summary>Gets the device generation that owns the resource.</summary>
    /// <summary>Gets the immutable resource description.</summary>
    public BufferDescription Description { get; }

    /// <summary>Gets the device generation that owns the resource.</summary>
    public uint DeviceEpoch { get; }

    /// <summary>Gets the exact byte range represented by this version.</summary>
    public GraphBufferRange Range { get; }

    /// <summary>Gets the version number within the owning graph.</summary>
    public int Version { get; }

    internal ulong ResourceId { get; }
}
