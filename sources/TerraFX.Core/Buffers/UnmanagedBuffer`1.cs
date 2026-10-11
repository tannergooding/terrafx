// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TerraFX.Buffers;

/// <summary>Owns zero-initialized contiguous native storage for unmanaged elements.</summary>
/// <typeparam name="T">The unmanaged element type.</typeparam>
public sealed class UnmanagedBuffer<T> : IDisposable
    where T : unmanaged
{
    private static readonly nint s_typeAlignment = GetTypeAlignment();

    private ReentrantGate _gate;
    private readonly List<LeaseRegistration> _registrations = [];
    private nint _activeOperationCount;
    private nuint _address;
    private bool _isClosed;
    private nint _leaseCreditCount;
    private readonly INativeMemoryOperations _nativeMemoryOperations;
    private readonly nint _stride;
    private readonly bool _usesAlignedAllocator;
    private bool _ownerCreditActive = true;
    private bool _isFreed;
    private bool _ownedBufferCreditActive;
    private bool _ownedBufferCreditReleased;

    private UnmanagedBuffer(nint length, nint byteLength, nint alignment, nint stride, INativeMemoryOperations nativeMemoryOperations)
    {
        Length = length;
        ByteLength = byteLength;
        _stride = stride;
        _nativeMemoryOperations = nativeMemoryOperations;
        _usesAlignedAllocator = alignment > IntPtr.Size;
    }

    /// <summary>Releases the allocation-owner credit if deterministic disposal was missed.</summary>
    ~UnmanagedBuffer()
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            _isClosed = true;
            _ownerCreditActive = false;
            _ownedBufferCreditActive = false;
            _ownedBufferCreditReleased = true;
            _leaseCreditCount = 0;
            _activeOperationCount = 0;
            _registrations.Clear();
            TryFree();
        }
    }

    /// <summary>Gets the immutable checked logical byte extent.</summary>
    public nint ByteLength { get; }

    /// <summary>Gets the immutable element count.</summary>
    public nint Length { get; }

    /// <summary>Allocates zero-initialized native storage within the supplied alignment and byte budget.</summary>
    /// <param name="length">The nonnegative number of elements.</param>
    /// <param name="options">The allocation budget and alignment.</param>
    /// <returns>An owner for the allocation, including when <paramref name="length"/> is zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative or the rounded extent exceeds the configured budget.</exception>
    /// <exception cref="ArgumentException"><paramref name="options"/> does not satisfy the element alignment and stride.</exception>
    /// <exception cref="OverflowException">The byte extent, alignment rounding, or native conversion is not representable.</exception>
    /// <exception cref="OutOfMemoryException">The native allocator cannot satisfy the admitted allocation.</exception>
    [SuppressMessage("Design", "CA1000", Justification = "The accepted public API allocates through UnmanagedBuffer<T>.Allocate.")]
    public static UnmanagedBuffer<T> Allocate(nint length, BufferAllocationOptions options)
        => Allocate(length, options, NativeMemoryOperations.Instance);

    internal static UnmanagedBuffer<T> Allocate(nint length, BufferAllocationOptions options, INativeMemoryOperations nativeMemoryOperations)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(nativeMemoryOperations);

        var stride = (nint)Unsafe.SizeOf<T>();

        if ((stride % s_typeAlignment) != 0 || options.Alignment < s_typeAlignment)
        {
            ThrowHelper.ThrowArgumentException("The requested alignment and element stride do not satisfy the unmanaged type layout.", nameof(options));
        }

        nint byteLength;
        checked
        {
            byteLength = length * stride;
        }

        var remainder = byteLength % options.Alignment;
        var allocationByteLength = remainder == 0 ? byteLength : checked(byteLength + options.Alignment - remainder);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(allocationByteLength, options.MaximumAllocationBytes, nameof(length));

        var nativeByteLength = checked((nuint)allocationByteLength);
        var nativeAlignment = checked((nuint)options.Alignment);
        var buffer = new UnmanagedBuffer<T>(length, byteLength, options.Alignment, stride, nativeMemoryOperations);

        if (allocationByteLength == 0)
        {
            return buffer;
        }

        var allocation = nativeMemoryOperations.Allocate(nativeByteLength, nativeAlignment, buffer._usesAlignedAllocator);

        if (allocation is 0)
        {
            buffer.Dispose();
            ThrowHelper.ThrowOutOfMemoryException();
        }

        buffer._address = allocation;

        try
        {
            nativeMemoryOperations.Clear(allocation, nativeByteLength);
        }
        catch
        {
            buffer.Dispose();
            throw;
        }

        return buffer;
    }

    private static nint GetTypeAlignment()
    {
        var probe = default(AlignmentProbe);
        ref var beginning = ref Unsafe.As<AlignmentProbe, byte>(ref probe);
        ref var value = ref Unsafe.As<T, byte>(ref probe.Value);
        var alignment = Unsafe.ByteOffset(ref beginning, ref value);

        if (alignment <= 0)
        {
            ThrowHelper.ThrowInvalidOperationException("The unmanaged element alignment could not be established.");
        }

        return alignment;
    }

    private static bool Overlaps(nint offset, nint length, nint otherOffset, nint otherLength)
    {
        if (length == 0 || otherLength == 0)
        {
            return false;
        }

        return offset <= otherOffset
            ? otherOffset - offset < length
            : offset - otherOffset < otherLength;
    }

    private static void ValidateRange(nint offset, nint length, nint extent, string offsetName, string lengthName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset, offsetName);
        ArgumentOutOfRangeException.ThrowIfNegative(length, lengthName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, extent, offsetName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, extent - offset, lengthName);
    }

    /// <summary>Acquires an overlapping-read lease for a validated element range.</summary>
    /// <param name="offset">The nonnegative element offset.</param>
    /// <param name="length">The nonnegative element count.</param>
    /// <returns>A disposable read lease.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is outside this buffer.</exception>
    /// <exception cref="InvalidOperationException">The range conflicts with an overlapping write lease.</exception>
    /// <exception cref="ObjectDisposedException">The owner is closed.</exception>
    public BufferReadLease<T> AcquireRead(nint offset, nint length)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            ThrowIfPublicOwnerClosed();
            ValidateRange(offset, length, Length, nameof(offset), nameof(length));
            ThrowIfConflictingLease(offset, length, BufferAccess.Read);

            var registration = new LeaseRegistration(this, offset, length, BufferAccess.Read, isLeaseCredit: true);
            var lease = new BufferReadLease<T>(this, registration);
            _registrations.Add(registration);
            _leaseCreditCount++;
            return lease;
        }
    }

    /// <summary>Acquires an exclusive-write lease for a validated element range.</summary>
    /// <param name="offset">The nonnegative element offset.</param>
    /// <param name="length">The nonnegative element count.</param>
    /// <returns>A disposable write lease.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The range is outside this buffer.</exception>
    /// <exception cref="InvalidOperationException">The range conflicts with an overlapping lease.</exception>
    /// <exception cref="ObjectDisposedException">The owner is closed.</exception>
    public BufferWriteLease<T> AcquireWrite(nint offset, nint length)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            ThrowIfPublicOwnerClosed();
            ValidateRange(offset, length, Length, nameof(offset), nameof(length));
            ThrowIfConflictingLease(offset, length, BufferAccess.Write);

            var registration = new LeaseRegistration(this, offset, length, BufferAccess.Write, isLeaseCredit: true);
            var lease = new BufferWriteLease<T>(this, registration);
            _registrations.Add(registration);
            _leaseCreditCount++;
            return lease;
        }
    }

    /// <summary>Copies a managed source span into a validated range of native storage.</summary>
    /// <param name="source">The source span.</param>
    /// <param name="destinationOffset">The nonnegative destination element offset.</param>
    /// <exception cref="ArgumentOutOfRangeException">The destination range is outside this buffer.</exception>
    /// <exception cref="InvalidOperationException">An overlapping lease conflicts with the copied write.</exception>
    /// <exception cref="ObjectDisposedException">The owner is closed.</exception>
    public void CopyFrom(ReadOnlySpan<T> source, nint destinationOffset)
    {
        var registration = BeginCopyOperation(destinationOffset, source.Length, BufferAccess.Write);

        try
        {
            unsafe
            {
                var destination = new Span<T>((void*)GetElementAddress(registration.Offset), source.Length);
                source.CopyTo(destination);
            }
        }
        finally
        {
            EndOperation(registration);
        }
    }

    /// <summary>Copies a validated range from native storage into a managed destination.</summary>
    /// <param name="sourceOffset">The nonnegative source element offset.</param>
    /// <param name="destination">The destination span.</param>
    /// <exception cref="ArgumentOutOfRangeException">The source range is outside this buffer.</exception>
    /// <exception cref="InvalidOperationException">An overlapping lease conflicts with the copied read.</exception>
    /// <exception cref="ObjectDisposedException">The owner is closed.</exception>
    public void CopyTo(nint sourceOffset, Span<T> destination)
    {
        var registration = BeginCopyOperation(sourceOffset, destination.Length, BufferAccess.Read);

        try
        {
            unsafe
            {
                var source = new ReadOnlySpan<T>((void*)GetElementAddress(registration.Offset), destination.Length);
                source.CopyTo(destination);
            }
        }
        finally
        {
            EndOperation(registration);
        }
    }

    /// <summary>Closes new admissions and releases the allocation-owner credit.</summary>
    public void Dispose()
    {
        if (ReleaseOwnerCredit())
        {
            GC.SuppressFinalize(this);
        }
    }

    internal BufferReadLease<T> AcquireOwnedReadLease(nint offset, nint length)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            ObjectDisposedException.ThrowIf(!_ownedBufferCreditActive || _ownedBufferCreditReleased, typeof(OwnedBuffer<>));

            ValidateRange(offset, length, Length, nameof(offset), nameof(length));
            ThrowIfConflictingLease(offset, length, BufferAccess.Read);
            var registration = new LeaseRegistration(this, offset, length, BufferAccess.Read, isLeaseCredit: true);
            var lease = new BufferReadLease<T>(this, registration);
            _registrations.Add(registration);
            _leaseCreditCount++;
            return lease;
        }
    }

    internal BufferReadLease<T> CloneReadLease(LeaseRegistration registration)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            VerifyLease(registration, BufferAccess.Read);
            var clone = new LeaseRegistration(this, registration.Offset, registration.Length, BufferAccess.Read, isLeaseCredit: true);
            var lease = new BufferReadLease<T>(this, clone);
            _registrations.Add(clone);
            _leaseCreditCount++;
            return lease;
        }
    }

    internal void CopyFromReadLease(LeaseRegistration registration, nint relativeOffset, Span<T> destination)
    {
        var absoluteOffset = BeginLeaseWindow(registration, relativeOffset, destination.Length, BufferAccess.Read);

        try
        {
            unsafe
            {
                var source = new ReadOnlySpan<T>((void*)GetElementAddress(absoluteOffset), destination.Length);
                source.CopyTo(destination);
            }
        }
        finally
        {
            EndOperation(registration);
        }
    }

    internal nint GetLeaseLength(LeaseRegistration registration, BufferAccess access)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            VerifyLease(registration, access);
            return registration.Length;
        }
    }

    internal void ReleaseLease(LeaseRegistration registration)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (registration.IsDisposed)
            {
                return;
            }

            VerifyRegistrationOwner(registration);
            registration.IsDisposed = true;
            _leaseCreditCount--;

            if (registration.ActiveOperationCount == 0)
            {
                _ = _registrations.Remove(registration);
            }

            TryFree();
        }
    }

    internal void ReleaseOwnedBufferCredit()
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_ownedBufferCreditActive)
            {
                _ownedBufferCreditActive = false;
                _ownedBufferCreditReleased = true;
                TryFree();
            }
        }
    }

    internal void TransferToOwnedBuffer()
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            ThrowIfPublicOwnerClosed();

            foreach (var registration in _registrations)
            {
                if (registration.Access == BufferAccess.Write && (!registration.IsDisposed || registration.ActiveOperationCount != 0))
                {
                    ThrowHelper.ThrowInvalidOperationException("Writable leases must be disposed before ownership is finalized.");
                }
            }

            _ownerCreditActive = false;
            _ownedBufferCreditActive = true;
            _isClosed = true;
        }
    }

    internal void UseReadWindow<TAction>(LeaseRegistration registration, nint relativeOffset, int length, scoped ref TAction action)
        where TAction : struct, IAction<ReadOnlySpan<T>>, allows ref struct
    {
        var absoluteOffset = BeginLeaseWindow(registration, relativeOffset, length, BufferAccess.Read);

        try
        {
            unsafe
            {
                var window = new ReadOnlySpan<T>((void*)GetElementAddress(absoluteOffset), length);
                action.Invoke(window);
            }
        }
        finally
        {
            EndOperation(registration);
        }
    }

    internal void UseWriteWindow<TAction>(LeaseRegistration registration, nint relativeOffset, int length, scoped ref TAction action)
        where TAction : struct, IAction<Span<T>>, allows ref struct
    {
        var absoluteOffset = BeginLeaseWindow(registration, relativeOffset, length, BufferAccess.Write);

        try
        {
            unsafe
            {
                var window = new Span<T>((void*)GetElementAddress(absoluteOffset), length);
                action.Invoke(window);
            }
        }
        finally
        {
            EndOperation(registration);
        }
    }

    internal bool VisitReadWindow<TVisitor>(LeaseRegistration registration, nint relativeOffset, int length, scoped ref TVisitor visitor)
        where TVisitor : struct, IFunc<ReadOnlySpan<T>, bool>, allows ref struct
    {
        var absoluteOffset = BeginLeaseWindow(registration, relativeOffset, length, BufferAccess.Read);

        try
        {
            unsafe
            {
                var window = new ReadOnlySpan<T>((void*)GetElementAddress(absoluteOffset), length);
                return visitor.Invoke(window);
            }
        }
        finally
        {
            EndOperation(registration);
        }
    }

    private LeaseRegistration BeginCopyOperation(nint offset, int length, BufferAccess access)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            ThrowIfPublicOwnerClosed();
            ValidateRange(offset, length, Length, nameof(offset), nameof(length));
            ThrowIfConflictingLease(offset, length, access);

            var registration = new LeaseRegistration(this, offset, length, access, isLeaseCredit: false) {
                ActiveOperationCount = 1,
            };
            _registrations.Add(registration);
            _activeOperationCount++;
            return registration;
        }
    }

    private nint BeginLeaseWindow(LeaseRegistration registration, nint relativeOffset, int length, BufferAccess access)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            VerifyLease(registration, access);
            ValidateRange(relativeOffset, length, registration.Length, nameof(relativeOffset), nameof(length));

            if (access == BufferAccess.Write && registration.ActiveOperationCount != 0)
            {
                ThrowHelper.ThrowInvalidOperationException("A write lease admits at most one active window.");
            }

            registration.ActiveOperationCount++;
            _activeOperationCount++;
            return checked(registration.Offset + relativeOffset);
        }
    }

    private void EndOperation(LeaseRegistration registration)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            VerifyRegistrationOwner(registration);
            registration.ActiveOperationCount--;
            _activeOperationCount--;

            if (registration.ActiveOperationCount == 0 && (registration.IsDisposed || !registration.IsLeaseCredit))
            {
                registration.IsDisposed = true;
                _ = _registrations.Remove(registration);
            }

            TryFree();
        }
    }

    private nuint GetElementAddress(nint elementOffset)
    {
        var byteOffset = checked((nuint)elementOffset * (nuint)_stride);
        return checked(_address + byteOffset);
    }

    private bool ReleaseOwnerCredit()
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            _isClosed = true;

            if (_ownerCreditActive)
            {
                _ownerCreditActive = false;
                TryFree();
            }

            return _isFreed;
        }
    }

    private void ThrowIfConflictingLease(nint offset, nint length, BufferAccess access)
    {
        foreach (var registration in _registrations)
        {
            if (registration.IsDisposed && registration.ActiveOperationCount == 0)
            {
                continue;
            }

            if (access == BufferAccess.Read && registration.Access == BufferAccess.Read)
            {
                continue;
            }

            if (Overlaps(offset, length, registration.Offset, registration.Length))
            {
                ThrowHelper.ThrowInvalidOperationException("The requested buffer range conflicts with an active lease.");
            }
        }
    }

    private void ThrowIfPublicOwnerClosed()
    {
        ObjectDisposedException.ThrowIf(_isClosed || !_ownerCreditActive, this);
    }

    private void TryFree()
    {
        if (!_isFreed && _isClosed && !_ownerCreditActive && !_ownedBufferCreditActive && _leaseCreditCount == 0 && _activeOperationCount == 0)
        {
            if (_address != 0)
            {
                _nativeMemoryOperations.Free(_address, _usesAlignedAllocator);
                _address = 0;
            }

            _isFreed = true;
        }
    }

    private void VerifyLease(LeaseRegistration registration, BufferAccess access)
    {
        VerifyRegistrationOwner(registration);

        if (registration.IsDisposed)
        {
            ObjectDisposedException.ThrowIf(true, access == BufferAccess.Read ? typeof(BufferReadLease<>) : typeof(BufferWriteLease<>));
        }

        if (registration.Access != access || !_registrations.Contains(registration))
        {
            ThrowHelper.ThrowInvalidOperationException("The buffer lease registration is invalid.");
        }
    }

    private void VerifyRegistrationOwner(LeaseRegistration registration)
    {
        if (!ReferenceEquals(registration.Owner, this))
        {
            ThrowHelper.ThrowInvalidOperationException("The buffer lease belongs to a different allocation.");
        }
    }

    internal sealed class LeaseRegistration
    {
        internal LeaseRegistration(UnmanagedBuffer<T> owner, nint offset, nint length, BufferAccess access, bool isLeaseCredit)
        {
            Owner = owner;
            Offset = offset;
            Length = length;
            Access = access;
            IsLeaseCredit = isLeaseCredit;
        }

        internal BufferAccess Access { get; }

        internal nint ActiveOperationCount { get; set; }

        internal bool IsDisposed { get; set; }

        internal bool IsLeaseCredit { get; }

        internal nint Length { get; }

        internal nint Offset { get; }

        internal UnmanagedBuffer<T> Owner { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AlignmentProbe
    {
        public byte Prefix;
        public T Value;
    }
}
