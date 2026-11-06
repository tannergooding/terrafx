// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal readonly struct StorageResourceUse
{
    private readonly GraphBufferRange _bufferRange;
    private readonly GraphBufferVersion? _bufferVersion;
    private readonly GraphTextureRange _textureRange;
    private readonly GraphTextureVersion? _textureVersion;

    internal StorageResourceUse(BufferBinding binding, ShaderResourceAccess access)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.Kind != ShaderResourceKind.StorageBuffer)
        {
            throw new ArgumentException("A storage-buffer use requires a storage-buffer binding.", nameof(binding));
        }

        _bufferRange = binding.Range;
        _bufferVersion = binding.Resource;
        _textureRange = default;
        _textureVersion = null;
        IsTexture = false;
        Reads = access is ShaderResourceAccess.Read or ShaderResourceAccess.ReadWrite;
        ResourceId = binding.Resource.ResourceId;
        Writes = access is ShaderResourceAccess.Write or ShaderResourceAccess.ReadWrite;
    }

    internal StorageResourceUse(TextureBinding binding, ShaderResourceAccess access)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.Kind != ShaderResourceKind.StorageImage)
        {
            throw new ArgumentException("A storage-texture use requires a storage-image binding.", nameof(binding));
        }

        _bufferRange = default;
        _bufferVersion = null;
        _textureRange = binding.Range;
        _textureVersion = binding.Resource;
        IsTexture = true;
        Reads = access is ShaderResourceAccess.Read or ShaderResourceAccess.ReadWrite;
        ResourceId = binding.Resource.ResourceId;
        Writes = access is ShaderResourceAccess.Write or ShaderResourceAccess.ReadWrite;
    }

    internal GraphBufferRange BufferRange => _bufferRange;

    internal GraphBufferVersion? BufferVersion => _bufferVersion;

    internal bool IsTexture { get; }

    internal bool Reads { get; }

    internal ulong ResourceId { get; }

    internal GraphTextureRange TextureRange => _textureRange;

    internal GraphTextureVersion? TextureVersion => _textureVersion;

    internal bool Writes { get; }

    internal bool HasHazardWith(StorageResourceUse other)
        => (ResourceId == other.ResourceId)
            && (IsTexture == other.IsTexture)
            && (Writes || other.Writes)
            && (IsTexture
                ? _textureRange.Overlaps(other._textureRange)
                : _bufferRange.Overlaps(other._bufferRange));
}
