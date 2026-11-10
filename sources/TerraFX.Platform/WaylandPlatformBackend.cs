// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Platform;

[SuppressMessage(
    "Reliability",
    "CA2216:Disposable types should declare finalizer",
    Justification = "Wayland proxies and callbacks are confined to the platform owner thread and are retired through Dispose.")]
internal sealed unsafe partial class WaylandPlatformBackend : IPlatformBackend
{
    private const int MaximumWindowCount = 32;
    private const uint MaximumWlOutputVersion = 3;
    private const uint XdgToplevelStateFullscreen = 2;
    private const uint XdgWmBaseVersion = 1;
    private Exception? _callbackFailure;
    private GCHandle _callbackHandle;
    private readonly ClipboardServicePolicy _clipboardPolicy;
    private volatile ClipboardServiceStatus _clipboardStatus;
    private readonly List<WindowHandle> _closeRequests;
    private nint _compositor;
    private WaylandDisplayConnection? _connection;
    private LinuxEvdevGamepadCapture? _gamepadCapture;
    private PlatformServiceStatus _gamepadStatus;
    private bool _hasPendingTextInputCommit;
    private bool _hasPendingTextInputDelete;
    private bool _hasPendingTextInputPreedit;
    private readonly PlatformInputSampleQueue _inputSamples;
    private bool _isDisposed;
    private nint _keyboard;
    private uint _keyboardDepressedModifiers;
    private WaylandWindowState? _keyboardFocusedWindow;
    private uint _keyboardGroup;
    private WaylandKeyboardKeymap? _keyboardKeymap;
    private readonly HashSet<uint> _keyboardKeysDown;
    private uint _keyboardLatchedModifiers;
    private nint _keyboardListener;
    private uint _keyboardLockedModifiers;
    private readonly WaylandKeyboardRepeatSchedule _keyboardRepeatSchedule;
    private readonly List<nint> _listenerTables;
    private ImmutableArray<MonitorHandle> _monitorHandles;
    private bool _monitorTopologyChanged;
    private nint _outputListener;
    private readonly SortedDictionary<uint, WaylandOutputState> _outputsByGlobalName;
    private readonly Dictionary<nint, WaylandOutputState> _outputsByProxy;
    private readonly uint _ownerId;
    private readonly int _ownerThreadId;
    private string? _pendingTextInputCommit;
    private uint _pendingTextInputDeleteAfter;
    private uint _pendingTextInputDeleteBefore;
    private string? _pendingTextInputPreedit;
    private int _pendingTextInputPreeditCursorBegin;
    private int _pendingTextInputPreeditCursorEnd;
    private nint _pointer;
    private readonly HashSet<uint> _pointerButtonsDown;
    private WaylandWindowState? _pointerFocusedWindow;
    private nint _pointerListener;
    private WaylandProtocolMetadata? _protocolMetadata;
    private nint _seat;
    private uint _seatGlobalName;
    private nint _seatListener;
    private int _seatVersion;
    private nint _surfaceListener;
    private nint _textInput;
    private uint _textInputCommitSerial;
    private readonly SortedDictionary<uint, WaylandTextInputCommitState> _textInputCommits;
    private bool _textInputCompositionActive;
    private long _textInputEnabledSessionId;
    private WaylandWindowState? _textInputEnabledWindow;
    private long _textInputExpectedDocumentRevision;
    private long _textInputExpectedDocumentSessionId;
    private WaylandWindowState? _textInputFocusedWindow;
    private nint _textInputManager;
    private uint _textInputManagerGlobalName;
    private bool _textInputStateDirty;
    private bool _textInputStateRequiresResync;
    private WaylandTextInputSurroundingText? _textInputSurroundingText;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<WindowHandle, WaylandWindowState> _windowsByHandle;
    private readonly Dictionary<nint, WaylandWindowState> _windowsBySurface;
    private readonly Dictionary<nint, WaylandWindowState> _windowsByToplevel;
    private readonly Dictionary<nint, WaylandWindowState> _windowsByXdgSurface;
    private nint _xdgSurfaceListener;
    private nint _xdgToplevelListener;
    private nint _xdgWmBase;
    private nint _xdgWmBaseListener;

