// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TerraFX.Graphics;

internal sealed class GraphicsOwnerRequestQueue : IDisposable
{
    private const int RequestCapacity = 256;
    private const int RetirementCapacity = 256;

    private bool _isDisposed;
    private readonly Channel<IGraphicsDeviceWork> _requests = Channel.CreateBounded<IGraphicsDeviceWork>(new BoundedChannelOptions(RequestCapacity) {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private readonly Lock _retirementGate = new();
    private readonly Channel<byte> _retirementReservations = Channel.CreateBounded<byte>(new BoundedChannelOptions(RetirementCapacity) {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private bool _retirementReservationsClosed;
    private readonly Channel<IGraphicsDeviceWork> _retirements = Channel.CreateBounded<IGraphicsDeviceWork>(new BoundedChannelOptions(RetirementCapacity) {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private bool _retirementsClosed;
    private readonly AutoResetEvent _workAvailable = new(initialState: false);

    internal GraphicsOwnerRequestQueue()
    {
        for (var index = 0; index < RetirementCapacity; index++)
        {
            if (!_retirementReservations.Writer.TryWrite(0))
            {
                throw new InvalidOperationException("Failed to initialize reserved Graphics retirement capacity.");
            }
        }
    }

    internal bool RequestsCompleted => _requests.Reader.Completion.IsCompleted;

    public void Dispose()
    {
        _ = _requests.Writer.TryComplete();

        lock (_retirementGate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _retirementReservationsClosed = true;
            _retirementsClosed = true;
            _ = _retirementReservations.Writer.TryComplete();
            _ = _retirements.Writer.TryComplete();
            _workAvailable.Dispose();
        }
    }

    internal void CompleteRequests()
    {
        _ = _requests.Writer.TryComplete();
        SignalWork();
    }

    internal void CompleteRetirementReservations()
    {
        lock (_retirementGate)
        {
            CloseRetirementReservationsUnderLock();
        }
    }

    internal void CompleteRetirements()
    {
        lock (_retirementGate)
        {
            if (!_retirementsClosed)
            {
                _retirementsClosed = true;
                _ = _retirements.Writer.TryComplete();
                CloseRetirementReservationsUnderLock();
                SignalWorkUnderLock();
            }
        }
    }

    internal async ValueTask<GraphicsRetirementReservation> ReserveRetirementAsync(CancellationToken cancellationToken)
    {
        lock (_retirementGate)
        {
            ObjectDisposedException.ThrowIf(_retirementReservationsClosed, this);
        }

        try
        {
            _ = await _retirementReservations.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            lock (_retirementGate)
            {
                ObjectDisposedException.ThrowIf(_retirementReservationsClosed, this);
            }

            throw;
        }

        lock (_retirementGate)
        {
            ObjectDisposedException.ThrowIf(_retirementReservationsClosed, this);

            return new GraphicsRetirementReservation(this);
        }
    }

    internal bool TryEnqueueRetirement(IGraphicsDeviceWork work, GraphicsRetirementReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(reservation);

        if (!reservation.IsOwnedBy(this))
        {
            throw new ArgumentException("The retirement reservation belongs to a different Graphics owner queue.", nameof(reservation));
        }

        lock (_retirementGate)
        {
            if (_retirementsClosed)
            {
                return false;
            }

            if (!reservation.TryMarkQueued())
            {
                throw new InvalidOperationException("The Graphics retirement reservation has already been consumed.");
            }

            if (!_retirements.Writer.TryWrite(work))
            {
                throw new InvalidOperationException("Reserved Graphics retirement capacity was unavailable.");
            }

            SignalWorkUnderLock();
            return true;
        }
    }

    internal bool TryReadRequest([NotNullWhen(true)] out IGraphicsDeviceWork? work)
        => _requests.Reader.TryRead(out work);

    internal bool TryReadRetirement([NotNullWhen(true)] out IGraphicsDeviceWork? work)
        => _retirements.Reader.TryRead(out work);

    internal void WaitForWork()
        => _workAvailable.WaitOne();

    internal async ValueTask WriteRequestAsync(IGraphicsDeviceWork work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await _requests.Writer.WriteAsync(work, cancellationToken).ConfigureAwait(false);
        SignalWork();
    }

    private void CloseRetirementReservationsUnderLock()
    {
        if (!_retirementReservationsClosed)
        {
            _retirementReservationsClosed = true;
            _ = _retirementReservations.Writer.TryComplete();
            SignalWorkUnderLock();
        }
    }

    private void ReleaseRetirementReservation(GraphicsRetirementReservation reservation)
    {
        lock (_retirementGate)
        {
            if (!reservation.TryMarkReleased())
            {
                return;
            }

            if (_retirementReservationsClosed)
            {
                return;
            }

            if (!_retirementReservations.Writer.TryWrite(0))
            {
                throw new InvalidOperationException("Reserved Graphics retirement capacity was released more than once.");
            }
        }
    }

    private void SignalWork()
    {
        lock (_retirementGate)
        {
            SignalWorkUnderLock();
        }
    }

    private void SignalWorkUnderLock()
    {
        if (!_isDisposed)
        {
            _ = _workAvailable.Set();
        }
    }

    internal sealed class GraphicsRetirementReservation : IDisposable
    {
        private readonly GraphicsOwnerRequestQueue _queue;
        // 0 is reserved, 1 is queued, and 2 is released.
        private int _state;

        internal GraphicsRetirementReservation(GraphicsOwnerRequestQueue queue)
        {
            _queue = queue;
        }

        public void Dispose()
        {
            _queue.ReleaseRetirementReservation(this);
        }

        internal bool IsOwnedBy(GraphicsOwnerRequestQueue queue)
            => ReferenceEquals(_queue, queue);

        internal bool TryMarkQueued()
        {
            if (_state != 0)
            {
                return false;
            }

            _state = 1;
            return true;
        }

        internal bool TryMarkReleased()
        {
            if (_state == 2)
            {
                return false;
            }

            _state = 2;
            return true;
        }
    }
}
