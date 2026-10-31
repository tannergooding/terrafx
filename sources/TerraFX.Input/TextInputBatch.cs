// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Input;

/// <summary>Stores one immutable ordered text-session transaction.</summary>
public sealed class TextInputBatch
{
    internal TextInputBatch(
        long sessionId,
        WindowHandle window,
        long hostTimestamp,
        long hostSequence,
        long baseDocumentRevision,
        long resultDocumentRevision,
        ImmutableArray<TextInputChange> changes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);

        if (!window.IsValid)
        {
            throw new ArgumentException("A valid window identity is required.", nameof(window));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostSequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseDocumentRevision);

        if (changes.IsDefaultOrEmpty || (changes.Length > TextInputBatchBudget.MaximumPendingChanges))
        {
            throw new ArgumentException("A text batch requires a bounded nonempty change array.", nameof(changes));
        }

        var changesDocument = false;
        var textBytes = 0;

        foreach (var change in changes)
        {
            if (change.Kind == TextInputChangeKind.Invalid)
            {
                throw new ArgumentException("A text batch cannot contain default change values.", nameof(changes));
            }

            changesDocument |= change.ChangesDocument;
            textBytes = checked(textBytes + change.Utf8TextBytes);

            if (textBytes > TextInputBatchBudget.MaximumPendingTextBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(changes), "A text batch cannot exceed the per-recipient text byte limit.");
            }
        }

        if (changesDocument)
        {
            if ((baseDocumentRevision == long.MaxValue)
                || (resultDocumentRevision != baseDocumentRevision + 1))
            {
                throw new ArgumentException("A text-edit batch must advance the document revision exactly once.", nameof(resultDocumentRevision));
            }
        }
        else if (resultDocumentRevision != baseDocumentRevision)
        {
            throw new ArgumentException("A composition-only or rejected text batch cannot advance the document revision.", nameof(resultDocumentRevision));
        }

        SessionId = sessionId;
        Window = window;
        HostTimestamp = hostTimestamp;
        HostSequence = hostSequence;
        BaseDocumentRevision = baseDocumentRevision;
        ResultDocumentRevision = resultDocumentRevision;
        Changes = [.. changes];
    }

    /// <summary>Gets the UI document revision against which the changes were prepared.</summary>
    public long BaseDocumentRevision { get; }

    /// <summary>Gets the immutable changes in native transaction order.</summary>
    public ImmutableArray<TextInputChange> Changes { get; }

    /// <summary>Gets the host sequence shared by this native transaction.</summary>
    public long HostSequence { get; }

    /// <summary>Gets the normalized host timestamp of the native transaction.</summary>
    public long HostTimestamp { get; }

    /// <summary>Gets the resulting UI document revision after the transaction.</summary>
    public long ResultDocumentRevision { get; }

    /// <summary>Gets the owning text-session identity.</summary>
    public long SessionId { get; }

    /// <summary>Gets the exact engine-issued window identity.</summary>
    public WindowHandle Window { get; }
}