    private WaylandPlatformBackend(uint ownerId, ClipboardServicePolicy clipboardPolicy, TimeProvider? timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfNegative((int)clipboardPolicy);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)clipboardPolicy, (int)ClipboardServicePolicy.Required);

        _ownerId = ownerId;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _clipboardPolicy = clipboardPolicy;
        _clipboardStatus = (clipboardPolicy is ClipboardServicePolicy.NotRequested)
            ? ClipboardServiceStatus.NotRequested
            : ClipboardServiceStatus.Unsupported;
        _closeRequests = [];
        _gamepadStatus = PlatformServiceStatus.Unsupported;
        _inputSamples = new PlatformInputSampleQueue(_ownerThreadId);
        _keyboardKeysDown = [];
        _listenerTables = [];
        _monitorHandles = [];
        _outputsByGlobalName = [];
        _outputsByProxy = [];
        _pointerButtonsDown = [];
        _windowsBySurface = [];
        _windowsByToplevel = [];
        _windowsByXdgSurface = [];
        _windowsByHandle = [];
        _timeProvider = timeProvider ?? TimeProvider.System;
        _keyboardRepeatSchedule = new WaylandKeyboardRepeatSchedule(_timeProvider.TimestampFrequency);
        _textInputCommits = [];
    }

    public PlatformServiceStatus AudioStatus
    {
        get
        {
            EnsureActiveOwnerThread();
            return PlatformServiceStatus.NotRequested;
        }
    }

    public PlatformBackend Backend => PlatformBackend.WaylandOnly;

    public ClipboardServiceStatus ClipboardStatus => _clipboardStatus;

    public PlatformServiceStatus DisplayStatus
    {
        get
        {
            EnsureActiveOwnerThread();
            return PlatformServiceStatus.Available;
        }
    }

    public PlatformServiceStatus GamepadStatus
    {
        get
        {
            EnsureActiveOwnerThread();
            return _gamepadCapture?.GamepadStatus ?? _gamepadStatus;
        }
    }

    public uint OwnerId => _ownerId;

    public PlatformServiceStatus PhysicalInputStatus
    {
        get
        {
            EnsureActiveOwnerThread();
            return ((_keyboard != 0) || (_pointer != 0))
                ? PlatformServiceStatus.Available
                : PlatformServiceStatus.Unsupported;
        }
    }

    public bool RequiresClipboardGesture => true;

    private WaylandDisplayConnection Connection
        => _connection ?? throw new InvalidOperationException("The Wayland display connection is not initialized.");

    private WaylandProtocolMetadata ProtocolMetadata
        => _protocolMetadata ?? throw new InvalidOperationException("The Wayland protocol metadata is not initialized.");

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Backend startup must dispose every native owner acquired before a later capability or allocation failure.")]
    internal static WaylandPlatformBackend Create(
        uint ownerId,
        ClipboardServicePolicy clipboardPolicy,
        TimeProvider? timeProvider)
    {
        var backend = new WaylandPlatformBackend(ownerId, clipboardPolicy, timeProvider);

        try
        {
            backend.Initialize();
            return backend;
        }
        catch (Exception exception)
        {
            try
            {
                backend.DisposeFromOwner();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Wayland backend startup and cleanup both failed.", exception, cleanupException);
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        EnsureOwnerThread();
        DisposeFromOwner();
    }

    private nint AllocateListenerTable(params nint[] callbacks)
    {
        var listener = (nint*)NativeMemory.AllocZeroed(checked((nuint)callbacks.Length), checked((nuint)sizeof(nint)));

        if (listener is null)
        {
            throw new InvalidOperationException("The native Wayland event listener allocation failed.");
        }

        for (var index = 0; index < callbacks.Length; index++)
        {
            listener[index] = callbacks[index];
        }

        _listenerTables.Add((nint)listener);
        return (nint)listener;
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Backend shutdown must attempt retirement of every native window, output, gamepad, and display owner.")]
    private void DisposeFromOwner()
    {
        if (_isDisposed)
        {
            return;
        }

        EnsureOwnerThread();
        var failures = new List<Exception>();

        while (_windowsByHandle.Count > 0)
        {
            WaylandWindowState? window = null;

            foreach (var activeWindow in _windowsByHandle.Values)
            {
                window = activeWindow;
                break;
            }

            if (window is null)
            {
                break;
            }

            try
            {
                DestroyWindowState(window, sendDestroyRequests: true);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        while (_outputsByGlobalName.Count > 0)
        {
            WaylandOutputState? output = null;

            foreach (var activeOutput in _outputsByGlobalName.Values)
            {
                output = activeOutput;
                break;
            }

            if (output is null)
            {
                break;
            }

            try
            {
                RemoveOutput(output);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        RetireSeat(failures, notifyFocus: false);
        RetireTextInputManager(failures);
        RetireClipboardManager(failures);

        if ((_xdgWmBase != 0) && (_connection is WaylandDisplayConnection connection))
        {
            try
            {
                connection.SendRequest(_xdgWmBase, opcode: 0);
                connection.DestroyProxy(_xdgWmBase);
                _xdgWmBase = 0;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if ((_compositor != 0) && (_connection is WaylandDisplayConnection activeConnection))
        {
            try
            {
                activeConnection.DestroyProxy(_compositor);
                _compositor = 0;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        try
        {
            _gamepadCapture?.Dispose();
            _gamepadCapture = null;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            _connection?.Dispose();
            _connection = null;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            _protocolMetadata?.Dispose();
            _protocolMetadata = null;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        foreach (var listener in _listenerTables)
        {
            NativeMemory.Free((void*)listener);
        }

        _listenerTables.Clear();

        if (_callbackHandle.IsAllocated)
        {
            _callbackHandle.Free();
        }

        _isDisposed = true;

        if (failures.Count > 0)
        {
            throw new AggregateException("Wayland Platform backend cleanup failed.", failures);
        }
    }

    private void EnsureActiveOwnerThread()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("The Wayland Platform backend is bound to its creating owner thread.");
        }
    }

    private void Initialize()
    {
        var connection = WaylandDisplayConnection.Connect();
        _connection = connection;

        if (!connection.HasGlobal("wl_compositor") || !connection.HasGlobal("xdg_wm_base"))
        {
            throw new PlatformNotSupportedException("The Wayland compositor does not advertise the required wl_compositor and xdg_wm_base globals.");
        }

        var protocolMetadata = WaylandProtocolMetadata.Create(connection);
        _protocolMetadata = protocolMetadata;
        _compositor = connection.BindGlobal("wl_compositor", maximumVersion: 4);
        var xdgWmBaseGlobal = connection.GetGlobals("xdg_wm_base")[0];
        _xdgWmBase = connection.BindGlobal(xdgWmBaseGlobal, protocolMetadata.XdgWmBaseInterface, XdgWmBaseVersion);
        _callbackHandle = GCHandle.Alloc(this);

        InitializeWindowListeners();
        InitializeInputListeners();
        RefreshTextInputManager();
        _xdgWmBaseListener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, void>)&OnXdgWmBasePing);

        if (connection.AddListener(_xdgWmBase, _xdgWmBaseListener, GCHandle.ToIntPtr(_callbackHandle)) < 0)
        {
            throw new IOException("The Wayland xdg_wm_base listener could not be installed.");
        }

        InitializeOutputListeners();
        InitializeClipboard();
        RefreshSeatBinding();
        RefreshOutputBindings();
        connection.Roundtrip();
        ThrowIfCallbackFailed();
        RefreshOutputBindings();

        if (_seat != 0)
        {
            connection.Roundtrip();
            ThrowIfCallbackFailed();
        }

        if ((_clipboardPolicy is ClipboardServicePolicy.Required)
            && (_clipboardStatus is not ClipboardServiceStatus.Ready))
        {
            throw new PlatformNotSupportedException("The Wayland compositor does not provide a usable data-device clipboard for its selected seat.");
        }

        InitializeGamepadCapture();
    }

    private void InitializeGamepadCapture()
    {
        try
        {
            _gamepadCapture = new LinuxEvdevGamepadCapture(_inputSamples, _timeProvider);
            _gamepadStatus = _gamepadCapture.GamepadStatus;
        }
        catch (DllNotFoundException)
        {
            _gamepadStatus = PlatformServiceStatus.Unsupported;
        }
        catch (EntryPointNotFoundException)
        {
            _gamepadStatus = PlatformServiceStatus.Unsupported;
        }
    }

    private void ThrowIfCallbackFailed()
    {
        if (_callbackFailure is Exception callbackFailure)
        {
            throw new InvalidOperationException("Wayland Platform event processing failed.", callbackFailure);
        }
    }
}
