// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Input;

/// <summary>Owns one bounded text-input session for a HostLiveUI window route.</summary>
public sealed class TextInputSession : IDisposable
{
    private bool _awaitingSnapshot;
    private readonly Queue<TextInputBatch> _batches = [];
    private readonly TextInputBatchBudget _budget;
    private bool _compositionActive;
    private long _documentRevision;
    private bool _isActive = true;
    private Diagnostic? _lastDiagnostic;
    private long _lastHostSequence;
    private long _lastHostTimestamp;
    private long _mirrorRevision;
    private TextInputSessionOptions? _options;
    private readonly int _ownerThreadId;
    private InputRouter? _router;
    private int _selectionLength;
    private int _selectionStart;
    private readonly HostEventSequenceLease _sequenceLease;
    private readonly long _sessionId;
    private string? _shadowText;
    private readonly WindowHandle _window;

    internal TextInputSession(
        InputRouter router,
        TextInputBatchBudget budget,
        HostEventSequenceLease sequenceLease,
        int ownerThreadId,
        long sessionId,
        WindowHandle window,
        TextInputSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(sequenceLease);
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerThreadId, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sessionId);
        ArgumentNullException.ThrowIfNull(options);

        if (!window.IsValid || (options.Window != window))
        {
            throw new ArgumentException("The session options must name its exact valid window route.", nameof(options));
        }

        if (ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("A text input session must be created on its host owner thread.");
        }

