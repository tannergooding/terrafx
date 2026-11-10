// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Platform;

[SuppressMessage(
    "Design",
    "TFX0002:Order type members by category, staticness, accessibility, and name using ordinal comparison",
    Justification = "Keep the Wayland text-input protocol lifecycle, callbacks, and state transitions grouped by protocol flow.")]
internal sealed unsafe partial class WaylandPlatformBackend
{
    private readonly record struct WaylandTextInputCommitState(
        WindowHandle Window,
        long SessionId,
        long DocumentRevision,
        int SnapshotTextLength,
        WaylandTextInputSurroundingText? SurroundingText);

    private const uint TextInputContentHintSpellcheck = 0x002;
    private const uint TextInputContentHintHiddenText = 0x040;
    private const uint TextInputContentHintSensitiveData = 0x080;
    private const uint TextInputContentHintMultiline = 0x200;
    private const uint TextInputChangeCauseInputMethod = 0;
    private const uint TextInputChangeCauseOther = 1;
    private const uint TextInputContentPurposeNormal = 0;
    private const uint TextInputContentPurposeNumber = 3;
    private const uint TextInputContentPurposePhone = 4;
    private const uint TextInputContentPurposeUrl = 5;
    private const uint TextInputContentPurposeEmail = 6;
    private const uint TextInputContentPurposePassword = 8;

    public PlatformTextInputCapabilities UpdateTextInputSession(
        WindowHandle window,
        TextInputSessionSnapshot? snapshot)
    {
        EnsureActiveOwnerThread();

        if (!window.IsValid || (window.OwnerId != _ownerId))
        {
            throw new ArgumentException("The text input session belongs to another Wayland platform owner.", nameof(window));
        }

        var state = GetWindowState(window);

        if (snapshot is not { } session)
        {
            if (state.TextInputSession is TextInputSessionSnapshot previousSession
                && (_textInputExpectedDocumentSessionId == previousSession.SessionId))
            {
                _textInputExpectedDocumentSessionId = 0;
                _textInputExpectedDocumentRevision = 0;
            }

            if (_textInputEnabledWindow == state)
            {
                DeactivateTextInput(state, TextCompositionCancelReason.SessionDisposed, sendDisableRequest: true);
            }

            state.TextInputSession = null;
            state.TextInputChangeCause = TextInputChangeCauseOther;
            return PlatformTextInputCapabilities.None;
        }

        if ((session.SessionId <= 0) || (session.Window != window))
        {
            throw new ArgumentException("The text input snapshot must name its exact session and window.", nameof(snapshot));
        }

        if ((state.TextInputSession is TextInputSessionSnapshot existingSession)
            && (existingSession.SessionId == session.SessionId)
            && ReferenceEquals(existingSession.Options, session.Options))
        {
            TryEnableTextInput(state);
            return GetTextInputCapabilities();
        }

        if (state.TextInputSession is TextInputSessionSnapshot previous
            && (previous.SessionId != session.SessionId)
            && (_textInputEnabledWindow == state))
        {
            DeactivateTextInput(state, TextCompositionCancelReason.SessionReplaced, sendDisableRequest: true);
        }
        else if ((state.TextInputSession is TextInputSessionSnapshot priorSnapshot)
            && (_textInputEnabledWindow == state)
            && (WaylandTextInputText.CreateSurroundingText(
                    WaylandTextInputText.GetNativeSurroundingText(priorSnapshot.Options),
                    priorSnapshot.Options.SelectionStart,
                    priorSnapshot.Options.SelectionLength) is not null)
            && (WaylandTextInputText.CreateSurroundingText(
                    WaylandTextInputText.GetNativeSurroundingText(session.Options),
                    session.Options.SelectionStart,
                    session.Options.SelectionLength) is null))
        {
            DeactivateTextInput(state, TextCompositionCancelReason.NativeCanceled, sendDisableRequest: true);
        }

        state.TextInputChangeCause = ((_textInputExpectedDocumentSessionId == session.SessionId)
            && (_textInputExpectedDocumentRevision == session.Options.DocumentRevision))
                ? TextInputChangeCauseInputMethod
                : TextInputChangeCauseOther;
        _textInputExpectedDocumentSessionId = 0;
        _textInputExpectedDocumentRevision = 0;

        state.TextInputSession = session;
        _textInputStateDirty = true;
        TryEnableTextInput(state);

        return GetTextInputCapabilities();
    }

