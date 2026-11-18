// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldSceneStorageReservation : IDisposable
{
    internal const long ControlByteCharge = 72;

    private const int StateDisposed = 3;
    private const int StateReturningStorage = 5;
    private const int StateReturningWorkspace = 4;
    private const int StateStorage = 2;
    private const int StateTransferring = 6;
    private const int StateWorkspace = 1;

    private readonly long _bytes;
    private WorldSceneStorageReservation? _next;
    private WorldDomainReservation? _parent;
    private WorldSceneStorageReservation? _previous;
    private object? _retainedOwner;
    private int _state;
    private WorldGuidUseControl? _workspaceUseControl;

    internal WorldSceneStorageReservation(
        WorldDomainReservation parent,
        long bytes,
        WorldGuidUseControl? workspaceUseControl)
    {
        _parent = parent;
        _bytes = bytes;
        _workspaceUseControl = workspaceUseControl;
    }

    internal long Bytes => _bytes;

    internal long DataBytes => _bytes - ControlByteCharge;

    internal bool IsConstructing => _state == 0;

    internal bool IsDisposed => _state == StateDisposed;

    internal bool IsReturningStorage => _state == StateReturningStorage;

    internal bool IsReturningWorkspace => _state == StateReturningWorkspace;

    internal bool IsStorage => _state == StateStorage;

    internal bool IsWorkspace => _state == StateWorkspace;

    internal WorldSceneStorageReservation? Next => _next;

    internal WorldDomainReservation? Parent => _parent;

    internal WorldSceneStorageReservation? Previous => _previous;

    internal object? RetainedOwner => _retainedOwner;

    internal WorldGuidUseControl? WorkspaceUseControl => _workspaceUseControl;

    internal static long GetRequiredBytes(long dataBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dataBytes);
        return checked(dataBytes + ControlByteCharge);
    }

    public void Dispose()
    {
        var spinner = new SpinWait();

        while (true)
        {
            var state = Volatile.Read(ref _state);

            if (state is StateDisposed or StateReturningStorage or StateReturningWorkspace)
            {
                return;
            }

            if (state == StateTransferring)
            {
                spinner.SpinOnce();
                continue;
            }

            if (state is not (StateStorage or StateWorkspace))
            {
                throw new InvalidOperationException("The Scene reservation has not completed construction.");
            }

            var returningState = (state == StateStorage) ? StateReturningStorage : StateReturningWorkspace;

            if (Interlocked.CompareExchange(ref _state, returningState, state) == state)
            {
                break;
            }
        }

        var parent = Volatile.Read(ref _parent);
        parent?.ReleaseSceneReservation(this);
    }

    internal void MarkReleased()
    {
        _retainedOwner = null;
        _workspaceUseControl = null;
        _previous = null;
        _next = null;
        _state = StateDisposed;
        Volatile.Write(ref _parent, null);
    }

    internal void MarkStorage(WorldSceneStorageReservation? next)
    {
        _workspaceUseControl = null;
        _next = next;
        Volatile.Write(ref _state, StateStorage);
    }

    internal void MarkWorkspace()
        => _state = StateWorkspace;

    internal void SetNext(WorldSceneStorageReservation? next)
        => _next = next;

    internal void SetPrevious(WorldSceneStorageReservation? previous)
        => _previous = previous;

    internal void SetRetainedOwner(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var parent = Volatile.Read(ref _parent)
            ?? throw new ObjectDisposedException(nameof(WorldSceneStorageReservation));
        parent.SetSceneReservationOwner(this, owner);
    }

    internal void SetRetainedOwnerCore(object owner)
        => _retainedOwner = owner;

    internal bool TryBeginStorageInstallation(bool constructing)
    {
        var expectedState = constructing ? 0 : StateWorkspace;
        return Interlocked.CompareExchange(ref _state, StateTransferring, expectedState) == expectedState;
    }

    internal bool TryTransferWorkspaceToStorage()
    {
        var parent = Volatile.Read(ref _parent);
        return (parent is not null) && parent.TryInstallSceneWorkspace(this);
    }
}
