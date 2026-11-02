// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Identifies one immutable graph version and subresource range of a GPU texture.</summary>
public sealed class GraphTextureVersion
{
    internal GraphTextureVersion(
        uint deviceEpoch,
        ulong resourceId,
        int version,
        TextureDescription description,
        GraphTextureRange range)
    {
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);
        ArgumentOutOfRangeException.ThrowIfZero(resourceId);
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        ArgumentNullException.ThrowIfNull(description);

        var aspect = (description.Format == TextureFormat.D32Float)
            ? TextureAspect.Depth
            : TextureAspect.Color;
        var fullRange = new GraphTextureRange(0, description.MipLevels, 0, description.ArrayLayers, aspect);

        if (!range.IsWithin(fullRange))
        {
            throw new ArgumentException("The graph texture range must fit the resource description and format aspect.", nameof(range));
        }

        DeviceEpoch = deviceEpoch;
        Description = description;
        Range = range;
        ResourceId = resourceId;
        Version = version;
    }

    /// <summary>Gets the device generation that owns the resource.</summary>
    /// <summary>Gets the immutable resource description.</summary>
    public TextureDescription Description { get; }

    /// <summary>Gets the device generation that owns the resource.</summary>
    public uint DeviceEpoch { get; }

    /// <summary>Gets the exact subresource range represented by this version.</summary>
    public GraphTextureRange Range { get; }

    /// <summary>Gets the version number within the owning graph.</summary>
    public int Version { get; }

    internal ulong ResourceId { get; }
}
