// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Assets;

internal sealed class OriginalIoLifetime
{
    internal const int NativeStateNotAcquired = 0;
    internal const int NativeStateReleased = 2;
    internal const int TerminalExecutionReturned = 3;
    internal const int TerminalServiceReturned = 4;
    internal const int TerminalStateLive = 0;
    internal const int TerminalStatePublished = 2;
    internal const int TerminalStatePublishing = 1;

    internal int _blockReleaseState;

    internal nint _event;

    internal int _eventCloseError;

    internal int _eventCloseState;

    internal OriginalPackageIo? _executionState;

    internal AssetPackageBacking? _generationHead;

    internal int _lifecycle;

    internal long _nativeGenerationInterests;

    internal nint _overlapped;

    internal nint _scratch;

    internal AssetStore.OriginalOperation? _serviceReturn;

    internal long _sourceClaim;

    ~OriginalIoLifetime()
    {
        if (_executionState is { _thread: not null } executionState)
        {
            executionState.PublishRetirement(this);
        }
    }

    internal static (AssetStore.OriginalIssuer Issuer, long SourceBytes) DetachTerminalExecutionState(
        OriginalIoLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        var executionState = lifetime._executionState
            ?? throw new InvalidOperationException("The terminal execution state has already been detached.");
        return executionState.DetachTerminalExecutionState(lifetime);
    }

    internal (AssetStore.OriginalIssuer Issuer, long SourceBytes) DetachServiceProvision()
        => Volatile.Read(ref _lifecycle) switch {
            TerminalStateLive => DetachUnstartedService(),
            TerminalExecutionReturned => DetachTerminalService(),
            _ => throw new InvalidOperationException("The original service provision has already been returned."),
        };

    internal (AssetStore.OriginalIssuer Issuer, long SourceBytes) DetachTerminalService()
    {
        var serviceReturn = _serviceReturn
            ?? throw new InvalidOperationException("The terminal service has no return owner.");
        var issuer = serviceReturn.Issuer;
        var sourceClaim = OriginalPackageIoSourceCapacity.ServiceBytes
            - AssetStore.OriginalStoreSourceBytes
            - AssetStore.OriginalIssuerSourceBytes
            - OriginalPackageIoSourceCapacity.ExecutionStateBytes;

        if (!ReferenceEquals(serviceReturn.IoLifetime, this)
            || (_executionState is not null)
            || (_lifecycle is not TerminalExecutionReturned)
            || (Interlocked.Read(ref _sourceClaim) != sourceClaim)
            || (_generationHead is not null)
            || (_nativeGenerationInterests is not 0)
            || (_event != 0)
            || (_overlapped != 0)
            || (_scratch != 0)
            || (_eventCloseError is not 0)
            || (_blockReleaseState is not (NativeStateNotAcquired or NativeStateReleased))
            || (_eventCloseState is not (NativeStateNotAcquired or NativeStateReleased)))
        {
            throw new InvalidOperationException("The terminal service still has a source, native resource, or generation interest.");
        }

        if (Interlocked.CompareExchange(
            ref _lifecycle,
            TerminalServiceReturned,
            TerminalExecutionReturned) is not TerminalExecutionReturned)
        {
            throw new InvalidOperationException("The terminal service has already been returned.");
        }

        if (Interlocked.CompareExchange(ref _sourceClaim, 0, sourceClaim) != sourceClaim)
        {
            throw new InvalidOperationException("The terminal service source claim changed during handback.");
        }

        _serviceReturn = null;
        return (issuer, sourceClaim);
    }

    internal (AssetStore.OriginalIssuer Issuer, long SourceBytes) DetachUnstartedService()
    {
        var serviceReturn = _serviceReturn
            ?? throw new InvalidOperationException("The unstarted service has no return owner.");
        var executionState = _executionState
            ?? throw new InvalidOperationException("The unstarted service has no execution state.");
        var issuer = serviceReturn.Issuer;
        var sourceClaim = OriginalPackageIoSourceCapacity.ServiceBytes
            - AssetStore.OriginalStoreSourceBytes
            - AssetStore.OriginalIssuerSourceBytes;

        if (!ReferenceEquals(serviceReturn.IoLifetime, this)
            || !ReferenceEquals(executionState._issuer, issuer)
            || (_lifecycle is not TerminalStateLive)
            || (Interlocked.Read(ref _sourceClaim) != sourceClaim)
            || (executionState._thread is not null)
            || (executionState.RetiringOwner is not null)
            || executionState.IsStopRequested
            || (Volatile.Read(ref executionState._queueAttemptState) is not OriginalPackageIo.CallbackNotEntered)
            || (Volatile.Read(ref executionState._joinState) is not OriginalPackageIo.JoinNotStarted)
            || (executionState._activeRequest is not null)
            || (executionState._queueGate is not null)
            || (executionState._queueHead is not null)
            || (executionState._queueTail is not null)
            || (executionState._returningHead is not null)
            || (executionState._queuedCount is not 0)
            || (Volatile.Read(ref executionState._nativeUse) is not 0)
            || (Volatile.Read(ref executionState._publicationUse) is not 0)
            || (Volatile.Read(ref executionState._startPermit) is not 0)
            || (_generationHead is not null)
            || (_nativeGenerationInterests is not 0)
            || (_event != 0)
            || (_overlapped != 0)
            || (_scratch != 0)
            || (_eventCloseError is not 0)
            || (_blockReleaseState is not NativeStateNotAcquired)
            || (_eventCloseState is not NativeStateNotAcquired))
        {
            throw new InvalidOperationException("The original service has started or still has an engine-owned use or resource.");
        }

        if (Interlocked.CompareExchange(ref _lifecycle, TerminalServiceReturned, TerminalStateLive)
            is not TerminalStateLive)
        {
            throw new InvalidOperationException("The unstarted original service has already been returned.");
        }

        if (Interlocked.CompareExchange(ref _sourceClaim, 0, sourceClaim) != sourceClaim)
        {
            throw new InvalidOperationException("The unstarted original service source claim changed during handback.");
        }

        executionState._issuer = null;
        executionState._queueGate = null;
        executionState._queueHead = null;
        executionState._queueTail = null;
        executionState._retirementCallback = null;
        executionState._returningHead = null;
        _executionState = null;
        _serviceReturn = null;
        return (issuer, sourceClaim);
    }
}
