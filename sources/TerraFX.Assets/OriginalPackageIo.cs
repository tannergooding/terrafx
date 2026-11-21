// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Assets;

internal sealed class OriginalPackageIo
{
    internal const int CallbackEntered = 1;
    internal const int CallbackNotEntered = 0;
    internal const int JoinNotStarted = 0;
    internal const int JoinPending = 1;
    internal const int JoinReturned = 2;
    internal const int TerminalReturned = 3;

    internal OriginalIoRequest? _activeRequest;

    internal AssetStore.OriginalIssuer? _issuer;

    internal int _joinState;

    internal int _loopReturnObservation;

    internal int _nativeUse;

    internal long _nextRequestSequence;

    internal int _publicationUse;

    internal int _queueAttemptState;

    internal Lock? _queueGate;

    internal OriginalIoRequest? _queueHead;

    internal OriginalIoRequest? _queueTail;

    internal int _queuedCount;

    internal Action<object?>? _retirementCallback;

    internal OriginalIoLifetime? _retiringOwner;

    internal OriginalIoRequest? _returningHead;

    internal int _startPermit;

    internal int _stopRequest;

    internal Thread? _thread;

    internal bool IsStopRequested
        => Volatile.Read(ref _stopRequest) is not 0;

    internal OriginalIoLifetime? RetiringOwner
        => Volatile.Read(ref _retiringOwner);

    internal static void RunTerminalReturnCallback(object? state)
    {
        if (state is not OriginalPackageIo executionState)
        {
            throw new ArgumentException("The terminal callback state is invalid.", nameof(state));
        }

        executionState.ObserveTerminalCallbackEntered();
        var lifetime = executionState.RetiringOwner
            ?? throw new InvalidOperationException("The terminal callback has no retirement owner.");
        var thread = executionState._thread
            ?? throw new InvalidOperationException("The terminal callback has no worker thread.");

        if (ReferenceEquals(thread, Thread.CurrentThread))
        {
            throw new InvalidOperationException("The terminal callback cannot join its own worker thread.");
        }

        executionState.BeginTerminalJoin();
        thread.Join();
        executionState.ObserveTerminalJoinReturned();
        OriginalIoLifetime.CompleteTerminalReturn(lifetime);
    }

    private static bool IsReturnedOrUnacquired(int state)
        => state is OriginalIoLifetime.NativeStateNotAcquired or OriginalIoLifetime.NativeStateReleased;

    internal void BeginTerminalJoin()
    {
        if ((RetiringOwner is null)
            || (Volatile.Read(ref _stopRequest) is 0)
            || (Volatile.Read(ref _queueAttemptState) is not CallbackEntered)
            || (_thread is null))
        {
            throw new InvalidOperationException("The terminal worker is not ready to join.");
        }

        if (Interlocked.CompareExchange(ref _joinState, JoinPending, JoinNotStarted)
            is not JoinNotStarted)
        {
            throw new InvalidOperationException("The terminal worker join was already started.");
        }
    }

