// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal readonly struct GraphResourceAccess
{
    private readonly AttachmentLoadAction? _attachmentLoadAction;
    private readonly AttachmentStoreAction? _attachmentStoreAction;
    private readonly TextureUsage? _attachmentUsage;
    private readonly GraphBufferRange _bufferRange;
    private readonly GraphBufferUsage? _bufferUsage;
    private readonly GraphBufferVersion? _bufferVersion;
    private readonly bool _contentsValid;
    private readonly GraphResourceState? _requiredStateOverride;
    private readonly ulong _storageReadWritePairId;
    private readonly GraphTextureRange _textureRange;
    private readonly GraphTextureUsage? _textureUsage;
    private readonly GraphTextureVersion? _textureVersion;

    internal GraphResourceAccess(
        RenderPassId passId,
        GraphBufferVersion version,
        GraphBufferRange range,
        GraphBufferUsage usage,
        bool isWrite,
        bool contentsValid = true,
        ulong storageReadWritePairId = 0)
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

        if (!Enum.IsDefined(usage))
        {
            throw new ArgumentOutOfRangeException(nameof(usage));
        }

        if ((storageReadWritePairId != 0)
            && (usage != (isWrite ? GraphBufferUsage.StorageWrite : GraphBufferUsage.StorageRead)))
        {
            throw new ArgumentException("A storage read-write pair must contain storage-read and storage-write accesses.", nameof(storageReadWritePairId));
        }

        DeviceEpoch = version.DeviceEpoch;
        IsTexture = false;
        IsWrite = isWrite;
        PassId = passId;
        ResourceId = version.ResourceId;
        Version = version.Version;
        _attachmentLoadAction = null;
        _attachmentStoreAction = null;
        _attachmentUsage = null;
        _contentsValid = contentsValid;
        _requiredStateOverride = null;
        _storageReadWritePairId = storageReadWritePairId;
        _bufferRange = range;
        _bufferUsage = usage;
        _bufferVersion = version;
        _textureRange = default;
        _textureUsage = null;
        _textureVersion = null;
    }

    internal GraphResourceAccess(
        RenderPassId passId,
        GraphTextureVersion version,
        GraphTextureRange range,
        GraphResourceState requiredState,
        bool contentsValid)
    {
        if (!passId.IsValid)
        {
            throw new ArgumentException("The graph pass ID must be valid.", nameof(passId));
        }

        ArgumentNullException.ThrowIfNull(version);

        if (!range.IsWithin(version.Range))
        {
            throw new ArgumentException("The presentation access range must fit its graph version.", nameof(range));
        }

        if (requiredState is not (GraphResourceState.Present or GraphResourceState.OffscreenOutput))
        {
            throw new ArgumentOutOfRangeException(nameof(requiredState), "A terminal texture access must name a surface-present or offscreen-output state.");
        }

        DeviceEpoch = version.DeviceEpoch;
        IsTexture = true;
        IsWrite = false;
        PassId = passId;
        ResourceId = version.ResourceId;
        Version = version.Version;
        _attachmentLoadAction = null;
        _attachmentStoreAction = null;
        _attachmentUsage = null;
        _contentsValid = contentsValid;
        _requiredStateOverride = requiredState;
        _storageReadWritePairId = 0;
        _bufferRange = default;
        _bufferUsage = null;
        _bufferVersion = null;
        _textureRange = range;
        _textureUsage = null;
        _textureVersion = version;
    }

    internal GraphResourceAccess(
        RenderPassId passId,
        GraphTextureVersion version,
        GraphTextureRange range,
        GraphTextureUsage usage,
        bool isWrite,
        bool contentsValid = true,
        ulong storageReadWritePairId = 0)
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

        if (!Enum.IsDefined(usage))
        {
            throw new ArgumentOutOfRangeException(nameof(usage));
        }

        if ((storageReadWritePairId != 0)
            && (usage != (isWrite ? GraphTextureUsage.StorageWrite : GraphTextureUsage.StorageRead)))
        {
            throw new ArgumentException("A storage read-write pair must contain storage-read and storage-write accesses.", nameof(storageReadWritePairId));
        }

        DeviceEpoch = version.DeviceEpoch;
        IsTexture = true;
        IsWrite = isWrite;
        PassId = passId;
        ResourceId = version.ResourceId;
        Version = version.Version;
        _attachmentLoadAction = null;
        _attachmentStoreAction = null;
        _attachmentUsage = null;
        _contentsValid = contentsValid;
        _requiredStateOverride = null;
        _storageReadWritePairId = storageReadWritePairId;
        _bufferRange = default;
        _bufferUsage = null;
        _bufferVersion = null;
        _textureRange = range;
        _textureUsage = usage;
        _textureVersion = version;
    }

    internal GraphResourceAccess(
        RenderPassId passId,
        GraphTextureVersion version,
        GraphTextureRange range,
        TextureUsage attachmentUsage,
        AttachmentLoadAction loadAction,
        AttachmentStoreAction storeAction,
        bool isWrite,
        bool contentsValid)
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

        if (attachmentUsage is not (
            TerraFX.Graphics.TextureUsage.ColorAttachment or TerraFX.Graphics.TextureUsage.DepthAttachment))
        {
            throw new ArgumentOutOfRangeException(nameof(attachmentUsage));
        }

        if (!Enum.IsDefined(loadAction))
        {
            throw new ArgumentOutOfRangeException(nameof(loadAction));
        }

        if (!Enum.IsDefined(storeAction))
        {
            throw new ArgumentOutOfRangeException(nameof(storeAction));
        }

        if (!isWrite && (loadAction != TerraFX.Graphics.AttachmentLoadAction.Load))
        {
            throw new ArgumentException("An attachment read must represent a Load action.", nameof(loadAction));
        }

        DeviceEpoch = version.DeviceEpoch;
        IsTexture = true;
        IsWrite = isWrite;
        PassId = passId;
        ResourceId = version.ResourceId;
        Version = version.Version;
        _attachmentLoadAction = loadAction;
        _attachmentStoreAction = storeAction;
        _attachmentUsage = attachmentUsage;
        _contentsValid = contentsValid;
        _requiredStateOverride = null;
        _storageReadWritePairId = 0;
        _bufferRange = default;
        _bufferUsage = null;
        _bufferVersion = null;
        _textureRange = range;
        _textureUsage = null;
        _textureVersion = version;
    }

    internal AttachmentLoadAction? AttachmentLoadAction => _attachmentLoadAction;

    internal AttachmentStoreAction? AttachmentStoreAction => _attachmentStoreAction;

    internal TextureUsage? AttachmentUsage => _attachmentUsage;

    internal GraphBufferRange BufferRange => _bufferRange;

    internal GraphBufferUsage? BufferUsage => _bufferUsage;

    internal GraphBufferVersion? BufferVersion => _bufferVersion;

    internal bool ContentsValid => _contentsValid;

    internal uint DeviceEpoch { get; }

    internal bool IsStorageReadWritePair => _storageReadWritePairId != 0;

    internal bool IsTexture { get; }

    internal bool IsWrite { get; }

    internal RenderPassId PassId { get; }

    internal GraphResourceState RequiredState
    {
        get
        {
            if (_requiredStateOverride is GraphResourceState requiredState)
            {
                return requiredState;
            }

            if (IsStorageReadWritePair)
            {
                return GraphResourceState.StorageReadWrite;
            }

            if (_attachmentUsage is TextureUsage attachmentUsage)
            {
                return attachmentUsage switch {
                    TerraFX.Graphics.TextureUsage.ColorAttachment => GraphResourceState.ColorAttachment,
                    TerraFX.Graphics.TextureUsage.DepthAttachment => GraphResourceState.DepthWrite,
                    _ => throw new InvalidOperationException("The attachment access has no supported graph state."),
                };
            }

            if (_bufferUsage is GraphBufferUsage bufferUsage)
            {
                return bufferUsage switch {
                    GraphBufferUsage.VertexRead => GraphResourceState.VertexRead,
                    GraphBufferUsage.IndexRead => GraphResourceState.IndexRead,
                    GraphBufferUsage.UniformRead => GraphResourceState.UniformRead,
                    GraphBufferUsage.StorageRead => GraphResourceState.StorageRead,
                    GraphBufferUsage.StorageWrite => GraphResourceState.StorageWrite,
                    GraphBufferUsage.CopySource => GraphResourceState.CopySource,
                    GraphBufferUsage.CopyDestination => GraphResourceState.CopyDestination,
                    _ => throw new InvalidOperationException("The buffer access has no supported graph state."),
                };
            }

            if (_textureUsage is GraphTextureUsage textureUsage)
            {
                return textureUsage switch {
                    GraphTextureUsage.SampledRead => GraphResourceState.ShaderSampledRead,
                    GraphTextureUsage.StorageRead => GraphResourceState.StorageRead,
                    GraphTextureUsage.StorageWrite => GraphResourceState.StorageWrite,
                    GraphTextureUsage.CopySource => GraphResourceState.CopySource,
                    GraphTextureUsage.CopyDestination => GraphResourceState.CopyDestination,
                    _ => throw new InvalidOperationException("The texture access has no supported graph state."),
                };
            }

            throw new InvalidOperationException("The graph resource access has no declared usage.");
        }
    }

    internal ulong ResourceId { get; }

    internal GraphTextureRange TextureRange => _textureRange;

    internal GraphTextureUsage? TextureUsage => _textureUsage;

    internal GraphTextureVersion? TextureVersion => _textureVersion;

    internal int Version { get; }

    internal bool IsAttachmentLoadOf(GraphResourceAccess other)
        => IsTexture
            && other.IsTexture
            && !IsWrite
            && other.IsWrite
            && (Version < other.Version)
            && (_textureRange == other._textureRange)
            && _attachmentUsage.HasValue
            && _attachmentUsage == other._attachmentUsage
            && _attachmentLoadAction == TerraFX.Graphics.AttachmentLoadAction.Load
            && _attachmentLoadAction == other._attachmentLoadAction
            && _attachmentStoreAction == other._attachmentStoreAction;

    internal bool IsStorageReadWritePairOf(GraphResourceAccess other)
    {
        if ((_storageReadWritePairId == 0)
            || (_storageReadWritePairId != other._storageReadWritePairId)
            || (DeviceEpoch != other.DeviceEpoch)
            || (PassId != other.PassId)
            || (ResourceId != other.ResourceId)
            || (IsTexture != other.IsTexture)
            || (IsWrite == other.IsWrite))
        {
            return false;
        }

        var read = IsWrite ? other : this;
        var write = IsWrite ? this : other;

        if (((long)read.Version + 1) != write.Version)
        {
            return false;
        }

        return IsTexture
            ? (read._textureUsage == GraphTextureUsage.StorageRead)
                && (write._textureUsage == GraphTextureUsage.StorageWrite)
                && read._textureRange.Equals(write._textureRange)
            : (read._bufferUsage == GraphBufferUsage.StorageRead)
                && (write._bufferUsage == GraphBufferUsage.StorageWrite)
                && read._bufferRange.Equals(write._bufferRange);
    }

    internal bool Overlaps(GraphResourceAccess other)
        => IsTexture
            ? _textureRange.Overlaps(other._textureRange)
            : _bufferRange.Overlaps(other._bufferRange);
}
