// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal readonly struct GraphResourceAccess
{
    private readonly GraphBufferRange _bufferRange;
    private readonly GraphTextureRange _textureRange;

    internal GraphResourceAccess(
        RenderPassId passId,
        GraphBufferVersion version,
        GraphBufferRange range,
        bool isWrite)
    {
        if (!passId.IsValid)
        {
            throw new ArgumentException("The graph pass ID must be valid.", nameof(passId));
        }

        ArgumentNullException.ThrowIfNull(version);

        if (!range.IsWithin(version.Range))
        {
            throw new ArgumentException("The buffer access range must fit its graph version.", nameof(range));
        }

        DeviceEpoch = version.DeviceEpoch;
        IsTexture = false;
        IsWrite = isWrite;
        PassId = passId;
        ResourceId = version.ResourceId;
        Version = version.Version;
        _bufferRange = range;
        _textureRange = default;
    }

    internal GraphResourceAccess(
        RenderPassId passId,
        GraphTextureVersion version,
        GraphTextureRange range,
        bool isWrite)
    {
        if (!passId.IsValid)
        {
            throw new ArgumentException("The graph pass ID must be valid.", nameof(passId));
        }

        ArgumentNullException.ThrowIfNull(version);

        if (!range.IsWithin(version.Range))
        {
            throw new ArgumentException("The texture access range must fit its graph version.", nameof(range));
        }

        DeviceEpoch = version.DeviceEpoch;
        IsTexture = true;
        IsWrite = isWrite;
        PassId = passId;
        ResourceId = version.ResourceId;
        Version = version.Version;
        _bufferRange = default;
        _textureRange = range;
    }

    internal uint DeviceEpoch { get; }

    internal bool IsTexture { get; }

    internal bool IsWrite { get; }

    internal RenderPassId PassId { get; }

    internal ulong ResourceId { get; }

    internal int Version { get; }

    internal bool Overlaps(GraphResourceAccess other)
        => IsTexture
            ? _textureRange.Overlaps(other._textureRange)
            : _bufferRange.Overlaps(other._bufferRange);
}
