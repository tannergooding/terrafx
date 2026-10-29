// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Buffers;

/// <summary>Owns an immutable ordered sequence of read-only native buffer ranges.</summary>
/// <typeparam name="T">The unmanaged element type.</typeparam>
public sealed class BufferChain<T> : IDisposable
    where T : unmanaged
{
    private const int MaximumSegmentCount = 65_536;

    private ReentrantGate _gate;
    private bool _isDisposed;
    private readonly int _segmentCount;
    private readonly Segment[] _segments;

    /// <summary>Retains independent read leases for the ordered nonempty segments.</summary>
    /// <param name="segments">The source read leases in logical order.</param>
    /// <exception cref="ArgumentException">The segment array is default or contains a null lease.</exception>
    /// <exception cref="ObjectDisposedException">A source lease is disposed.</exception>
    /// <exception cref="OverflowException">The total logical length exceeds <see cref="long.MaxValue"/>.</exception>
    [SuppressMessage("Reliability", "CA2000", Justification = "Each clone is transferred to the private segment array or disposed in finally; prior transfers are released if construction fails.")]
    public BufferChain(ImmutableArray<BufferReadLease<T>> segments)
    {
        if (segments.IsDefault)
        {
            ThrowHelper.ThrowArgumentException("The segment array must be initialized.", nameof(segments));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(segments.Length, MaximumSegmentCount, nameof(segments));

        _segments = new Segment[segments.Length];
        long totalLength = 0;
        var retainedCount = 0;

        try
        {
            foreach (var sourceLease in segments)
            {
                ArgumentNullException.ThrowIfNull(sourceLease);
                var lease = sourceLease.Clone();
                var releaseLease = true;

                try
                {
                    var length = lease.Length;

                    if (length == 0)
                    {
                        continue;
                    }

                    var nextLength = checked(totalLength + length);
                    _segments[retainedCount++] = new Segment(lease, totalLength, length);
                    releaseLease = false;
                    totalLength = nextLength;
                }
                finally
                {
                    if (releaseLease)
                    {
                        lease.Dispose();
                    }
                }
            }
        }
        catch
        {
            for (var index = 0; index < retainedCount; index++)
            {
                _segments[index].Lease.Dispose();
            }

            throw;
        }

        _segmentCount = retainedCount;
        Length = totalLength;
    }

    private BufferChain(Segment[] segments, long length)
    {
        _segments = segments;
        _segmentCount = segments.Length;
        Length = length;
    }

    /// <summary>Gets the immutable checked logical element count.</summary>
    public long Length { get; }

    /// <summary>Gets the immutable number of retained nonempty segments.</summary>
    public int SegmentCount => _segmentCount;

    private static void EndOperation(BufferReadLease<T>[] operationLeases)
    {
        foreach (var lease in operationLeases)
        {
            lease.Dispose();
        }
    }

    private static bool VisitSegments<TVisitor>(BufferReadLease<T>[] operationLeases, scoped ref TVisitor visitor)
        where TVisitor : struct, IFunc<ReadOnlySpan<T>, bool>, allows ref struct
    {
        for (var index = 0; index < operationLeases.Length; index++)
        {
            var remaining = operationLeases[index].Length;
            nint relativeOffset = 0;

            while (remaining != 0)
            {
                var windowLength = remaining > int.MaxValue ? int.MaxValue : checked((int)remaining);

                if (!operationLeases[index].VisitWindow(relativeOffset, windowLength, ref visitor))
                {
                    return false;
                }

                relativeOffset = checked(relativeOffset + windowLength);
                remaining -= windowLength;
            }
        }

        return true;
    }

    /// <summary>Copies an in-range logical range into a managed destination.</summary>
    /// <param name="sourceOffset">The nonnegative logical element offset.</param>
    /// <param name="destination">The destination span.</param>
    /// <exception cref="ArgumentOutOfRangeException">The requested logical range is outside this chain.</exception>
    /// <exception cref="ObjectDisposedException">The chain is disposed.</exception>
    public void CopyTo(long sourceOffset, Span<T> destination)
    {
        var operationLeases = BeginOperation(sourceOffset, destination.Length);

        try
        {
            var requestEnd = checked(sourceOffset + destination.Length);
            var destinationOffset = 0;

            for (var index = 0; index < _segmentCount && destinationOffset < destination.Length; index++)
            {
                var segment = _segments[index];
                var segmentEnd = checked(segment.LogicalOffset + segment.Length);

                if (sourceOffset >= segmentEnd || requestEnd <= segment.LogicalOffset)
                {
                    continue;
                }

                var copyStart = Math.Max(sourceOffset, segment.LogicalOffset);
                var copyEnd = Math.Min(requestEnd, segmentEnd);
                var segmentOffset = checked((nint)(copyStart - segment.LogicalOffset));
                var copyLength = checked((int)(copyEnd - copyStart));
                operationLeases[index].CopyWindow(segmentOffset, destination.Slice(destinationOffset, copyLength));
                destinationOffset += copyLength;
            }
        }
        finally
        {
            EndOperation(operationLeases);
        }
    }

    /// <summary>Releases the chain's retained segment credits and closes new operations.</summary>
    public void Dispose()
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
        }

        for (var index = 0; index < _segmentCount; index++)
        {
            _segments[index].Lease.Dispose();
        }
    }

    /// <summary>Returns an independent chain retaining the same ordered segments.</summary>
    /// <returns>The new chain.</returns>
    /// <exception cref="ObjectDisposedException">The chain is disposed.</exception>
    public BufferChain<T> Retain()
    {
        var operationLeases = BeginOperation(sourceOffset: null, destinationLength: 0);
        var ownershipTransferred = false;

        try
        {
            var segments = new Segment[_segmentCount];

            for (var index = 0; index < _segmentCount; index++)
            {
                var source = _segments[index];
                segments[index] = new Segment(operationLeases[index], source.LogicalOffset, source.Length);
            }

            var retained = new BufferChain<T>(segments, Length);
            ownershipTransferred = true;
            return retained;
        }
        finally
        {
            if (!ownershipTransferred)
            {
                EndOperation(operationLeases);
            }
        }
    }

    /// <summary>Visits each segment in logical order using maximal int-sized windows.</summary>
    /// <typeparam name="TVisitor">The concrete visitor type, which may be a ref struct.</typeparam>
    /// <param name="visitor">The borrowed visitor state.</param>
    /// <returns><see langword="true"/> when traversal completes; otherwise, <see langword="false"/> when the visitor stops.</returns>
    /// <exception cref="InvalidOperationException">The visitor throws, which is propagated after operation cleanup.</exception>
    /// <exception cref="ObjectDisposedException">The chain is disposed.</exception>
    public bool VisitWindows<TVisitor>(scoped ref TVisitor visitor)
        where TVisitor : struct, IFunc<ReadOnlySpan<T>, bool>, allows ref struct
    {
        var operationLeases = BeginOperation(sourceOffset: null, destinationLength: 0);

        try
        {
            return VisitSegments(operationLeases, ref visitor);
        }
        finally
        {
            EndOperation(operationLeases);
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Every acquired operation lease must be released before propagating an admission failure.")]
    private BufferReadLease<T>[] BeginOperation(long? sourceOffset, int destinationLength)
    {
        using (ReentrantGate.EnterScope(ref _gate))
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (sourceOffset.HasValue)
            {
                ValidateRange(sourceOffset.GetValueOrDefault(), destinationLength);
            }

            var operationLeases = new BufferReadLease<T>[_segmentCount];
            var acquiredCount = 0;

            try
            {
                for (; acquiredCount < _segmentCount; acquiredCount++)
                {
                    var lease = _segments[acquiredCount].Lease.Clone();
                    operationLeases[acquiredCount] = lease;

                    if (lease.Length != _segments[acquiredCount].Length)
                    {
                        ThrowHelper.ThrowInvalidOperationException("A retained segment changed length.");
                    }
                }
            }
            catch
            {
                for (var index = 0; index <= acquiredCount && index < operationLeases.Length; index++)
                {
                    operationLeases[index]?.Dispose();
                }

                throw;
            }

            return operationLeases;
        }
    }

    private void ValidateRange(long sourceOffset, int destinationLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceOffset, Length);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(destinationLength, Length - sourceOffset, nameof(destinationLength));
    }

    private readonly struct Segment(BufferReadLease<T> lease, long logicalOffset, nint length)
    {
        internal BufferReadLease<T> Lease { get; } = lease;

        internal long LogicalOffset { get; } = logicalOffset;

        internal nint Length { get; } = length;
    }
}
