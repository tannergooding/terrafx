// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class GraphResourceRegistry
{
    private readonly List<GraphResourceAccess> _accesses = [];
    private readonly List<GraphBufferImportRecord> _bufferImports = [];
    private readonly Dictionary<GraphBufferVersion, GraphBufferResourceRecord> _bufferResourcesByVersion = [];
    private readonly GraphicsDevice _device;
    private readonly uint _deviceEpoch;
    private readonly uint _graphOwnerId;
    private ulong _nextResourceId;
    private readonly int _ownerThreadId;
    private readonly GraphPassRegistry _passRegistry;
    private readonly List<GraphTextureImportRecord> _textureImports = [];
    private readonly Dictionary<GraphTextureVersion, GraphTextureResourceRecord> _textureResourcesByVersion = [];

    internal GraphResourceRegistry(GraphicsDevice device, uint graphOwnerId)
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
        _passRegistry = new GraphPassRegistry(graphOwnerId);
    }

    internal ImmutableArray<GraphResourceAccess> Accesses
    {
        get
        {
            EnsureOwnerThread();
            return [.. _accesses];
        }
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

    internal bool HasInvalidImports
    {
        get
        {
            EnsureOwnerThread();

            foreach (var import in _bufferImports)
            {
                if (!import.CaptureValidation().IsAdmissible)
                {
                    return true;
                }
            }

            foreach (var import in _textureImports)
            {
                if (!import.CaptureValidation().IsAdmissible)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal bool HasUninitializedReads
    {
        get
        {
            EnsureOwnerThread();

            foreach (var access in _accesses)
            {
                if (!access.IsWrite && !access.ContentsValid)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal ImmutableArray<RenderGraphPassNode> Passes => _passRegistry.Passes;

    internal ImmutableArray<GraphTextureImportRecord> TextureImports
    {
        get
        {
            EnsureOwnerThread();
            return [.. _textureImports];
        }
    }

    private static BufferUsage GetRequiredBufferUsage(GraphBufferUsage usage)
        => usage switch {
            GraphBufferUsage.VertexRead => BufferUsage.Vertex,
            GraphBufferUsage.IndexRead => BufferUsage.Index,
            GraphBufferUsage.UniformRead => BufferUsage.Uniform,
            GraphBufferUsage.StorageRead or GraphBufferUsage.StorageWrite => BufferUsage.Storage,
            GraphBufferUsage.CopySource => BufferUsage.CopySource,
            GraphBufferUsage.CopyDestination => BufferUsage.CopyDestination,
            _ => throw new ArgumentOutOfRangeException(nameof(usage)),
        };

    private static TextureUsage GetRequiredTextureUsage(GraphTextureUsage usage)
        => usage switch {
            GraphTextureUsage.SampledRead => TextureUsage.Sampled,
            GraphTextureUsage.StorageRead or GraphTextureUsage.StorageWrite => TextureUsage.Storage,
            GraphTextureUsage.CopySource => TextureUsage.CopySource,
            GraphTextureUsage.CopyDestination => TextureUsage.CopyDestination,
            _ => throw new ArgumentOutOfRangeException(nameof(usage)),
        };

    private static void ValidateBufferUsage(BufferDescription description, GraphBufferUsage usage, bool isWrite)
    {
        var isWriteUsage = usage is GraphBufferUsage.StorageWrite or GraphBufferUsage.CopyDestination;

        if (isWrite != isWriteUsage)
        {
            throw new ArgumentException("The selected graph buffer use has the wrong access direction.", nameof(usage));
        }

        if ((description.Usage & GetRequiredBufferUsage(usage)) == 0)
        {
            throw new ArgumentException("The buffer description does not permit the selected graph use.", nameof(usage));
        }
    }

    private static void ValidateTextureUsage(TextureDescription description, GraphTextureUsage usage, bool isWrite)
    {
        var isWriteUsage = usage is GraphTextureUsage.StorageWrite or GraphTextureUsage.CopyDestination;

        if (isWrite != isWriteUsage)
        {
            throw new ArgumentException("The selected graph texture use has the wrong access direction.", nameof(usage));
        }

        if ((description.Usage & GetRequiredTextureUsage(usage)) == 0)
        {
            throw new ArgumentException("The texture description does not permit the selected graph use.", nameof(usage));
        }
    }

    internal RenderPassId AddPass(RenderPassDescription description)
    {
        EnsureOwnerThread();
        return _passRegistry.AddPass(description);
    }

    internal GraphBufferVersion CreateTransientBuffer(BufferDescription description)
    {
        EnsureOwnerThread();
        EnsureDeviceReady();
        ArgumentNullException.ThrowIfNull(description);

        var range = new GraphBufferRange(0, description.ByteLength);
        var version = new GraphBufferVersion(_deviceEpoch, GetNextResourceId(), 0, description, range);
        var resource = new GraphBufferResourceRecord(version, contentsValid: false, importRecord: null);
        _bufferResourcesByVersion.Add(version, resource);
        return version;
    }

    internal GraphTextureVersion CreateTransientTexture(TextureDescription description)
    {
        EnsureOwnerThread();
        EnsureDeviceReady();
        ArgumentNullException.ThrowIfNull(description);

        var aspect = (description.Format == TextureFormat.D32Float)
            ? TextureAspect.Depth
            : TextureAspect.Color;
        var range = new GraphTextureRange(0, description.MipLevels, 0, description.ArrayLayers, aspect);
        var version = new GraphTextureVersion(_deviceEpoch, GetNextResourceId(), 0, description, range);
        var resource = new GraphTextureResourceRecord(version, contentsValid: false, importRecord: null);
        _textureResourcesByVersion.Add(version, resource);
        return version;
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

        throw new ArgumentException("The buffer version is not an imported resource owned by this graph.", nameof(version));
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

        throw new ArgumentException("The texture version is not an imported resource owned by this graph.", nameof(version));
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
        _bufferResourcesByVersion.Add(
            version,
            new GraphBufferResourceRecord(version, import.InitialContentsValid, import));
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
        _textureResourcesByVersion.Add(
            version,
            new GraphTextureResourceRecord(version, import.InitialContentsValid, import));
        return version;
    }

    internal void ReadBuffer(
        RenderPassId passId,
        GraphBufferVersion version,
        GraphBufferRange range,
        GraphBufferUsage usage)
    {
        EnsureOwnerThread();
        ValidatePass(passId);
        ArgumentNullException.ThrowIfNull(version);
        ValidateBufferUsage(version.Description, usage, isWrite: false);

        var resource = GetBufferResource(version);
        var contentsValid = resource.IsInitialized(version, range);
        _accesses.Add(new GraphResourceAccess(
            passId,
            version,
            range,
            isWrite: false,
            contentsValid: contentsValid));
    }

    internal void ReadTexture(
        RenderPassId passId,
        GraphTextureVersion version,
        GraphTextureRange range,
        GraphTextureUsage usage)
    {
        EnsureOwnerThread();
        ValidatePass(passId);
        ArgumentNullException.ThrowIfNull(version);
        ValidateTextureUsage(version.Description, usage, isWrite: false);

        var resource = GetTextureResource(version);
        var contentsValid = resource.IsInitialized(version, range);
        _accesses.Add(new GraphResourceAccess(
            passId,
            version,
            range,
            isWrite: false,
            contentsValid: contentsValid));
    }

    internal bool TryOrder(out ImmutableArray<RenderPassId> orderedPasses)
    {
        EnsureOwnerThread();
        return _passRegistry.TryOrder([.. _accesses], out orderedPasses);
    }

    internal GraphTextureVersion WriteAttachment(
        RenderPassId passId,
        GraphTextureVersion previousVersion,
        GraphTextureRange range,
        AttachmentLoadAction loadAction,
        AttachmentStoreAction storeAction)
    {
        EnsureOwnerThread();
        ValidatePass(passId);
        ArgumentNullException.ThrowIfNull(previousVersion);

        var usage = (previousVersion.Description.Format == TextureFormat.D32Float)
            ? TextureUsage.DepthAttachment
            : TextureUsage.ColorAttachment;

        if ((previousVersion.Description.Usage & usage) == 0)
        {
            throw new ArgumentException("The texture description does not permit the selected attachment use.", nameof(previousVersion));
        }

        var resource = GetTextureResource(previousVersion);
        var (nextVersion, loadContentsValid) =
            resource.WriteAttachment(previousVersion, range, loadAction, storeAction);
        _textureResourcesByVersion.Add(nextVersion, resource);
        var nextContentsValid = resource.IsInitialized(nextVersion, range);

        if (loadAction == AttachmentLoadAction.Load)
        {
            _accesses.Add(new GraphResourceAccess(
                passId,
                previousVersion,
                range,
                isWrite: false,
                contentsValid: loadContentsValid));
        }

        _accesses.Add(new GraphResourceAccess(
            passId,
            nextVersion,
            range,
            isWrite: true,
            contentsValid: nextContentsValid));
        return nextVersion;
    }

    internal GraphBufferVersion WriteBuffer(
        RenderPassId passId,
        GraphBufferVersion previousVersion,
        GraphBufferRange range,
        GraphBufferUsage usage)
    {
        EnsureOwnerThread();
        ValidatePass(passId);
        ArgumentNullException.ThrowIfNull(previousVersion);
        ValidateBufferUsage(previousVersion.Description, usage, isWrite: true);

        var resource = GetBufferResource(previousVersion);
        var nextVersion = resource.Write(previousVersion, range);
        _bufferResourcesByVersion.Add(nextVersion, resource);
        _accesses.Add(new GraphResourceAccess(passId, nextVersion, range, isWrite: true));
        return nextVersion;
    }

    internal GraphTextureVersion WriteTexture(
        RenderPassId passId,
        GraphTextureVersion previousVersion,
        GraphTextureRange range,
        GraphTextureUsage usage)
    {
        EnsureOwnerThread();
        ValidatePass(passId);
        ArgumentNullException.ThrowIfNull(previousVersion);
        ValidateTextureUsage(previousVersion.Description, usage, isWrite: true);

        var resource = GetTextureResource(previousVersion);
        var nextVersion = resource.Write(previousVersion, range);
        _textureResourcesByVersion.Add(nextVersion, resource);
        _accesses.Add(new GraphResourceAccess(passId, nextVersion, range, isWrite: true));
        return nextVersion;
    }

    private void EnsureDeviceReady()
    {
        if ((_device.DeviceEpoch != _deviceEpoch) || (_device.Status != GraphicsDeviceStatus.Ready))
        {
            throw new InvalidOperationException("The graph's GraphicsDevice is stale or unavailable.");
        }
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Graph resource declarations are restricted to their creating thread.");
        }
    }

    private GraphBufferResourceRecord GetBufferResource(GraphBufferVersion version)
    {
        if (_bufferResourcesByVersion.TryGetValue(version, out var resource))
        {
            return resource;
        }

        throw new ArgumentException("The buffer version does not belong to this graph.", nameof(version));
    }

    private ulong GetNextResourceId()
    {
        _nextResourceId = checked(_nextResourceId + 1);
        return _nextResourceId;
    }

    private GraphTextureResourceRecord GetTextureResource(GraphTextureVersion version)
    {
        if (_textureResourcesByVersion.TryGetValue(version, out var resource))
        {
            return resource;
        }

        throw new ArgumentException("The texture version does not belong to this graph.", nameof(version));
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

    private void ValidatePass(RenderPassId passId)
    {
        if (!_passRegistry.IsRegistered(passId))
        {
            throw new ArgumentException("The pass ID is not registered in this graph.", nameof(passId));
        }
    }
}