    private PlatformTextInputCapabilities GetTextInputCapabilities()
    {
        if (_textInput != 0)
        {
            return PlatformTextInputCapabilities.CommittedText | PlatformTextInputCapabilities.Composition;
        }

        return (_keyboardKeymap is null)
            ? PlatformTextInputCapabilities.None
            : PlatformTextInputCapabilities.CommittedText;
    }

    private void BindTextInput()
    {
        if ((_textInputManager == 0) || (_seat == 0) || (_textInput != 0))
        {
            return;
        }

        var arguments = stackalloc WaylandArgument[2];
        arguments[0] = default;
        arguments[1].Pointer = _seat;
        var textInput = Connection.CreateProxy(
            _textInputManager,
            opcode: 1,
            ProtocolMetadata.ZwpTextInputV3Interface,
            version: 1,
            arguments);

        if (textInput == 0)
        {
            throw new IOException("The Wayland seat could not create a text-input-v3 object.");
        }

        var listener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnTextInputEnter,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnTextInputLeave,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int, int, void>)&OnTextInputPreeditString,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnTextInputCommitString,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, uint, void>)&OnTextInputDeleteSurroundingText,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, void>)&OnTextInputDone);

        if (Connection.AddListener(textInput, listener, GCHandle.ToIntPtr(_callbackHandle)) < 0)
        {
            try
            {
                Connection.SendRequest(textInput, opcode: 0);
            }
            finally
            {
                Connection.DestroyProxy(textInput);
            }

            throw new IOException("The Wayland text-input-v3 listener could not be installed.");
        }

        _textInput = textInput;
        _textInputCommitSerial = 0;
        _textInputCommits.Clear();
        _textInputStateDirty = false;
        _textInputStateRequiresResync = false;
        _textInputFocusedWindow = null;
        _textInputEnabledWindow = null;
        _textInputEnabledSessionId = 0;
        _textInputCompositionActive = false;
        _textInputSurroundingText = null;
        ClearPendingTextInputEvents();
    }

    private void RefreshTextInputManager()
    {
        var globals = Connection.GetGlobals("zwp_text_input_manager_v3");
        WaylandGlobal? selectedGlobal = null;

        foreach (var global in globals)
        {
            if ((selectedGlobal is null)
                || (global.Name == _textInputManagerGlobalName)
                || (selectedGlobal.Value.Name != _textInputManagerGlobalName
                    && (global.Version > selectedGlobal.Value.Version)))
            {
                selectedGlobal = global;
            }
        }

        if ((_textInputManager != 0)
            && ((selectedGlobal is null) || (selectedGlobal.Value.Name != _textInputManagerGlobalName)))
        {
            var failures = new List<Exception>();
            RetireTextInputManager(failures);

            if (failures.Count > 0)
            {
                throw new AggregateException("The Wayland text-input manager could not be retired.", failures);
            }
        }

        if ((_textInputManager == 0) && (selectedGlobal is WaylandGlobal globalToBind))
        {
            _textInputManager = Connection.BindGlobal(
                globalToBind,
                ProtocolMetadata.ZwpTextInputManagerV3Interface,
                version: 1);
            _textInputManagerGlobalName = globalToBind.Name;
            BindTextInput();
        }
        else if ((_textInputManager != 0) && (_seat != 0) && (_textInput == 0))
        {
            BindTextInput();
        }
    }

    private void TryEnableTextInput(WaylandWindowState window)
    {
        if ((_textInput == 0)
            || (_textInputFocusedWindow != window)
            || (window.TextInputSession is not TextInputSessionSnapshot snapshot))
        {
            return;
        }

        if ((_textInputEnabledWindow == window)
            && (_textInputEnabledSessionId == snapshot.SessionId))
        {
            if (_textInputStateDirty && !_textInputStateRequiresResync)
            {
                SendTextInputState(snapshot, window.TextInputChangeCause);
                CommitTextInputState();
                _textInputStateDirty = false;
            }

            return;
        }

        if (_textInputEnabledWindow is WaylandWindowState previousWindow)
        {
            DeactivateTextInput(previousWindow, TextCompositionCancelReason.SessionReplaced, sendDisableRequest: true);
        }

        _textInputStateRequiresResync = false;
        Connection.SendRequest(_textInput, opcode: 1);
        _textInputEnabledWindow = window;
        _textInputEnabledSessionId = snapshot.SessionId;
        _textInputCompositionActive = false;
        _textInputStateDirty = true;
        SendTextInputState(snapshot, window.TextInputChangeCause);
        CommitTextInputState();
        _textInputStateDirty = false;
    }

    private void FlushTextInputState()
    {
        if (_textInputFocusedWindow is WaylandWindowState window)
        {
            TryEnableTextInput(window);
        }
    }

    private void SendTextInputState(TextInputSessionSnapshot snapshot, uint changeCause)
    {
        var options = snapshot.Options;
        var (hint, purpose) = GetTextInputContentType(options);
        var contentTypeArguments = stackalloc WaylandArgument[2];
        contentTypeArguments[0].Unsigned = hint;
        contentTypeArguments[1].Unsigned = purpose;
        Connection.SendRequest(_textInput, opcode: 5, contentTypeArguments);
        var causeArguments = stackalloc WaylandArgument[1];
        causeArguments[0].Unsigned = changeCause;
        Connection.SendRequest(_textInput, opcode: 4, causeArguments);

        _textInputSurroundingText = WaylandTextInputText.CreateSurroundingText(
            WaylandTextInputText.GetNativeSurroundingText(options),
            options.SelectionStart,
            options.SelectionLength);

        if (_textInputSurroundingText is WaylandTextInputSurroundingText surroundingText)
        {
            var nativeText = Marshal.StringToCoTaskMemUTF8(surroundingText.Text);

            try
            {
                var surroundingArguments = stackalloc WaylandArgument[3];
                surroundingArguments[0].Pointer = nativeText;
                surroundingArguments[1].FileDescriptor = surroundingText.CursorByteOffset;
                surroundingArguments[2].FileDescriptor = surroundingText.AnchorByteOffset;
                Connection.SendRequest(_textInput, opcode: 3, surroundingArguments);
            }
            finally
            {
                Marshal.FreeCoTaskMem(nativeText);
            }
        }

        var caretArguments = stackalloc WaylandArgument[4];
        caretArguments[0].FileDescriptor = ToWaylandCoordinate(options.CaretPosition.X, nameof(options.CaretPosition));
        caretArguments[1].FileDescriptor = ToWaylandCoordinate(options.CaretPosition.Y, nameof(options.CaretPosition));
        caretArguments[2].FileDescriptor = ToWaylandExtent(options.CaretSize.X, nameof(options.CaretSize));
        caretArguments[3].FileDescriptor = ToWaylandExtent(options.CaretSize.Y, nameof(options.CaretSize));
        Connection.SendRequest(_textInput, opcode: 6, caretArguments);
    }

    private void CommitTextInputState()
    {
        if (_textInputCommits.Count == PlatformInputSampleQueue.DefaultCapacity)
        {
            throw new InvalidOperationException("The Wayland text-input protocol exceeded its bounded unacknowledged-commit capacity.");
        }

        var nextSerial = checked(_textInputCommitSerial + 1);
        Connection.SendRequest(_textInput, opcode: 7);
        _textInputCommitSerial = nextSerial;

        if ((_textInputEnabledWindow is WaylandWindowState window)
            && (window.TextInputSession is TextInputSessionSnapshot snapshot)
            && (snapshot.SessionId == _textInputEnabledSessionId))
        {
            _textInputCommits.Add(
                nextSerial,
                new WaylandTextInputCommitState(
                    window.Window,
                    snapshot.SessionId,
                    snapshot.Options.DocumentRevision,
                    WaylandTextInputText.GetNativeSurroundingText(snapshot.Options)?.Length ?? 0,
                    _textInputSurroundingText));
        }
    }

    private void HandleTextInputEnter(nint textInput, nint surface)
    {
        if (textInput != _textInput)
        {
            return;
        }

        if (!_windowsBySurface.TryGetValue(surface, out var window))
        {
            if (_textInputFocusedWindow is WaylandWindowState unfocusedWindow)
            {
                DeactivateTextInput(unfocusedWindow, TextCompositionCancelReason.FocusLost, sendDisableRequest: false);
            }

            _textInputFocusedWindow = null;
            return;
        }

        if ((_textInputFocusedWindow is WaylandWindowState previousWindow)
            && (previousWindow != window))
        {
            DeactivateTextInput(previousWindow, TextCompositionCancelReason.FocusLost, sendDisableRequest: false);
        }

        _textInputFocusedWindow = window;
        _textInputStateDirty = true;
    }

    private void HandleTextInputLeave(nint textInput, nint surface)
    {
        if ((textInput != _textInput)
            || (_textInputFocusedWindow is not WaylandWindowState window)
            || (window.Surface != surface))
        {
            return;
        }

        DeactivateTextInput(window, TextCompositionCancelReason.FocusLost, sendDisableRequest: false);
        _textInputFocusedWindow = null;
    }

    private void HandleTextInputPreeditString(nint textInput, nint text, int cursorBegin, int cursorEnd)
    {
        if (textInput != _textInput)
        {
            return;
        }

        _pendingTextInputPreedit = ReadTextInputString(text, allowNull: true);
        _pendingTextInputPreeditCursorBegin = cursorBegin;
        _pendingTextInputPreeditCursorEnd = cursorEnd;
        _hasPendingTextInputPreedit = true;
    }

    private void HandleTextInputCommitString(nint textInput, nint text)
    {
        if (textInput != _textInput)
        {
            return;
        }

        _pendingTextInputCommit = ReadTextInputString(text, allowNull: true);
        _hasPendingTextInputCommit = true;
    }

    private void HandleTextInputDeleteSurroundingText(nint textInput, uint beforeLength, uint afterLength)
    {
        if (textInput != _textInput)
        {
            return;
        }

        _pendingTextInputDeleteBefore = beforeLength;
        _pendingTextInputDeleteAfter = afterLength;
        _hasPendingTextInputDelete = true;
    }

    private void HandleTextInputDone(nint textInput, uint serial)
    {
        if (textInput != _textInput)
        {
            return;
        }

        if (!_textInputCommits.TryGetValue(serial, out var commitState))
        {
            if ((_textInputEnabledWindow is WaylandWindowState activeWindow)
                && (activeWindow.TextInputSession is TextInputSessionSnapshot activeSnapshot))
            {
                _ = _inputSamples.TryEnqueueTextInput(new PlatformTextInputSample(
                    activeWindow.Window,
                    [CreateRejectedTextEdit("The Wayland compositor returned an unknown text-input commit serial.")],
                    _timeProvider.GetTimestamp(),
                    SessionId: activeSnapshot.SessionId,
                    BaseDocumentRevision: activeSnapshot.Options.DocumentRevision));
            }

            _textInputStateRequiresResync = _textInputEnabledWindow is not null;
            ClearPendingTextInputEvents();
            return;
        }

        if ((_textInputEnabledWindow is null)
            || (commitState.SessionId != _textInputEnabledSessionId))
        {
            RemoveTextInputCommitStatesThrough(serial);
            ClearPendingTextInputEvents();
            return;
        }

        UpdateTextInputStateSync(serial);

        var changes = ImmutableArray.CreateBuilder<TextInputChange>(5);

        if (_hasPendingTextInputDelete
            && ((_pendingTextInputDeleteBefore != 0) || (_pendingTextInputDeleteAfter != 0)))
        {
            if ((commitState.SurroundingText is WaylandTextInputSurroundingText surroundingText)
                && WaylandTextInputText.TryMapDelete(
                    commitState.SnapshotTextLength,
                    surroundingText,
                    _pendingTextInputDeleteBefore,
                    _pendingTextInputDeleteAfter,
                    out var editStart,
                    out var deleteLength))
            {
                changes.Add(new TextInputChange(
                    TextInputChangeKind.TextEditRequested,
                    editStart: editStart,
                    deleteLength: deleteLength));
            }
            else
            {
                changes.Add(CreateRejectedTextEdit(
                    "The Wayland input method requested a deletion outside the supplied surrounding-text snapshot."));
            }
        }

        var compositionChanges = WaylandTextInputText.CreateCompositionChanges(
            _textInputCompositionActive,
            _hasPendingTextInputCommit ? _pendingTextInputCommit : null,
            _hasPendingTextInputPreedit,
            _pendingTextInputPreedit,
            _pendingTextInputPreeditCursorBegin,
            _pendingTextInputPreeditCursorEnd,
            out _textInputCompositionActive);

        changes.AddRange(compositionChanges);

        if (changes.Count > 0)
        {
            var changeArray = changes.ToImmutable();
            var published = _inputSamples.TryEnqueueTextInput(new PlatformTextInputSample(
                commitState.Window,
                changeArray,
                _timeProvider.GetTimestamp(),
                SessionId: commitState.SessionId,
                BaseDocumentRevision: commitState.DocumentRevision));

            if (published
                && (commitState.DocumentRevision < long.MaxValue)
                && changeArray.Any(static change => change.Kind is TextInputChangeKind.TextEditRequested or TextInputChangeKind.TextCommitted))
            {
                _textInputExpectedDocumentSessionId = commitState.SessionId;
                _textInputExpectedDocumentRevision = checked(commitState.DocumentRevision + 1);
            }
        }

        RemoveTextInputCommitStatesThrough(serial);

        ClearPendingTextInputEvents();
    }

    private void UpdateTextInputStateSync(uint serial)
    {
        if (serial != _textInputCommitSerial)
        {
            _textInputStateRequiresResync = true;
        }
        else if (_textInputStateRequiresResync)
        {
            _textInputStateRequiresResync = false;
            _textInputStateDirty = true;
        }
    }

    private void RemoveTextInputCommitStatesThrough(uint serial)
    {
        while (_textInputCommits.Count > 0)
        {
            var enumerator = _textInputCommits.GetEnumerator();

            if (!enumerator.MoveNext() || (enumerator.Current.Key >= serial))
            {
                return;
            }

            _ = _textInputCommits.Remove(enumerator.Current.Key);
        }
    }

    private void DeactivateTextInput(
        WaylandWindowState window,
        TextCompositionCancelReason cancellationReason,
        bool sendDisableRequest)
    {
        if (_textInputEnabledWindow != window)
        {
            return;
        }

        if (sendDisableRequest && (_textInput != 0) && (_textInputFocusedWindow == window))
        {
            Connection.SendRequest(_textInput, opcode: 2);
            CommitTextInputState();
        }

        if (_textInputCompositionActive && (window.TextInputSession is TextInputSessionSnapshot snapshot))
        {
            _ = _inputSamples.TryEnqueueTextInput(new PlatformTextInputSample(
                window.Window,
                [new TextInputChange(
                    TextInputChangeKind.CompositionCanceled,
                    cancellationReason: cancellationReason)],
                _timeProvider.GetTimestamp(),
                SessionId: snapshot.SessionId,
                BaseDocumentRevision: snapshot.Options.DocumentRevision));
        }

        _textInputEnabledWindow = null;
        _textInputEnabledSessionId = 0;
        _textInputStateDirty = false;
        _textInputCompositionActive = false;
        _textInputSurroundingText = null;
        ClearPendingTextInputEvents();
    }

    private void RetireTextInputWindow(WaylandWindowState window, bool sendDestroyRequests)
    {
        if (_textInputEnabledWindow == window)
        {
            DeactivateTextInput(window, TextCompositionCancelReason.SessionDisposed, sendDestroyRequests);
        }

        if (_textInputFocusedWindow == window)
        {
            _textInputFocusedWindow = null;
        }

        window.TextInputSession = null;
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Text-input proxy retirement attempts every native teardown operation and aggregates failures for owner shutdown.")]
    private void RetireTextInput(List<Exception> failures)
    {
        if (_textInputEnabledWindow is WaylandWindowState enabledWindow)
        {
            try
            {
                DeactivateTextInput(enabledWindow, TextCompositionCancelReason.FocusLost, sendDisableRequest: true);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        var textInput = _textInput;
        _textInput = 0;
        _textInputFocusedWindow = null;
        _textInputEnabledWindow = null;
        _textInputEnabledSessionId = 0;
        _textInputStateDirty = false;
        _textInputStateRequiresResync = false;
        _textInputCompositionActive = false;
        _textInputCommits.Clear();
        ClearPendingTextInputEvents();
        _textInputSurroundingText = null;

        if (textInput != 0)
        {
            try
            {
                Connection.SendRequest(textInput, opcode: 0);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            try
            {
                Connection.DestroyProxy(textInput);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Text-input manager retirement attempts each native teardown operation and aggregates failures for owner shutdown.")]
    private void RetireTextInputManager(List<Exception> failures)
    {
        RetireTextInput(failures);
        var manager = _textInputManager;
        _textInputManager = 0;
        _textInputManagerGlobalName = 0;

        if (manager == 0)
        {
            return;
        }

        try
        {
            Connection.SendRequest(manager, opcode: 0);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            Connection.DestroyProxy(manager);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static int ToWaylandCoordinate(float value, string parameterName)
    {
        var rounded = Math.Round((double)value, MidpointRounding.ToEven);

        if (rounded is < int.MinValue or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The Wayland text-input caret coordinate exceeds the protocol range.");
        }

        return checked((int)rounded);
    }

    private static int ToWaylandExtent(float value, string parameterName)
    {
        var rounded = Math.Round((double)value, MidpointRounding.ToEven);

        if (rounded is < 0.0 or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The Wayland text-input caret extent exceeds the protocol range.");
        }

        return checked((int)rounded);
    }

    internal static (uint Hint, uint Purpose) GetTextInputContentType(TextInputSessionOptions options)
    {
        var hint = 0u;
        var isProtected = (options.Purpose is TextInputPurpose.Password)
            || ((options.Hints & TextInputHints.Sensitive) is TextInputHints.Sensitive);
        var purpose = options.Purpose switch {
            TextInputPurpose.Email => TextInputContentPurposeEmail,
            TextInputPurpose.Number => TextInputContentPurposeNumber,
            TextInputPurpose.Password => TextInputContentPurposePassword,
            TextInputPurpose.Telephone => TextInputContentPurposePhone,
            TextInputPurpose.Url => TextInputContentPurposeUrl,
            TextInputPurpose.Search => TextInputContentPurposeNormal,
            TextInputPurpose.FreeForm => TextInputContentPurposeNormal,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        if (!isProtected && ((options.Hints & TextInputHints.SpellCheck) is TextInputHints.SpellCheck))
        {
            hint |= TextInputContentHintSpellcheck;
        }

        if (!isProtected && ((options.Hints & TextInputHints.AutoCorrect) is TextInputHints.AutoCorrect))
        {
            hint |= TextInputContentHintSpellcheck;
        }

        if ((options.Hints & TextInputHints.Multiline) is TextInputHints.Multiline)
        {
            hint |= TextInputContentHintMultiline;
        }

        if (isProtected)
        {
            hint |= TextInputContentHintHiddenText | TextInputContentHintSensitiveData;
        }

        return (hint, purpose);
    }

    private static TextInputChange CreateRejectedTextEdit(string message)
    {
        var diagnostic = new Diagnostic(
            "TextInput.TextEditRejected",
            DiagnosticSeverity.Error,
            Stopwatch.GetTimestamp(),
            hostId: null,
            worldGuid: null,
            deviceId: null,
            deviceEpoch: null,
            surfaceId: null,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: null,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation: "WaylandTextInputV3",
            nativeResult: null,
            message: message,
            context: []);
        return new TextInputChange(TextInputChangeKind.TextEditRejected, diagnostic: diagnostic);
    }

    private static string? ReadTextInputString(nint value, bool allowNull)
    {
        if (value == 0)
        {
            return allowNull
                ? null
                : throw new InvalidOperationException("The Wayland text-input event omitted a required string.");
        }

        var bytes = (byte*)value;
        var byteCount = 0;

        while ((byteCount <= 4096) && (bytes[byteCount] != 0))
        {
            byteCount++;
        }

        if (byteCount > 4096)
        {
            throw new InvalidOperationException("The Wayland text-input string exceeds its bounded payload.");
        }

        return WaylandTextInputText.DecodeUtf8(new ReadOnlySpan<byte>(bytes, byteCount));
    }

    private void ClearPendingTextInputEvents()
    {
        _pendingTextInputPreedit = null;
        _hasPendingTextInputPreedit = false;
        _pendingTextInputPreeditCursorBegin = 0;
        _pendingTextInputPreeditCursorEnd = 0;
        _pendingTextInputCommit = null;
        _hasPendingTextInputCommit = false;
        _pendingTextInputDeleteBefore = 0;
        _pendingTextInputDeleteAfter = 0;
        _hasPendingTextInputDelete = false;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnTextInputEnter(nint data, nint textInput, nint surface)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleTextInputEnter(textInput, surface);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnTextInputLeave(nint data, nint textInput, nint surface)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleTextInputLeave(textInput, surface);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnTextInputPreeditString(
        nint data,
        nint textInput,
        nint text,
        int cursorBegin,
        int cursorEnd)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleTextInputPreeditString(textInput, text, cursorBegin, cursorEnd);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnTextInputCommitString(nint data, nint textInput, nint text)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleTextInputCommitString(textInput, text);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnTextInputDeleteSurroundingText(
        nint data,
        nint textInput,
        uint beforeLength,
        uint afterLength)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleTextInputDeleteSurroundingText(textInput, beforeLength, afterLength);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnTextInputDone(nint data, nint textInput, uint serial)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleTextInputDone(textInput, serial);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }
    }
}
