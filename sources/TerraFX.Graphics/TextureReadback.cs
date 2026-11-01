// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Buffers;

namespace TerraFX.Graphics;

/// <summary>Owns one completed texture-subresource copy in CPU-readable storage.</summary>
public sealed class TextureReadback : IDisposable
{
    private int _isDisposed;
    private readonly OwnedBuffer<byte> _storage;

    internal TextureReadback(
        TextureFormat format,
        TextureSubresource subresource,
        int width,
        int height,
        int depth,
        nint rowPitch,
        nint slicePitch,
        OwnedBuffer<byte> storage)
    {
        if ((format == TextureFormat.None) || !Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowPitch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slicePitch);
        ArgumentNullException.ThrowIfNull(storage);

        var isDepthFormat = format == TextureFormat.D32Float;

        if (isDepthFormat ? subresource.Aspect != TextureAspect.Depth : subresource.Aspect != TextureAspect.Color)
        {
            throw new ArgumentException("The selected aspect is incompatible with the texture format.", nameof(subresource));
        }

        var minimumRowPitch = checked((nint)width * GetBytesPerTexel(format));

        ArgumentOutOfRangeException.ThrowIfLessThan(rowPitch, minimumRowPitch);

        var minimumSlicePitch = checked(rowPitch * height);

        ArgumentOutOfRangeException.ThrowIfLessThan(slicePitch, minimumSlicePitch);

        var byteLength = checked(slicePitch * depth);

        if (storage.ByteLength != byteLength)
        {
            throw new ArgumentException("Readback storage length must equal SlicePitch multiplied by Depth.", nameof(storage));
        }

        using (var validationLease = storage.AcquireReadLease(0, byteLength))
        {
            if (validationLease.Length != byteLength)
            {
                throw new InvalidOperationException("The owned readback lease reported an inconsistent byte extent.");
            }
        }

        Format = format;
        Subresource = subresource;
        Width = width;
        Height = height;
        Depth = depth;
        RowPitch = rowPitch;
        SlicePitch = slicePitch;
        ByteLength = byteLength;
        _storage = storage;
    }

    /// <summary>Gets the complete exposed byte length, including zeroed row/slice padding.</summary>
    public nint ByteLength { get; }

    /// <summary>Gets the positive depth extent.</summary>
    public int Depth { get; }

    /// <summary>Gets the format of the copied subresource.</summary>
    public TextureFormat Format { get; }

    /// <summary>Gets the positive height extent.</summary>
    public int Height { get; }

    /// <summary>Gets the positive row pitch in bytes.</summary>
    public nint RowPitch { get; }

    /// <summary>Gets the positive slice pitch in bytes.</summary>
    public nint SlicePitch { get; }

    /// <summary>Gets the selected subresource identity.</summary>
    public TextureSubresource Subresource { get; }

    /// <summary>Gets the positive width extent.</summary>
    public int Width { get; }

    internal static int GetBytesPerTexel(TextureFormat format)
        => format switch {
            TextureFormat.R8Unorm => 1,
            TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb or TextureFormat.Bgra8Unorm or TextureFormat.Bgra8Srgb => 4,
            TextureFormat.Rgba16Float => 8,
            TextureFormat.R32Float or TextureFormat.R32UInt or TextureFormat.D32Float => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    /// <summary>Acquires a read lease for the complete copied subresource.</summary>
    /// <returns>A lease that remains valid until disposed.</returns>
    /// <exception cref="ObjectDisposedException">This readback has been disposed.</exception>
    public BufferReadLease<byte> AcquireReadLease()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
        return _storage.AcquireReadLease(0, ByteLength);
    }

    /// <summary>Releases the owned CPU storage.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _storage.Dispose();
        }
    }
}
