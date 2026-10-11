// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Buffers;

/// <summary>Owns an exclusive-write credit for a range of unmanaged buffer storage.</summary>
/// <typeparam name="T">The unmanaged element type.</typeparam>
public sealed class BufferWriteLease<T> : IDisposable
    where T : unmanaged
{
    private readonly UnmanagedBuffer<T> _owner;
    private readonly UnmanagedBuffer<T>.LeaseRegistration _registration;

    internal BufferWriteLease(UnmanagedBuffer<T> owner, UnmanagedBuffer<T>.LeaseRegistration registration)
    {
        _owner = owner;
        _registration = registration;
    }

    /// <summary>Gets the immutable element count covered by this lease.</summary>
    /// <exception cref="ObjectDisposedException">The lease is disposed.</exception>
    public nint Length => _owner.GetLeaseLength(_registration, BufferAccess.Write);

    /// <summary>Releases this lease's lifetime credit.</summary>
    public void Dispose()
    {
        _owner.ReleaseLease(_registration);
    }

    /// <summary>Invokes a synchronous callback with a borrowed writable window.</summary>
    /// <typeparam name="TAction">The concrete callback type, which may be a ref struct.</typeparam>
    /// <param name="relativeOffset">The nonnegative offset relative to this lease.</param>
    /// <param name="length">The nonnegative window element count.</param>
    /// <param name="action">The borrowed callback state.</param>
    /// <exception cref="ArgumentOutOfRangeException">The window is outside this lease.</exception>
    /// <exception cref="InvalidOperationException">The write lease already has an active window.</exception>
    /// <exception cref="ObjectDisposedException">The lease is disposed.</exception>
    public void UseWindow<TAction>(nint relativeOffset, int length, scoped ref TAction action)
        where TAction : struct, IAction<Span<T>>, allows ref struct
    {
        _owner.UseWriteWindow(_registration, relativeOffset, length, ref action);
    }
}
