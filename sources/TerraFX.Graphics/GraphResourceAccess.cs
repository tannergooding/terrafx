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
    private readonly bool _contentsValid;
    private readonly GraphTextureRange _textureRange;
    private readonly GraphTextureUsage? _textureUsage;

    internal GraphResourceAccess(
        RenderPassId passId,
        GraphBufferVersion version,
        GraphBufferRange range,
        GraphBufferUsage usage,
        bool isWrite,
        bool contentsValid = true)
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
        _bufferRange = range;
        _bufferUsage = usage;
        _textureRange = default;
        _textureUsage = null;
    }

    internal GraphResourceAccess(
        RenderPassId passId,
        GraphTextureVersion version,
        GraphTextureRange range,
        GraphTextureUsage usage,
        bool isWrite,
        bool contentsValid = true)
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
        _bufferRange = default;
        _bufferUsage = null;
        _textureRange = range;
        _textureUsage = usage;
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
        _bufferRange = default;
        _bufferUsage = null;
        _textureRange = range;
        _textureUsage = null;
    }

    internal AttachmentLoadAction? AttachmentLoadAction => _attachmentLoadAction;

    internal AttachmentStoreAction? AttachmentStoreAction => _attachmentStoreAction;

    internal TextureUsage? AttachmentUsage => _attachmentUsage;

    internal GraphBufferRange BufferRange => _bufferRange;

    internal GraphBufferUsage? BufferUsage => _bufferUsage;

    internal bool ContentsValid => _contentsValid;

    internal uint DeviceEpoch { get; }

    internal bool IsTexture { get; }

    internal bool IsWrite { get; }

    internal RenderPassId PassId { get; }

    internal GraphResourceState RequiredState
    {
        get
        {
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

    internal bool Overlaps(GraphResourceAccess other)
        => IsTexture
            ? _textureRange.Overlaps(other._textureRange)
            : _bufferRange.Overlaps(other._bufferRange);
}