        _router = router;
        _budget = budget;
        _sequenceLease = sequenceLease;
        _ownerThreadId = ownerThreadId;
        _sessionId = sessionId;
        _window = window;
        _options = options;
        _documentRevision = options.DocumentRevision;
        _mirrorRevision = options.DocumentRevision;
        _shadowText = options.SurroundingText;
        _selectionStart = options.SelectionStart;
        _selectionLength = options.SelectionLength;
        _lastHostSequence = sessionId;
        _lastHostTimestamp = router.CurrentHostControlTimestamp;
    }

    /// <summary>Gets the UI document revision most recently supplied to this session.</summary>
    /// <exception cref="InvalidOperationException">The property is read from another thread.</exception>
    public long DocumentRevision
    {
        get
        {
            EnsureOwnerThread();
            return _documentRevision;
        }
    }

    /// <summary>Gets whether native text capture is still accepted for this session.</summary>
    /// <exception cref="InvalidOperationException">The property is read from another thread.</exception>
    public bool IsActive
    {
        get
        {
            EnsureOwnerThread();
            return _isActive && (_router is not null);
        }
    }

    /// <summary>Gets the latest text-session diagnostic.</summary>
    /// <exception cref="InvalidOperationException">The property is read from another thread.</exception>
    public Diagnostic? LastDiagnostic
    {
        get
        {
            EnsureOwnerThread();
            return _lastDiagnostic;
        }
    }

    /// <summary>Gets the positive host-issued identity for this text session.</summary>
    public long SessionId => _sessionId;

    /// <summary>Gets the exact window route owned by this session.</summary>
    public WindowHandle Window => _window;

    private static Diagnostic CreateDiagnostic(string code, string operation, long hostTimestamp, string message)
        => new(
            code,
            DiagnosticSeverity.Error,
            hostTimestamp,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            operation,
            null,
            message,
            []);

    private static bool SplitsSurrogatePair(string text, int index)
        => (index > 0)
            && (index < text.Length)
            && char.IsHighSurrogate(text[index - 1])
            && char.IsLowSurrogate(text[index]);

    private static bool TryApplyDelete(string? text, int start, int length, out string? updatedText)
    {
        updatedText = text;

        if (text is null
            || (start > text.Length)
            || (length > text.Length - start)
            || SplitsSurrogatePair(text, start)
            || SplitsSurrogatePair(text, start + length))
        {
            return false;
        }

        updatedText = text.Remove(start, length);
        return true;
    }

    private static bool TryReplaceText(string text, int start, int length, string replacement, out string updatedText)
    {
        updatedText = text;

        if ((start > text.Length)
            || (length > text.Length - start)
            || SplitsSurrogatePair(text, start)
            || SplitsSurrogatePair(text, start + length)
            || (replacement.Length > (65_536 - (text.Length - length))))
        {
            return false;
        }

        updatedText = string.Concat(text.AsSpan(0, start), replacement.AsSpan(), text.AsSpan(start + length));
        return true;
    }

    /// <summary>Disposes native text ownership while preserving already published batches.</summary>
    [SuppressMessage(
        "Design",
        "CA1065:Do not raise exceptions in unexpected locations",
        Justification = "Text-session disposal is host-owner-thread-affine; off-owner disposal must not appear successful while native ownership is active.")]
    public void Dispose()
    {
        EnsureOwnerThread();
        var router = _router;

        if (!_isActive || (router is null))
        {
            return;
        }

        var hostSequence = _sequenceLease.Allocate();
        router.RecordTextInputControlSequence(hostSequence);
        EndSession(
            hostSequence,
            Math.Max(_lastHostTimestamp, router.CurrentHostControlTimestamp),
            TextCompositionCancelReason.SessionDisposed);
    }

    /// <summary>Returns the next complete immutable native text transaction.</summary>
    /// <param name="batch">The next copied batch, or null when no batch is queued.</param>
    /// <returns><see langword="true"/> when a complete batch was returned.</returns>
    /// <exception cref="InvalidOperationException">The method is called from another thread.</exception>
    public bool TryReadBatch([NotNullWhen(true)] out TextInputBatch? batch)
    {
        EnsureOwnerThread();

        if (_batches.TryDequeue(out var result))
        {
            _budget.Release(result);
            batch = result;
            return true;
        }

        batch = null;
        return false;
    }

    /// <summary>Replaces the authoritative UI snapshot after a complete text edit.</summary>
    /// <param name="options">The complete new snapshot and caret.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">The window, purpose, or hints differ, or the revision trails the native mirror.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The revision is not strictly greater than the prior UI revision.</exception>
    /// <exception cref="InvalidOperationException">The session is inactive or called off the owner thread.</exception>
    public void Update(TextInputSessionOptions options)
    {
        EnsureActiveOwnerThread();
        ArgumentNullException.ThrowIfNull(options);
        var router = _router
            ?? throw new InvalidOperationException("The active text session has no owner router.");
        var currentOptions = _options
            ?? throw new InvalidOperationException("The active text session has no current options.");

        if ((options.Window != _window)
            || (options.Purpose != currentOptions.Purpose)
            || (options.Hints != currentOptions.Hints))
        {
            throw new ArgumentException("An update must preserve the window, purpose, and frozen text hints.", nameof(options));
        }

        if (options.DocumentRevision <= _documentRevision)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "A text-session update requires a strictly greater document revision.");
        }

        if (options.DocumentRevision < _mirrorRevision)
        {
            throw new ArgumentException("The new snapshot does not include all accepted native text edits.", nameof(options));
        }

        if (router.IsTextInputOverflowed)
        {
            throw new InvalidOperationException("The input recipient must reconcile overflow before updating text state.");
        }

        var hostSequence = _sequenceLease.Allocate();
        router.RecordTextInputControlSequence(hostSequence);
        _options = options;
        _documentRevision = options.DocumentRevision;
        _mirrorRevision = options.DocumentRevision;
        _shadowText = options.SurroundingText;
        _selectionStart = options.SelectionStart;
        _selectionLength = options.SelectionLength;
        _lastHostSequence = hostSequence;
        _lastHostTimestamp = Math.Max(_lastHostTimestamp, router.CurrentHostControlTimestamp);
        _awaitingSnapshot = false;
        _lastDiagnostic = null;
    }

    internal void EndForNativeControl(
        long hostSequence,
        long hostTimestamp,
        TextCompositionCancelReason cancellationReason)
    {
        EnsureActiveOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostSequence);
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);

        if (!Enum.IsDefined(cancellationReason))
        {
            throw new ArgumentOutOfRangeException(nameof(cancellationReason));
        }

        if ((hostSequence <= _lastHostSequence) || (hostTimestamp < _lastHostTimestamp))
        {
            throw new InvalidOperationException("Text control boundaries must preserve host sequence and timestamp order.");
        }

        _lastHostSequence = hostSequence;
        _lastHostTimestamp = hostTimestamp;
        EndSession(hostSequence, hostTimestamp, cancellationReason);
    }

    internal void EndForRecipientDisposal(long hostSequence, long hostTimestamp)
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("A text input session must end on its host owner thread.");
        }

        if (_isActive && (_router is not null))
        {
            EndSession(
                hostSequence,
                Math.Max(_lastHostTimestamp, hostTimestamp),
                TextCompositionCancelReason.SessionDisposed);
        }
    }

    internal TextInputBatchPublishStatus PublishNativeBatch(
        long hostTimestamp,
        long hostSequence,
        long baseDocumentRevision,
        ImmutableArray<TextInputChange> changes)
    {
        EnsureActiveOwnerThread();
        var router = _router
            ?? throw new InvalidOperationException("The active text session has no owner router.");
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostSequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseDocumentRevision);

        if (changes.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A native text transaction must contain a bounded initialized change array.", nameof(changes));
        }

        if ((hostSequence <= _lastHostSequence) || (hostTimestamp < _lastHostTimestamp))
        {
            throw new InvalidOperationException("Text batches must retain increasing host sequence and nondecreasing host timestamp order.");
        }

        if (router.IsTextInputOverflowed)
        {
            _lastHostSequence = hostSequence;
            _lastHostTimestamp = hostTimestamp;
            return OverflowNativeBatch(
                hostTimestamp,
                hostSequence,
                "The input recipient is waiting for state reconciliation.");
        }

        router.RecordTextInputCapture(hostTimestamp, hostSequence);
        _lastHostSequence = hostSequence;
        _lastHostTimestamp = hostTimestamp;

        if (changes.Length > TextInputBatchBudget.MaximumPendingChanges)
        {
            return OverflowNativeBatch(
                hostTimestamp,
                hostSequence,
                "The native text transaction exceeded the per-recipient change limit.");
        }

        foreach (var change in changes)
        {
            if (change.Kind == TextInputChangeKind.Invalid)
            {
                throw new ArgumentException("A native text transaction cannot contain default change values.", nameof(changes));
            }
        }

        var inputTextBytes = 0;

        foreach (var change in changes)
        {
            inputTextBytes = checked(inputTextBytes + change.Utf8TextBytes);

            if (inputTextBytes > TextInputBatchBudget.MaximumPendingTextBytes)
            {
                return OverflowNativeBatch(
                    hostTimestamp,
                    hostSequence,
                    "The native text transaction exceeded the per-recipient text byte limit.");
            }
        }

        if (_awaitingSnapshot
            || (baseDocumentRevision != _mirrorRevision)
            || (_documentRevision != _mirrorRevision))
        {
            return PublishStaleMirror(hostTimestamp, hostSequence, baseDocumentRevision);
        }

        var output = ImmutableArray.CreateBuilder<TextInputChange>(changes.Length);
        var shadowText = _shadowText;
        var selectionStart = _selectionStart;
        var selectionLength = _selectionLength;
        var compositionActive = _compositionActive;
        var documentChanged = false;
        string? updatedText;

        foreach (var change in changes)
        {
            if (change.Kind == TextInputChangeKind.StaleMirror)
            {
                throw new ArgumentException("The native text source cannot issue the engine-owned StaleMirror change.", nameof(changes));
            }

            switch (change.Kind)
            {
                case TextInputChangeKind.CompositionStarted:
                {
                    if (compositionActive)
                    {
                        output.Add(CreateRejectedChange(
                            hostTimestamp,
                            "TextInput.CompositionOrder",
                            "A composition was started while another composition was active."));
                    }
                    else
                    {
                        compositionActive = true;
                        output.Add(change);
                    }

                    break;
                }

                case TextInputChangeKind.CompositionUpdated:
                {
                    if (!compositionActive)
                    {
                        output.Add(CreateRejectedChange(
                            hostTimestamp,
                            "TextInput.CompositionOrder",
                            "A composition update arrived without an active composition."));
                    }
                    else
                    {
                        output.Add(change);
                    }

                    break;
                }

                case TextInputChangeKind.TextEditRequested:
                {
                    if (baseDocumentRevision == long.MaxValue)
                    {
                        output.Add(CreateRejectedChange(
                            hostTimestamp,
                            "TextInput.DocumentRevisionExhausted",
                            "The document revision cannot advance for a native text edit."));
                        break;
                    }

                    if (TryApplyDelete(
                        shadowText,
                        change.EditStart.GetValueOrDefault(),
                        change.DeleteLength.GetValueOrDefault(),
                        out updatedText))
                    {
                        shadowText = updatedText;
                        selectionStart = change.EditStart.GetValueOrDefault();
                        selectionLength = 0;
                        documentChanged = true;
                        output.Add(change);
                    }
                    else
                    {
                        output.Add(CreateRejectedChange(
                            hostTimestamp,
                            "TextInput.TextEditRejected",
                            "The native deletion range is outside the supplied surrounding-text snapshot."));
                    }

                    break;
                }

                case TextInputChangeKind.TextCommitted:
                {
                    if (baseDocumentRevision == long.MaxValue)
                    {
                        output.Add(CreateRejectedChange(
                            hostTimestamp,
                            "TextInput.DocumentRevisionExhausted",
                            "The document revision cannot advance for a committed text edit."));
                        break;
                    }

                    if (shadowText is null)
                    {
                        if ((change.EditStart is not null) || (change.DeleteLength is not null))
                        {
                            output.Add(CreateRejectedChange(
                                hostTimestamp,
                                "TextInput.TextEditRejected",
                                "A commit replacement range requires a surrounding-text snapshot."));
                            break;
                        }

                        output.Add(change);
                        documentChanged = true;
                        break;
                    }

                    var editStart = change.EditStart ?? selectionStart;
                    var deleteLength = change.DeleteLength ?? selectionLength;

                    if (!TryReplaceText(shadowText, editStart, deleteLength, change.Text!, out updatedText))
                    {
                        output.Add(CreateRejectedChange(
                            hostTimestamp,
                            "TextInput.TextEditRejected",
                            "The committed replacement range is outside the supplied surrounding-text snapshot or exceeds its bound."));
                        break;
                    }

                    var normalizedCommit = new TextInputChange(
                        TextInputChangeKind.TextCommitted,
                        change.Text,
                        editStart: editStart,
                        deleteLength: deleteLength);
                    shadowText = updatedText;
                    selectionStart = checked(editStart + change.Text!.Length);
                    selectionLength = 0;
                    documentChanged = true;
                    output.Add(normalizedCommit);
                    break;
                }

                case TextInputChangeKind.CompositionEnded:
                case TextInputChangeKind.CompositionCanceled:
                {
                    if (!compositionActive)
                    {
                        output.Add(CreateRejectedChange(
                            hostTimestamp,
                            "TextInput.CompositionOrder",
                            "A composition terminal record arrived without an active composition."));
                    }
                    else
                    {
                        compositionActive = false;
                        output.Add(change);
                    }

                    break;
                }

                case TextInputChangeKind.TextEditRejected:
                {
                    _lastDiagnostic = change.Diagnostic;
                    output.Add(change);
                    break;
                }

                default:
                {
                    throw new ArgumentOutOfRangeException(nameof(changes), "The native text change kind is undefined.");
                }
            }
        }

        if (output.Count == 0)
        {
            throw new InvalidOperationException("A native text transaction produced no observable changes.");
        }

        var resultRevision = documentChanged
            ? checked(baseDocumentRevision + 1)
            : baseDocumentRevision;
        var batch = new TextInputBatch(
            _sessionId,
            _window,
            hostTimestamp,
            hostSequence,
            baseDocumentRevision,
            resultRevision,
            output.MoveToImmutable());

        if (!TryQueueBatch(batch, hostTimestamp, hostSequence))
        {
            return TextInputBatchPublishStatus.CapacityExceeded;
        }

        _shadowText = shadowText;
        _selectionStart = selectionStart;
        _selectionLength = selectionLength;
        _compositionActive = compositionActive;
        _mirrorRevision = resultRevision;
        return TextInputBatchPublishStatus.Published;
    }

    private TextInputChange CreateRejectedChange(long hostTimestamp, string code, string message)
    {
        var diagnostic = CreateDiagnostic(code, "TextInputSession.ApplyNativeBatch", hostTimestamp, message);
        _lastDiagnostic = diagnostic;
        return new TextInputChange(TextInputChangeKind.TextEditRejected, diagnostic: diagnostic);
    }

    private void EndSession(long hostSequence, long hostTimestamp, TextCompositionCancelReason reason)
    {
        var router = _router;

        if (_compositionActive)
        {
            var change = new TextInputChange(
                TextInputChangeKind.CompositionCanceled,
                cancellationReason: reason);
            var batch = new TextInputBatch(
                _sessionId,
                _window,
                hostTimestamp,
                hostSequence,
                _mirrorRevision,
                _mirrorRevision,
                [change]);

            if (!TryQueueBatch(batch, hostTimestamp, hostSequence))
            {
                _lastDiagnostic = CreateDiagnostic(
                    "TextInput.Overflow",
                    "TextInputSession.Dispose",
                    hostTimestamp,
                    "The text output budget was exhausted while ending a composition.");
            }

            _compositionActive = false;
        }

        _isActive = false;
        router?.ReleaseTextSession(this);
        _router = null;
        _options = null;
        _shadowText = null;
        _selectionStart = 0;
        _selectionLength = 0;
        _awaitingSnapshot = false;
    }

    private void EnsureActiveOwnerThread()
    {
        EnsureOwnerThread();

        if (!_isActive || (_router is null))
        {
            throw new InvalidOperationException("The text input session is inactive.");
        }
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("A text input session is restricted to its host owner thread.");
        }
    }

    private TextInputBatchPublishStatus OverflowNativeBatch(long hostTimestamp, long hostSequence, string message)
    {
        _lastDiagnostic = CreateDiagnostic(
            "TextInput.Overflow",
            "TextInputSession.PublishNativeBatch",
            hostTimestamp,
            message);
        _isActive = false;
        _compositionActive = false;
        var router = _router;
        router?.ReleaseTextSession(this);
        router?.LatchTextInputOverflow(hostTimestamp, hostSequence);
        _router = null;
        _options = null;
        _shadowText = null;
        _selectionStart = 0;
        _selectionLength = 0;
        _awaitingSnapshot = false;
        return TextInputBatchPublishStatus.CapacityExceeded;
    }

    private TextInputBatchPublishStatus PublishStaleMirror(long hostTimestamp, long hostSequence, long suppliedBaseRevision)
    {
        var diagnostic = CreateDiagnostic(
            "TextInput.StaleMirror",
            "TextInputSession.PublishNativeBatch",
            hostTimestamp,
            "The native text mirror does not match the latest authoritative UI snapshot.");
        _lastDiagnostic = diagnostic;
        _awaitingSnapshot = true;
        var changes = ImmutableArray.Create(new TextInputChange(
            TextInputChangeKind.StaleMirror,
            diagnostic: diagnostic));
        var batch = new TextInputBatch(
            _sessionId,
            _window,
            hostTimestamp,
            hostSequence,
            suppliedBaseRevision,
            suppliedBaseRevision,
            changes);
        return TryQueueBatch(batch, hostTimestamp, hostSequence)
            ? TextInputBatchPublishStatus.StaleMirror
            : TextInputBatchPublishStatus.CapacityExceeded;
    }

    private bool TryQueueBatch(TextInputBatch batch, long hostTimestamp, long hostSequence)
    {
        if (!_budget.TryReserve(batch.Changes, out _))
        {
            _ = OverflowNativeBatch(
                hostTimestamp,
                hostSequence,
                "The per-recipient text output budget was exhausted.");
            return false;
        }

        _batches.Enqueue(batch);
        return true;
    }
}
