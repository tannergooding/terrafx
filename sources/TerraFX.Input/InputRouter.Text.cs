// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Input;

/// <summary>Provides window-bound text-session ownership for one HostLiveUI recipient.</summary>
public sealed partial class InputRouter
{
    internal TextInputSession? ActiveTextSession => _activeTextSession;

    internal long CurrentHostControlTimestamp => _hostControlTimestamp;

    internal bool IsTextInputOverflowed => _inputOverflowed;

    /// <summary>Begins one validated text session on this router's exact window route.</summary>
    /// <param name="options">The complete UI-owned text snapshot and caret.</param>
    /// <returns>The owner-thread text session.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">The options name another window.</exception>
    /// <exception cref="InvalidOperationException">This is not a HostLiveUI router or a session is already active.</exception>
    public TextInputSession BeginTextSession(TextInputSessionOptions options)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(options);

        if ((_recipientKind != InputRecipientKind.HostLiveUI) || (_window is not { } window))
        {
            throw new InvalidOperationException("Text sessions require a HostLiveUI router bound to a window.");
        }

        if (_inputOverflowed)
        {
            throw new InvalidOperationException("The input recipient must reconcile overflow before starting another text session.");
        }

        if (options.Window != window)
        {
            throw new ArgumentException("The text options must name this router's exact window.", nameof(options));
        }

        if (_activeTextSession is { IsActive: true })
        {
            throw new InvalidOperationException("This HostLiveUI window already has an active text session.");
        }

        var sessionId = _sequenceLease.Allocate();
        RecordTextInputControlSequence(sessionId);
        var session = new TextInputSession(
            this,
            _textInputBatchBudget,
            _sequenceLease,
            _ownerThreadId,
            sessionId,
            window,
            options);
        _activeTextSession = session;
        return session;
    }

    internal void LatchTextInputOverflow(long hostTimestamp, long hostSequence)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostSequence);

        if (_recipientKind != InputRecipientKind.HostLiveUI)
        {
            throw new InvalidOperationException("Only a HostLiveUI recipient can own a text session.");
        }

        if ((hostSequence < _lastSeenHostSequence)
            || (hostTimestamp < _lastSeenHostTimestamp))
        {
            throw new InvalidOperationException("Text overflow must preserve the recipient host sequence and timestamp order.");
        }

        _lastSeenHostSequence = Math.Max(_lastSeenHostSequence, hostSequence);
        _lastSeenHostTimestamp = Math.Max(_lastSeenHostTimestamp, hostTimestamp);
        _inputOverflowed = true;
        RecordUnacceptedSequence(hostSequence);
    }

    internal void RecordTextInputCapture(long hostTimestamp, long hostSequence)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegative(hostTimestamp);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostSequence);

        if (_recipientKind != InputRecipientKind.HostLiveUI)
        {
            throw new InvalidOperationException("Only a HostLiveUI recipient can capture text.");
        }

        if (_inputOverflowed)
        {
            throw new InvalidOperationException("The text recipient must reconcile input overflow before accepting capture.");
        }

        if ((hostSequence <= _lastSeenHostSequence)
            || (hostTimestamp < _lastSeenHostTimestamp))
        {
            throw new InvalidOperationException("Text capture must preserve host sequence and timestamp order.");
        }

        _lastSeenHostSequence = hostSequence;
        _lastSeenHostTimestamp = hostTimestamp;
    }

    internal void RecordTextInputControlSequence(long hostSequence)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hostSequence);

        if (hostSequence <= _lastSeenHostSequence)
        {
            throw new InvalidOperationException("Text-session controls must preserve the shared host sequence order.");
        }

        RecordHostControlSequence(hostSequence);
        _lastSeenHostTimestamp = Math.Max(_lastSeenHostTimestamp, _hostControlTimestamp);
    }

    internal void ReleaseTextSession(TextInputSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (ReferenceEquals(_activeTextSession, session))
        {
            _activeTextSession = null;
        }
    }
}
