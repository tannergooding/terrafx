// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using TerraFX;
using TerraFX.Input;
using TerraFX.Interop.Windows;

namespace TerraFX.Platform;

[SupportedOSPlatform("windows10.0.22000")]
internal sealed unsafe class WindowsTextInputManager : IDisposable
{
    private const uint WindowMessageKeyDown = 0x0100;
    private const uint WindowMessageKeyUp = 0x0101;
    private const uint WindowMessageSystemKeyDown = 0x0104;
    private const uint WindowMessageSystemKeyUp = 0x0105;

    [SuppressMessage(
        "Style",
        "IDE0044:Make field readonly",
        Justification = "ITfThreadMgr.Activate writes the client ID during owner-thread initialization.")]
    private uint _clientId;
    private WindowHandle? _focusedWindow;
    private readonly PlatformInputSampleQueue _inputSamples;
    private bool _isActivated;
    private bool _isDisposed;
    private ITfKeystrokeMgr* _keystrokeManager;
    private readonly int _ownerThreadId;
    private readonly Dictionary<WindowHandle, TextStoreContext> _sessions = [];
    private ITfThreadMgr* _threadManager;
    private readonly TimeProvider _timeProvider;

    internal WindowsTextInputManager(
        PlatformInputSampleQueue inputSamples,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inputSamples);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _inputSamples = inputSamples;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _timeProvider = timeProvider;
        Initialize();
    }

    private static InputScope GetInputScope(WindowsTextInputSessionState state)
    {
        if (state.Purpose is TextInputPurpose.Password)
        {
            return InputScope.IS_PASSWORD;
        }

        if (state.IsProtected)
        {
            return InputScope.IS_PRIVATE;
        }

        return state.Purpose switch {
            TextInputPurpose.Email => InputScope.IS_EMAIL_SMTPEMAILADDRESS,
            TextInputPurpose.Number => InputScope.IS_NUMBER,
            TextInputPurpose.Search => InputScope.IS_SEARCH,
            TextInputPurpose.Telephone => InputScope.IS_TELEPHONE_FULLTELEPHONENUMBER,
            TextInputPurpose.Url => InputScope.IS_URL,
            TextInputPurpose.FreeForm when (state.Hints & TextInputHints.Multiline) != 0 => InputScope.IS_TEXT,
            TextInputPurpose.FreeForm => InputScope.IS_DEFAULT,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    private static void ThrowIfFailed(HRESULT result, string operation)
    {
        if (result.Value < 0)
        {
            throw new WindowsTextInputInteropException(operation, result.Value);
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Host shutdown attempts all TSF context and thread-manager releases before reporting failures.")]
    public void Dispose()
    {
        EnsureOwnerThread();

        if (_isDisposed)
        {
            return;
        }

        var failures = new List<Exception>();

        foreach (var window in new List<WindowHandle>(_sessions.Keys))
        {
            try
            {
                RemoveSession(window);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (_focusedWindow is not null)
        {
            try
            {
                SetNativeFocus(documentManager: null);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            _focusedWindow = null;
        }

        if (_isActivated && (_threadManager is not null))
        {
            try
            {
                ThrowIfFailed(_threadManager->Deactivate(), "thread-manager deactivation");
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            _isActivated = false;
        }

        if (_keystrokeManager is not null)
        {
            _ = _keystrokeManager->Release();
            _keystrokeManager = null;
        }

        if (_threadManager is not null)
        {
            _ = _threadManager->Release();
            _threadManager = null;
        }

        _isDisposed = true;

        if (failures.Count > 0)
        {
            throw new AggregateException("Windows TSF shutdown failed.", failures);
        }
    }

    internal void FlushPendingTextInput()
    {
        EnsureOwnerThread();

        foreach (var session in _sessions.Values)
        {
            var result = session.Store.GrantPendingLock();

            if (result < 0)
            {
                throw new WindowsTextInputInteropException("asynchronous text-store lock", result);
            }

            session.Store.FlushPendingTextInput();
        }
    }

    internal bool ProcessKeyMessage(nint nativeWindow, uint message, nuint wParam, nint lParam)
    {
        EnsureOwnerThread();

        if ((_keystrokeManager is null)
            || (_focusedWindow is not { } focusedWindow)
            || !_sessions.TryGetValue(focusedWindow, out var session)
            || (session.NativeWindow != nativeWindow))
        {
            return false;
        }

        var eaten = default(BOOL);
        var result = default(HRESULT);

        switch (message)
        {
            case WindowMessageKeyDown:
            case WindowMessageSystemKeyDown:
            {
                result = _keystrokeManager->TestKeyDown(wParam, lParam, &eaten);
                ThrowIfFailed(result, "TestKeyDown");

                if (eaten.Value == 0)
                {
                    return false;
                }

                result = _keystrokeManager->KeyDown(wParam, lParam, &eaten);
                ThrowIfFailed(result, "KeyDown");
                break;
            }

            case WindowMessageKeyUp:
            case WindowMessageSystemKeyUp:
            {
                result = _keystrokeManager->TestKeyUp(wParam, lParam, &eaten);
                ThrowIfFailed(result, "TestKeyUp");

                if (eaten.Value == 0)
                {
                    return false;
                }

                result = _keystrokeManager->KeyUp(wParam, lParam, &eaten);
                ThrowIfFailed(result, "KeyUp");
                break;
            }

            default:
            {
                return false;
            }
        }

        FlushPendingTextInput();
        return eaten.Value != 0;
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Session retirement attempts context, manager, and text-store cleanup before reporting failures.")]
    internal void RemoveSession(WindowHandle window)
    {
        EnsureOwnerThread();

        if (!_sessions.Remove(window, out var session))
        {
            return;
        }

        var failures = new List<Exception>();

        if (_focusedWindow == window)
        {
            try
            {
                SetNativeFocus(documentManager: null);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            _focusedWindow = null;
        }

        try
        {
            ThrowIfFailed(
                Windows.SetInputScope((HWND)session.NativeWindow, InputScope.IS_DEFAULT),
                "input-scope reset");
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        session.Dispose(failures);

        if (failures.Count > 0)
        {
            throw new AggregateException("A Windows TSF document context could not be retired.", failures);
        }
    }

    internal void SetWindowFocus(WindowHandle window, bool isFocused)
    {
        EnsureOwnerThread();

        if (!isFocused)
        {
            if ((_focusedWindow == window) && _sessions.TryGetValue(window, out var previousSession))
            {
                previousSession.Store.CancelComposition(TextCompositionCancelReason.FocusLost);
                previousSession.Store.FlushPendingTextInput();
                SetNativeFocus(documentManager: null);
                _focusedWindow = null;
            }

            return;
        }

        if (!_sessions.TryGetValue(window, out var session))
        {
            return;
        }

        if (_focusedWindow is { } currentWindow
            && (currentWindow != window)
            && _sessions.TryGetValue(currentWindow, out var currentSession))
        {
            currentSession.Store.CancelComposition(TextCompositionCancelReason.FocusLost);
            currentSession.Store.FlushPendingTextInput();
        }

        SetNativeFocus(session.DocumentManager);
        _focusedWindow = window;
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Failed TSF context creation releases every partially created COM object before rethrowing.")]
    internal void UpdateSession(
        WindowHandle window,
        nint nativeWindow,
        WindowsTextInputSessionState state,
        bool isFocused)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(state);

        if (_sessions.TryGetValue(window, out var existingSession)
            && (existingSession.Store.SessionId == state.SessionId))
        {
            if (existingSession.Store.DocumentRevision != state.DocumentRevision)
            {
                existingSession.Store.UpdateSnapshot(state);
            }

            existingSession.NativeWindow = nativeWindow;

            if (isFocused != (_focusedWindow == window))
            {
                SetWindowFocus(window, isFocused);
            }

            return;
        }

        RemoveSession(window);
        var store = new WindowsTextInputStore(window, nativeWindow, state, _inputSamples, _timeProvider);
        var documentManager = (ITfDocumentMgr*)0;
        var context = (ITfContext*)0;
        var contextPushed = false;
        var inputScopeSet = false;
        TextStoreContext? session = null;

        try
        {
            ThrowIfFailed(
                Windows.SetInputScope((HWND)nativeWindow, GetInputScope(state)),
                "input-scope update");
            inputScopeSet = true;
            ThrowIfFailed(_threadManager->CreateDocumentMgr(&documentManager), "document-manager creation");
            var editCookie = 0u;
            ThrowIfFailed(
                documentManager->CreateContext(
                    _clientId,
                    0,
                    (IUnknown*)store.GetIUnknownPointer(),
                    &context,
                    &editCookie),
                "document-context creation");
            ThrowIfFailed(documentManager->Push(context), "document-context activation");
            contextPushed = true;
            session = new TextStoreContext(documentManager, context, store, nativeWindow, state.SessionId);
            _sessions.Add(window, session);
            documentManager = null;
            context = null;
            contextPushed = false;

            if (isFocused)
            {
                SetWindowFocus(window, isFocused: true);
            }
        }
        catch (Exception exception)
        {
            var cleanupFailures = new List<Exception>();

            if (session is not null)
            {
                _ = _sessions.Remove(window);
                session.Dispose(cleanupFailures);
                documentManager = null;
                context = null;
                contextPushed = false;
            }

            if (contextPushed && (documentManager != null))
            {
                try
                {
                    _ = documentManager->Pop((uint)TF.TF_POPF_ALL);
                }
                catch (Exception cleanupException)
                {
                    cleanupFailures.Add(cleanupException);
                }
            }

            if (context != null)
            {
                _ = context->Release();
            }

            if (documentManager != null)
            {
                _ = documentManager->Release();
            }

            if (inputScopeSet)
            {
                try
                {
                    ThrowIfFailed(
                        Windows.SetInputScope((HWND)nativeWindow, InputScope.IS_DEFAULT),
                        "input-scope rollback");
                }
                catch (Exception cleanupException)
                {
                    cleanupFailures.Add(cleanupException);
                }
            }

            store.Dispose();

            if (cleanupFailures.Count > 0)
            {
                cleanupFailures.Insert(0, exception);
                throw new AggregateException("Windows TSF session creation and cleanup both failed.", cleanupFailures);
            }

            throw;
        }
    }

    private void EnsureOwnerThread()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Windows TSF calls are restricted to the Platform owner STA thread.");
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Failed TSF initialization deactivates and releases every partially acquired COM object.")]
    private void Initialize()
    {
        ITfThreadMgr* threadManager = null;

        try
        {
            ThrowIfFailed(TF.TF_CreateThreadMgr(&threadManager), "thread-manager creation");
            _threadManager = threadManager;
            var clientId = 0u;
            ThrowIfFailed(_threadManager->Activate(&clientId), "thread-manager activation");
            _clientId = clientId;
            _isActivated = true;
            var iid = IID.IID_ITfKeystrokeMgr;
            void* keyManager = null;
            var result = ((IUnknown*)_threadManager)->QueryInterface(&iid, &keyManager);
            ThrowIfFailed(result, "keystroke-manager query");
            _keystrokeManager = (ITfKeystrokeMgr*)keyManager;
        }
        catch (Exception exception)
        {
            var cleanupFailures = new List<Exception>();

            if (_isActivated && (_threadManager is not null))
            {
                try
                {
                    ThrowIfFailed(_threadManager->Deactivate(), "thread-manager deactivation");
                }
                catch (Exception cleanupException)
                {
                    cleanupFailures.Add(cleanupException);
                }
            }

            if (_keystrokeManager is not null)
            {
                _ = _keystrokeManager->Release();
                _keystrokeManager = null;
            }

            if (_threadManager is not null)
            {
                _ = _threadManager->Release();
                _threadManager = null;
            }

            if (cleanupFailures.Count > 0)
            {
                cleanupFailures.Insert(0, exception);
                throw new AggregateException("Windows TSF initialization and cleanup both failed.", cleanupFailures);
            }

            throw;
        }
    }

    private void SetNativeFocus(ITfDocumentMgr* documentManager)
        => ThrowIfFailed(
            _threadManager->SetFocus(documentManager),
            "document-manager focus update");

    private sealed unsafe class TextStoreContext
    {
        private readonly ITfContext* _context;
        private readonly ITfDocumentMgr* _documentManager;
        private readonly WindowsTextInputStore _store;

        internal TextStoreContext(
            ITfDocumentMgr* documentManager,
            ITfContext* context,
            WindowsTextInputStore store,
            nint nativeWindow,
            long sessionId)
        {
            _context = context;
            _documentManager = documentManager;
            _store = store;
            NativeWindow = nativeWindow;
            SessionId = sessionId;
        }

        internal ITfDocumentMgr* DocumentManager => _documentManager;

        internal nint NativeWindow { get; set; }

        internal long SessionId { get; }

        internal WindowsTextInputStore Store => _store;

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "Context retirement continues releasing all native owners after an individual COM release fails.")]
        internal void Dispose(List<Exception> failures)
        {
            _store.CancelComposition(TextCompositionCancelReason.SessionDisposed);

            try
            {
                ThrowIfFailed(_documentManager->Pop((uint)TF.TF_POPF_ALL), "document-context pop");
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            _ = _context->Release();
            _ = _documentManager->Release();

            try
            {
                _store.Dispose();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }
}
