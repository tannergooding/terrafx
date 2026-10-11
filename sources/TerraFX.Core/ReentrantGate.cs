// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX;

// Keep this embedded in its owner; copying it after use duplicates the lock state.
/// <summary>Provides a reentrant spin gate stored directly in its owning state.</summary>
internal struct ReentrantGate
{
    // A default SpinLock is unlocked with owner tracking disabled.
    private SpinLock _spinLock;
    private int _ownerThreadId;
    private int _recursionCount;

    /// <summary>Enters the gate and returns a scope that releases it on disposal.</summary>
    internal static Scope EnterScope(ref ReentrantGate gate)
    {
        var currentThreadId = Environment.CurrentManagedThreadId;

        if (Volatile.Read(ref gate._ownerThreadId) == currentThreadId)
        {
            gate._recursionCount = checked(gate._recursionCount + 1);
            return new Scope(ref gate, currentThreadId);
        }

        var lockTaken = false;
        gate._spinLock.Enter(ref lockTaken);
        Volatile.Write(ref gate._ownerThreadId, currentThreadId);
        gate._recursionCount = 1;
        return new Scope(ref gate, currentThreadId);
    }

    private void Exit(int threadId)
    {
        if (Volatile.Read(ref _ownerThreadId) != threadId)
        {
            ThrowHelper.ThrowSynchronizationLockException();
        }

        _recursionCount--;

        if (_recursionCount == 0)
        {
            Volatile.Write(ref _ownerThreadId, 0);
            _spinLock.Exit();
        }
    }

    /// <summary>Releases a gate acquisition when disposed.</summary>
    internal ref struct Scope
    {
        private ref ReentrantGate _gate;
        private readonly int _threadId;
        private bool _hasGate;

        internal Scope(ref ReentrantGate gate, int threadId)
        {
            _gate = ref gate;
            _threadId = threadId;
            _hasGate = true;
        }

        public void Dispose()
        {
            if (_hasGate)
            {
                _gate.Exit(_threadId);
                _hasGate = false;
            }
        }
    }
}
