// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class GraphResourceImportRegistry
{
    private readonly List<GraphBufferImportRecord> _bufferImports = [];
    private readonly GraphicsDevice _device;
    private readonly uint _deviceEpoch;
    private readonly uint _graphOwnerId;
    private ulong _nextResourceId;
    private readonly int _ownerThreadId;
    private readonly List<GraphTextureImportRecord> _textureImports = [];

    internal GraphResourceImportRegistry(GraphicsDevice device, uint graphOwnerId)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfZero(graphOwnerId);

        if (device.Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The graph requires a ready GraphicsDevice.");
        }

        _device = device;
        _deviceEpoch = device.DeviceEpoch;
        _graphOwnerId = graphOwnerId;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    internal ImmutableArray<GraphBufferImportRecord> BufferImports
    {
        get
        {
            EnsureOwnerThread();
            return [.. _bufferImports];
        }
    }

    internal uint DeviceEpoch => _deviceEpoch;

    internal uint GraphOwnerId => _graphOwnerId;

    internal ImmutableArray<GraphTextureImportRecord> TextureImports
    {
        get
        {
            EnsureOwnerThread();
            return [.. _textureImports];
        }
    }

    internal GraphBufferImportRecord GetBufferImport(GraphBufferVersion version)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(version);

        foreach (var import in _bufferImports)
        {
            if (ReferenceEquals(import.Version, version))
            {
                return import;
            }
        }

        throw new ArgumentException("The buffer version is not owned by this graph.", nameof(version));
    }

    internal GraphTextureImportRecord GetTextureImport(GraphTextureVersion version)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(version);

        foreach (var import in _textureImports)
        {
            if (ReferenceEquals(import.Version, version))
            {
                return import;
            }
        }

        throw new ArgumentException("The texture version is not owned by this graph.", nameof(version));
    }

    internal GraphBufferVersion ImportBuffer(GpuBuffer buffer, GraphBufferRange range, GraphImportOptions options)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(options);
        ValidateDevice(buffer.ResourceOwner, buffer.DeviceEpoch);

        var version = new GraphBufferVersion(_deviceEpoch, GetNextResourceId(), 0, buffer.Description, range);
        var import = new GraphBufferImportRecord(buffer, version, options);

        foreach (var existing in _bufferImports)
        {
            if (ReferenceEquals(existing.Buffer.ResourceOwner, buffer.ResourceOwner)
                && existing.Version.Range.Overlaps(range))
            {
                existing.HasOverlappingImport = true;
                import.HasOverlappingImport = true;
            }
        }

        _bufferImports.Add(import);
        return version;
    }

    internal GraphTextureVersion ImportTexture(GpuTexture texture, GraphTextureRange range, GraphImportOptions options)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(options);
        ValidateDevice(texture.ResourceOwner, texture.DeviceEpoch);

        var version = new GraphTextureVersion(_deviceEpoch, GetNextResourceId(), 0, texture.Description, range);
        var import = new GraphTextureImportRecord(texture, version, options);

        foreach (var existing in _textureImports)
        {
            if (ReferenceEquals(existing.Texture.ResourceOwner, texture.ResourceOwner)
                && existing.Version.Range.Overlaps(range))
            {
                existing.HasOverlappingImport = true;
                import.HasOverlappingImport = true;
            }
        }

        _textureImports.Add(import);
        return version;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Graph resource imports are restricted to their creating thread.");
        }
    }

    private ulong GetNextResourceId()
    {
        _nextResourceId = checked(_nextResourceId + 1);
        return _nextResourceId;
    }

    private void ValidateDevice(GraphicsResourceOwner resourceOwner, uint deviceEpoch)
    {
        if (!ReferenceEquals(resourceOwner.Device, _device))
        {
            throw new ArgumentException("The imported resource belongs to another GraphicsDevice.");
        }

        if ((deviceEpoch != _deviceEpoch)
            || (_device.DeviceEpoch != _deviceEpoch)
            || (_device.Status != GraphicsDeviceStatus.Ready))
        {
            throw new InvalidOperationException("The imported resource belongs to a stale or unavailable device epoch.");
        }
    }
}
