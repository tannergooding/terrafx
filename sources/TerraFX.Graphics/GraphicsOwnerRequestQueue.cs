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
    private PendingRequestWriter? _pendingRequestHead;
    private PendingRequestWriter? _pendingRequestTail;
    private int _requestCount;
    private int _requestHead;
    private readonly IGraphicsDeviceWork?[] _requests = new IGraphicsDeviceWork?[RequestCapacity];
    private bool _requestsClosed;
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

    internal static long KnownRequestBackingByteCharge
        => checked(64 + ((checked(RequestCapacity * (long)IntPtr.Size) + 15) & ~15L));

    internal long RequestBackingByteCharge
        => checked(64 + ((checked(_requests.LongLength * IntPtr.Size) + 15) & ~15L));

    internal int RequestStorageCapacity => _requests.Length;

    internal bool RequestsCompleted
    {
        get
        {
            lock (_retirementGate)
            {
                return _requestsClosed && (_requestCount == 0);
            }
        }
    }

    private static void ClearRequestInputs(ref IGraphicsDeviceWork? work, ref CancellationToken cancellationToken)
    {
        work = null;
        cancellationToken = default;
    }

    private static void CompletePendingRequestWriters(PendingRequestWriter? writer)
    {
        while (writer is not null)
        {
            var next = writer.Next;
            writer.Next = null;

            if (writer.State == 1)
            {
                _ = writer.Completion.TrySetResult();
            }
            else if (writer.State == 2)
            {
                _ = writer.Completion.TrySetCanceled(writer.CancellationToken);
            }
            else if (writer.State == 4)
            {
                _ = writer.Completion.TrySetException(
                    new InvalidOperationException("The Graphics graph-retention cells are unavailable."));
            }
            else
            {
                _ = writer.Completion.TrySetException(new ChannelClosedException());
            }

            writer = next;
        }
    }

    private static async ValueTask WaitForPendingRequestAsync(PendingRequestWriter writer)
    {
        try
        {
            using var registration = writer.CancellationToken.UnsafeRegister(
                static state => {
                    ArgumentNullException.ThrowIfNull(state);
                    var pending = (PendingRequestWriter)state;
                    pending.Queue?.CancelPendingRequest(pending);
                },
                writer);
            await writer.Completion.Task.ConfigureAwait(false);
        }
        finally
        {
            writer.Queue = null;
            writer.CancellationToken = default;
        }
    }

    public void Dispose()
    {
        PendingRequestWriter? pending;

        lock (_retirementGate)
        {
            if (_isDisposed)
            {
                return;
            }

            pending = CloseRequestsUnderLock();
            _isDisposed = true;
            _retirementReservationsClosed = true;
            _retirementsClosed = true;
            _ = _retirementReservations.Writer.TryComplete();
            _ = _retirements.Writer.TryComplete();
            _workAvailable.Dispose();
        }

        CompletePendingRequestWriters(pending);
    }

    internal void CompleteRequests()
    {
        PendingRequestWriter? pending;

        lock (_retirementGate)
        {
            pending = CloseRequestsUnderLock();
            SignalWorkUnderLock();
        }

        CompletePendingRequestWriters(pending);
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

    internal void ReleaseGraphRetentionCell(IGraphicsGraphRetentionCellWork work)
    {
        ArgumentNullException.ThrowIfNull(work);

        lock (_retirementGate)
        {
            work.ReleaseRetentionCell();
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

    internal void SignalOwnerWork()
        => SignalWork();

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
    {
        PendingRequestWriter? completed = null;

        lock (_retirementGate)
        {
            if (_requestCount == 0)
            {
                work = null;
                return false;
            }

            work = _requests[_requestHead]
                ?? throw new InvalidOperationException("The Graphics request ring contains an empty live slot.");
            _requests[_requestHead] = null;
            _requestHead = (_requestHead + 1) % _requests.Length;
            _requestCount--;

            // Promote the oldest ordinary writer before exposing the freed slot.
            // A nonwaiting diagnostic producer cannot overtake accepted ordering.
            while (_pendingRequestHead is PendingRequestWriter writer)
            {
                RemovePendingRequestUnderLock(writer);

                if (writer.CancellationToken.IsCancellationRequested)
                {
                    writer.State = 2;
                    writer.Work = null;
                    writer.Next = completed;
                    completed = writer;
                    continue;
                }

                var accepted = writer.Work
                    ?? throw new InvalidOperationException("A pending Graphics request lost its work.");
                writer.Work = null;

                if (!TryEnqueueRequestUnderLock(accepted))
                {
                    writer.State = 4;
                    writer.Next = completed;
                    completed = writer;
                    continue;
                }

                writer.State = 1;
                writer.Next = completed;
                completed = writer;
                break;
            }
        }

        CompletePendingRequestWriters(completed);
        return true;
    }

    internal bool TryReadRetirement([NotNullWhen(true)] out IGraphicsDeviceWork? work)
        => _retirements.Reader.TryRead(out work);

    internal bool TryWriteDiagnosticRequest(IGraphicsDeviceWork work, long maximumBackingBytes)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBackingBytes);

        if ((RequestBackingByteCharge > maximumBackingBytes) || !_retirementGate.TryEnter())
        {
            return false;
        }

        try
        {
            if (_requestsClosed
                || (_requestCount == _requests.Length)
                || !TryEnqueueRequestUnderLock(work))
            {
                return false;
            }

            SignalWorkUnderLock();
            return true;
        }
        finally
        {
            _retirementGate.Exit();
        }
    }

    internal void WaitForWork()
        => _workAvailable.WaitOne();

    internal async ValueTask WriteRequestAsync(IGraphicsDeviceWork? work, CancellationToken cancellationToken)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(work);
            var admission = WriteRequestCore(work, cancellationToken);
            ClearRequestInputs(ref work, ref cancellationToken);
            await admission.ConfigureAwait(false);
        }
        finally
        {
            ClearRequestInputs(ref work, ref cancellationToken);
        }
    }

    private void CancelPendingRequest(PendingRequestWriter writer)
    {
        lock (_retirementGate)
        {
            if (writer.State != 0)
            {
                return;
            }

            RemovePendingRequestUnderLock(writer);
            writer.State = 2;
            writer.Work = null;
        }

        _ = writer.Completion.TrySetCanceled(writer.CancellationToken);
    }

    private PendingRequestWriter? CloseRequestsUnderLock()
    {
        _requestsClosed = true;
        var pending = _pendingRequestHead;
        _pendingRequestHead = null;
        _pendingRequestTail = null;

        for (var writer = pending; writer is not null; writer = writer.Next)
        {
            writer.Previous = null;
            writer.State = 3;
            writer.Work = null;
        }

        return pending;
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

    private void EnqueueRequestUnderLock(IGraphicsDeviceWork work)
    {
        _requests[(_requestHead + _requestCount) % _requests.Length] = work;
        _requestCount++;
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

    private void RemovePendingRequestUnderLock(PendingRequestWriter writer)
    {
        if (writer.Previous is null)
        {
            _pendingRequestHead = writer.Next;
        }
        else
        {
            writer.Previous.Next = writer.Next;
        }

        if (writer.Next is null)
        {
            _pendingRequestTail = writer.Previous;
        }
        else
        {
            writer.Next.Previous = writer.Previous;
        }

        writer.Next = null;
        writer.Previous = null;
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

    private bool TryEnqueueRequestUnderLock(IGraphicsDeviceWork work)
    {
        if ((work is IGraphicsGraphRetentionCellWork graphWork)
            && !graphWork.TryClaimRetentionCell())
        {
            return false;
        }

        EnqueueRequestUnderLock(work);
        return true;
    }

    private ValueTask WriteRequestCore(IGraphicsDeviceWork work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();
        PendingRequestWriter? writer = null;
        var retentionCellsUnavailable = false;

        lock (_retirementGate)
        {
            if (_requestsClosed)
            {
                throw new ChannelClosedException();
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (_requestCount < _requests.Length)
            {
                if (TryEnqueueRequestUnderLock(work))
                {
                    SignalWorkUnderLock();
                    return ValueTask.CompletedTask;
                }

                retentionCellsUnavailable = true;
            }
            else
            {
                writer = new PendingRequestWriter(this, work, cancellationToken) {
                    Previous = _pendingRequestTail,
                };

                if (_pendingRequestTail is null)
                {
                    _pendingRequestHead = writer;
                }
                else
                {
                    _pendingRequestTail.Next = writer;
                }

                _pendingRequestTail = writer;
            }
        }

        if (retentionCellsUnavailable)
        {
            throw new InvalidOperationException("The Graphics graph-retention cells are unavailable.");
        }

        return WaitForPendingRequestAsync(writer
            ?? throw new InvalidOperationException("A pending Graphics request lost its admission owner."));
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

    private sealed class PendingRequestWriter
    {
        internal PendingRequestWriter(GraphicsOwnerRequestQueue queue, IGraphicsDeviceWork work, CancellationToken cancellationToken)
        {
            Queue = queue;
            Work = work;
            CancellationToken = cancellationToken;
        }

        internal CancellationToken CancellationToken { get; set; }

        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal PendingRequestWriter? Next { get; set; }

        internal PendingRequestWriter? Previous { get; set; }

        internal GraphicsOwnerRequestQueue? Queue { get; set; }

        // Pending, accepted, canceled, or closed. Changes linearize at the queue gate.
        internal int State { get; set; }

        internal IGraphicsDeviceWork? Work { get; set; }
    }
}
