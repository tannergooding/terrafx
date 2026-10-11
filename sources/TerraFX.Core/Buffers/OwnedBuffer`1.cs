// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Buffers;

/// <summary>Owns finalized native storage that can only be read.</summary>
/// <typeparam name="T">The unmanaged element type.</typeparam>
public sealed class OwnedBuffer<T> : IDisposable
    where T : unmanaged
{
    private bool _isDisposed;
    private readonly UnmanagedBuffer<T> _storage;

    internal OwnedBuffer(UnmanagedBuffer<T> storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
        storage.TransferToOwnedBuffer();
    }

    /// <summary>Gets the immutable checked logical byte extent.</summary>
    public nint ByteLength => _storage.ByteLength;

    /// <summary>Gets the immutable element count.</summary>
    public nint Length => _storage.Length;

    /// <summary>Acquires an overlapping-read lease for a validated element range.</summary>
    /// <param name="offset">The nonnegative element offset.</param>
    /// <param name="length">The nonnegative element count.</param>
    /// <returns>A disposable read lease.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is outside this buffer.</exception>
    /// <exception cref="ObjectDisposedException">The owner is closed.</exception>
    public BufferReadLease<T> AcquireReadLease(nint offset, nint length)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return _storage.AcquireOwnedReadLease(offset, length);
    }

    /// <summary>Closes new admissions and releases the finalized-storage owner credit.</summary>
    public void Dispose()
    {
        DisposeCore();
    }

    private void DisposeCore()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _storage.ReleaseOwnedBufferCredit();
    }
}