    internal (AssetStore.OriginalIssuer Issuer, long SourceBytes) DetachTerminalExecutionState(
        OriginalIoLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        var serviceReturn = lifetime._serviceReturn
            ?? throw new InvalidOperationException("The terminal source owner has no service return.");
        var issuer = serviceReturn.Issuer;

        if (!ReferenceEquals(lifetime._executionState, this)
            || !ReferenceEquals(RetiringOwner, lifetime)
            || !ReferenceEquals(_issuer, issuer)
            || (Volatile.Read(ref lifetime._lifecycle) is not OriginalIoLifetime.TerminalStatePublished)
            || (Volatile.Read(ref _stopRequest) is 0)
            || (Volatile.Read(ref _queueAttemptState) is not CallbackEntered)
            || (Volatile.Read(ref _joinState) is not JoinReturned)
            || (_thread is null)
            || (_activeRequest is not null)
            || (_queueHead is not null)
            || (_queueTail is not null)
            || (_returningHead is not null)
            || (_queuedCount is not 0)
            || (Volatile.Read(ref _nativeUse) is not 0)
            || (Volatile.Read(ref _publicationUse) is not 0)
            || (Volatile.Read(ref _startPermit) is not 0)
            || (lifetime._generationHead is not null)
            || (lifetime._nativeGenerationInterests is not 0)
            || (lifetime._event != 0)
            || (lifetime._overlapped != 0)
            || (lifetime._scratch != 0)
            || (lifetime._eventCloseError is not 0)
            || !IsReturnedOrUnacquired(lifetime._blockReleaseState)
            || !IsReturnedOrUnacquired(lifetime._eventCloseState))
        {
            throw new InvalidOperationException("The terminal execution state still has an engine-owned use or resource.");
        }

        var sourceClaim = Interlocked.Read(ref lifetime._sourceClaim);

        if (sourceClaim < OriginalPackageIoSourceCapacity.ExecutionStateBytes)
        {
            throw new InvalidOperationException("The terminal execution-state source claim is inconsistent.");
        }

        if (Interlocked.CompareExchange(
            ref lifetime._lifecycle,
            OriginalIoLifetime.TerminalExecutionReturned,
            OriginalIoLifetime.TerminalStatePublished) is not OriginalIoLifetime.TerminalStatePublished)
        {
            throw new InvalidOperationException("The terminal execution state has already been returned.");
        }

        var observedSourceClaim = Interlocked.CompareExchange(
            ref lifetime._sourceClaim,
            sourceClaim - OriginalPackageIoSourceCapacity.ExecutionStateBytes,
            sourceClaim);

        if (observedSourceClaim != sourceClaim)
        {
            throw new InvalidOperationException("The terminal execution-state source claim changed during handback.");
        }

        _activeRequest = null;
        _issuer = null;
        _queueGate = null;
        _queueHead = null;
        _queueTail = null;
        _queuedCount = 0;
        _retirementCallback = null;
        _retiringOwner = null;
        _returningHead = null;
        _thread = null;
        _queueAttemptState = TerminalReturned;
        _joinState = TerminalReturned;
        lifetime._executionState = null;
        return (issuer, OriginalPackageIoSourceCapacity.ExecutionStateBytes);
    }

    internal void ObserveTerminalCallbackEntered()
    {
        var lifetime = RetiringOwner;

        if ((lifetime is null)
            || (Volatile.Read(ref _stopRequest) is 0)
            || (Volatile.Read(ref lifetime._lifecycle) is not OriginalIoLifetime.TerminalStatePublished))
        {
            throw new InvalidOperationException("The terminal callback has no published retirement owner.");
        }

        if (Interlocked.CompareExchange(ref _queueAttemptState, CallbackEntered, CallbackNotEntered)
            is not CallbackNotEntered)
        {
            throw new InvalidOperationException("The terminal callback entry was already observed.");
        }
    }

    internal void ObserveTerminalJoinReturned()
    {
        if (Interlocked.CompareExchange(ref _joinState, JoinReturned, JoinPending)
            is not JoinPending)
        {
            throw new InvalidOperationException("The terminal worker join return was not pending.");
        }
    }

    internal void PublishRetirement(OriginalIoLifetime lifetime)
    {
        if (Interlocked.CompareExchange(
            ref lifetime._lifecycle,
            OriginalIoLifetime.TerminalStatePublishing,
            OriginalIoLifetime.TerminalStateLive) is not OriginalIoLifetime.TerminalStateLive)
        {
            return;
        }

        var current = Interlocked.CompareExchange(ref _retiringOwner, lifetime, null);

        if ((current is null) || ReferenceEquals(current, lifetime))
        {
            Volatile.Write(ref _stopRequest, 1);
            Volatile.Write(ref lifetime._lifecycle, OriginalIoLifetime.TerminalStatePublished);
        }
    }
}
