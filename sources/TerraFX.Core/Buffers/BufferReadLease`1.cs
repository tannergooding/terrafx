// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Buffers;

/// <summary>Owns an independent read credit for a range of unmanaged buffer storage.</summary>
/// <typeparam name="T">The unmanaged element type.</typeparam>
public sealed class BufferReadLease<T> : IDisposable
    where T : unmanaged
{
    private readonly UnmanagedBuffer<T> _owner;
    private readonly UnmanagedBuffer<T>.LeaseRegistration _registration;

    internal BufferReadLease(UnmanagedBuffer<T> owner, UnmanagedBuffer<T>.LeaseRegistration registration)
    {
        _owner = owner;
        _registration = registration;
    }

    /// <summary>Gets the immutable element count covered by this lease.</summary>
    /// <exception cref="ObjectDisposedException">The lease is disposed.</exception>
    public nint Length => _owner.GetLeaseLength(_registration, BufferAccess.Read);

    /// <summary>Releases this lease's lifetime credit.</summary>
    public void Dispose()
    {
        _owner.ReleaseLease(_registration);
    }

    /// <summary>Invokes a synchronous callback with a borrowed readonly window.</summary>
    /// <typeparam name="TAction">The concrete callback type, which may be a ref struct.</typeparam>
    /// <param name="relativeOffset">The nonnegative offset relative to this lease.</param>
    /// <param name="length">The nonnegative window element count.</param>
    /// <param name="action">The borrowed callback state.</param>
    /// <exception cref="ArgumentOutOfRangeException">The window is outside this lease.</exception>
    /// <exception cref="InvalidOperationException">The window conflicts with an active write.</exception>
    /// <exception cref="ObjectDisposedException">The lease is disposed.</exception>
    public void UseWindow<TAction>(nint relativeOffset, int length, scoped ref TAction action)
        where TAction : struct, IAction<ReadOnlySpan<T>>, allows ref struct
    {
        _owner.UseReadWindow(_registration, relativeOffset, length, ref action);
    }

    internal BufferReadLease<T> Clone()
    {
        return _owner.CloneReadLease(_registration);
    }

    internal void CopyWindow(nint relativeOffset, Span<T> destination)
    {
        _owner.CopyFromReadLease(_registration, relativeOffset, destination);
    }

    internal bool VisitWindow<TVisitor>(nint relativeOffset, int length, scoped ref TVisitor visitor)
        where TVisitor : struct, IFunc<ReadOnlySpan<T>, bool>, allows ref struct
    {
        return _owner.VisitReadWindow(_registration, relativeOffset, length, ref visitor);
    }
}
