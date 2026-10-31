// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Input;

internal sealed class TextInputBatchBudget
{
    internal const int MaximumPendingChanges = 4096;
    internal const int MaximumPendingTextBytes = 256 * 1024;

    private readonly int _ownerThreadId;
    private int _pendingChangeCount;
    private int _pendingTextBytes;

    internal TextInputBatchBudget(int ownerThreadId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerThreadId, 1);
        _ownerThreadId = ownerThreadId;
    }

    internal void Release(TextInputBatch batch)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(batch);

        var textBytes = 0;

        foreach (var change in batch.Changes)
        {
            textBytes = checked(textBytes + change.Utf8TextBytes);
        }

        _pendingChangeCount -= batch.Changes.Length;
        _pendingTextBytes -= textBytes;

        if ((_pendingChangeCount < 0) || (_pendingTextBytes < 0))
        {
            throw new InvalidOperationException("The text input output budget was released more than once.");
        }
    }

    internal bool TryReserve(ImmutableArray<TextInputChange> changes, out int textBytes)
    {
        EnsureOwnerThread();
        textBytes = 0;

        foreach (var change in changes)
        {
            textBytes = checked(textBytes + change.Utf8TextBytes);
        }

        if ((changes.Length > (MaximumPendingChanges - _pendingChangeCount))
            || (textBytes > (MaximumPendingTextBytes - _pendingTextBytes)))
        {
            return false;
        }

        _pendingChangeCount += changes.Length;
        _pendingTextBytes += textBytes;
        return true;
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("The text input output budget is restricted to its host owner thread.");
        }
    }
}
