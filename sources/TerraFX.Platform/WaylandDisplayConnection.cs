// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TerraFX.Platform;

[SuppressMessage(
    "Reliability",
    "CA2216:Disposable types should declare finalizer",
    Justification = "The Wayland connection and registry callbacks are owner-thread resources and cannot be safely disconnected from a finalizer.")]
internal sealed unsafe partial class WaylandDisplayConnection : IDisposable
{
    private const int DisplayGetRegistryOpcode = 1;
    private const int ErrorInterrupted = 4;
    private const int ErrorTryAgain = 11;
    private const short PollError = 8;
    private const short PollHangup = 16;
    private const short PollInput = 1;
    private const short PollInvalid = 32;

    private Exception? _callbackFailure;
    private nint _display;
    private readonly List<WaylandGlobal> _globals = [];
    private bool _isDisposed;
    private readonly int _ownerThreadId;
    private nint _registry;
    private nint* _registryListener;
    private GCHandle _registryListenerHandle;
    private nint _waylandLibrary;

    private WaylandDisplayConnection()
    {
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    internal nint Display
    {
        get
        {
            EnsureActiveOwnerThread();
            return _display;
        }
    }

    internal static WaylandDisplayConnection Connect()
    {
        var connection = new WaylandDisplayConnection();

        try
        {
            connection.Initialize();
            return connection;
        }
        catch (Exception exception)
        {
            try
            {
                connection.DisposeCore();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Wayland connection setup and cleanup both failed.", exception, cleanupException);
            }

            throw;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnGlobal(nint data, nint registry, uint name, nint interfaceNamePointer, uint version)
    {
        _ = registry;

        try
        {
            if (GCHandle.FromIntPtr(data).Target is WaylandDisplayConnection connection)
            {
                connection.AddGlobal(name, interfaceNamePointer, version);
            }
        }
        catch (Exception exception)
        {
            if (GCHandle.FromIntPtr(data).Target is WaylandDisplayConnection connection)
            {
                connection._callbackFailure ??= exception;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnGlobalRemove(nint data, nint registry, uint name)
    {
        _ = registry;

        try
        {
            if (GCHandle.FromIntPtr(data).Target is WaylandDisplayConnection connection)
            {
                for (var index = connection._globals.Count - 1; index >= 0; index--)
                {
                    if (connection._globals[index].Name == name)
                    {
                        connection._globals.RemoveAt(index);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            if (GCHandle.FromIntPtr(data).Target is WaylandDisplayConnection connection)
            {
                connection._callbackFailure ??= exception;
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        EnsureOwnerThread();
        DisposeCore();
    }

    internal int AddListener(nint proxy, nint implementation, nint data)
    {
        EnsureActiveOwnerThread();
        return WaylandNative.ProxyAddListener(proxy, implementation, data);
    }

    internal nint BindGlobal(WaylandGlobal global, nint interfacePointer, uint version)
    {
        EnsureActiveOwnerThread();
        ThrowIfCallbackFailed();

        if ((interfacePointer == 0) || (version == 0) || (version > global.Version))
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        if (!_globals.Contains(global))
        {
            throw new InvalidOperationException("The Wayland global was removed before it could be bound.");
        }

        var interfaceName = Marshal.StringToCoTaskMemUTF8(global.InterfaceName);

        try
        {
            var arguments = stackalloc WaylandArgument[4];
            arguments[0].Unsigned = global.Name;
            arguments[1].Pointer = interfaceName;
            arguments[2].Unsigned = version;
            arguments[3].Pointer = 0;
            var proxy = WaylandNative.ProxyMarshalArrayFlags(
                _registry,
                opcode: 0,
                interfacePointer,
                version,
                flags: 0,
                arguments);

            if (proxy == 0)
            {
                throw new IOException($"The Wayland compositor refused to bind {global.InterfaceName}.");
            }

            return proxy;
        }
        finally
        {
            Marshal.FreeCoTaskMem(interfaceName);
        }
    }

    internal nint BindGlobal(string interfaceName, uint maximumVersion)
    {
        ArgumentNullException.ThrowIfNull(interfaceName);
        ArgumentOutOfRangeException.ThrowIfZero(maximumVersion);
        EnsureActiveOwnerThread();
        ThrowIfCallbackFailed();

        WaylandGlobal? selectedGlobal = null;

        foreach (var global in _globals)
        {
            if (global.InterfaceName.Equals(interfaceName, StringComparison.Ordinal)
                && ((selectedGlobal is null) || (global.Version > selectedGlobal.Value.Version)))
            {
                selectedGlobal = global;
            }
        }

        var target = selectedGlobal
            ?? throw new PlatformNotSupportedException($"The Wayland compositor does not advertise {interfaceName}.");
        var version = Math.Min(target.Version, maximumVersion);
        var interfacePointer = GetInterface($"{interfaceName}_interface");
        return BindGlobal(target, interfacePointer, version);
    }

    internal nint CreateProxy(
        nint proxy,
        uint opcode,
        nint interfacePointer,
        uint version,
        WaylandArgument* arguments)
    {
        EnsureActiveOwnerThread();
        return WaylandNative.ProxyMarshalArrayFlags(proxy, opcode, interfacePointer, version, flags: 0, arguments);
    }

    internal void DestroyProxy(nint proxy)
    {
        EnsureActiveOwnerThread();
        WaylandNative.ProxyDestroy(proxy);
    }

    internal ImmutableArray<WaylandGlobal> GetGlobals(string interfaceName)
    {
        ArgumentNullException.ThrowIfNull(interfaceName);
        EnsureActiveOwnerThread();
        ThrowIfCallbackFailed();
        var globals = ImmutableArray.CreateBuilder<WaylandGlobal>();

        foreach (var global in _globals)
        {
            if (global.InterfaceName.Equals(interfaceName, StringComparison.Ordinal))
            {
                globals.Add(global);
            }
        }

        return globals.ToImmutable();
    }

    internal nint GetInterface(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        EnsureActiveOwnerThread();
        ThrowIfCallbackFailed();
        return NativeLibrary.GetExport(_waylandLibrary, name);
    }

    internal uint GetProxyVersion(nint proxy)
    {
        EnsureActiveOwnerThread();
        return WaylandNative.ProxyGetVersion(proxy);
    }

    internal bool HasGlobal(string interfaceName)
    {
        ArgumentNullException.ThrowIfNull(interfaceName);
        EnsureActiveOwnerThread();
        ThrowIfCallbackFailed();
        return _globals.Exists(global => global.InterfaceName.Equals(interfaceName, StringComparison.Ordinal));
    }

    internal void Pump()
    {
        EnsureActiveOwnerThread();
        ThrowIfCallbackFailed();

        while (WaylandNative.DisplayPrepareRead(_display) != 0)
        {
            if (WaylandNative.DisplayDispatchPending(_display) < 0)
            {
                throw CreateDisplayException("dispatch pending events");
            }

            ThrowIfCallbackFailed();
        }

        var readPrepared = true;

        try
        {
            var fileDescriptor = WaylandNative.DisplayGetFileDescriptor(_display);
            var descriptor = new PollDescriptor {
                FileDescriptor = fileDescriptor,
                Events = PollInput,
            };
            var pollResult = WaylandNative.Poll(&descriptor, count: 1, timeoutMilliseconds: 0);

            if (pollResult < 0)
            {
                var nativeError = Marshal.GetLastPInvokeError();

                if (nativeError is ErrorInterrupted)
                {
                    return;
                }

                throw new IOException($"Wayland polling failed with native error {nativeError}.");
            }

            if ((pollResult == 0) || ((descriptor.ReturnedEvents & (PollInput | PollError | PollHangup | PollInvalid)) == 0))
            {
                WaylandNative.DisplayCancelRead(_display);
                readPrepared = false;
            }
            else
            {
                var readStatus = WaylandNative.DisplayReadEvents(_display);
                readPrepared = false;

                if (readStatus < 0)
                {
                    throw CreateDisplayException("read events");
                }
            }
        }
        finally
        {
            if (readPrepared)
            {
                WaylandNative.DisplayCancelRead(_display);
            }
        }

        if (WaylandNative.DisplayDispatchPending(_display) < 0)
        {
            throw CreateDisplayException("dispatch events");
        }

        ThrowIfCallbackFailed();

        if ((WaylandNative.DisplayFlush(_display) < 0)
            && (Marshal.GetLastPInvokeError() != ErrorTryAgain))
        {
            throw CreateDisplayException("flush requests");
        }
    }

    internal void Roundtrip()
    {
        EnsureActiveOwnerThread();
        ThrowIfCallbackFailed();

        if (WaylandNative.DisplayRoundtrip(_display) < 0)
        {
            throw CreateDisplayException("complete a registry synchronization roundtrip");
        }

        ThrowIfCallbackFailed();
    }

    internal void SendRequest(nint proxy, uint opcode)
    {
        EnsureActiveOwnerThread();
        _ = WaylandNative.ProxyMarshalArrayFlags(
            proxy,
            opcode,
            interfacePointer: 0,
            version: WaylandNative.ProxyGetVersion(proxy),
            flags: 0,
            arguments: null);
    }

    internal void SendRequest(nint proxy, uint opcode, WaylandArgument* arguments)
    {
        EnsureActiveOwnerThread();
        _ = WaylandNative.ProxyMarshalArrayFlags(
            proxy,
            opcode,
            interfacePointer: 0,
            version: WaylandNative.ProxyGetVersion(proxy),
            flags: 0,
            arguments);
    }

    private void AddGlobal(uint name, nint interfaceNamePointer, uint version)
    {
        var interfaceName = Marshal.PtrToStringUTF8(interfaceNamePointer)
            ?? throw new InvalidOperationException("Wayland announced a global without an interface name.");
        _globals.Add(new WaylandGlobal(name, interfaceName, version));
    }

    private IOException CreateDisplayException(string operation)
    {
        var nativeError = WaylandNative.DisplayGetError(_display);

        if (nativeError == 0)
        {
            nativeError = Marshal.GetLastPInvokeError();
        }

        return new IOException($"Wayland could not {operation}; native error {nativeError}.");
    }

    private void DisposeCore()
    {
        if (_display != 0)
        {
            WaylandNative.DisplayDisconnect(_display);
            _display = 0;
        }

        if (_registryListener != null)
        {
            NativeMemory.Free(_registryListener);
            _registryListener = null;
        }

        if (_registryListenerHandle.IsAllocated)
        {
            _registryListenerHandle.Free();
        }

        if (_waylandLibrary != 0)
        {
            NativeLibrary.Free(_waylandLibrary);
            _waylandLibrary = 0;
        }

        _registry = 0;
        _isDisposed = true;
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
            throw new InvalidOperationException("The Wayland display connection is bound to its creating owner thread.");
        }
    }

    private void Initialize()
    {
        _waylandLibrary = NativeLibrary.Load("libwayland-client.so.0");
        _display = WaylandNative.DisplayConnect(name: 0);

        if (_display == 0)
        {
            throw new PlatformNotSupportedException("The Wayland session connection could not be opened.");
        }

        var registryInterface = NativeLibrary.GetExport(_waylandLibrary, "wl_registry_interface");
        var registryArguments = stackalloc WaylandArgument[1];
        registryArguments[0] = default;
        _registry = WaylandNative.ProxyMarshalArrayFlags(
            _display,
            DisplayGetRegistryOpcode,
            registryInterface,
            WaylandNative.ProxyGetVersion(_display),
            flags: 0,
            registryArguments);

        if (_registry == 0)
        {
            throw new PlatformNotSupportedException("The Wayland registry proxy could not be created.");
        }

        _registryListenerHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        _registryListener = (nint*)NativeMemory.AllocZeroed(checked((nuint)(2 * sizeof(nint))));

        if (_registryListener is null)
        {
            throw new InvalidOperationException("The native Wayland registry listener allocation failed.");
        }

        _registryListener[0] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, nint, uint, void>)&OnGlobal;
        _registryListener[1] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, void>)&OnGlobalRemove;

        if (WaylandNative.ProxyAddListener(
                _registry,
                (nint)_registryListener,
                GCHandle.ToIntPtr(_registryListenerHandle)) < 0)
        {
            throw new IOException("The Wayland registry listener could not be installed.");
        }

        if (WaylandNative.DisplayRoundtrip(_display) < 0)
        {
            throw CreateDisplayException("complete the initial registry roundtrip");
        }

        ThrowIfCallbackFailed();
    }

    private void ThrowIfCallbackFailed()
    {
        if (_callbackFailure is Exception callbackFailure)
        {
            throw new InvalidOperationException("Wayland registry event processing failed.", callbackFailure);
        }
    }

    private static partial class WaylandNative
    {
        private const string WaylandClientLibrary = "libwayland-client.so.0";

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_display_cancel_read")]
        internal static partial void DisplayCancelRead(nint display);

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_display_connect", SetLastError = true)]
        internal static partial nint DisplayConnect(nint name);

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_display_disconnect")]
        internal static partial void DisplayDisconnect(nint display);

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_display_dispatch_pending", SetLastError = true)]
        internal static partial int DisplayDispatchPending(nint display);

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_display_flush", SetLastError = true)]
        internal static partial int DisplayFlush(nint display);

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_display_get_error")]
        internal static partial int DisplayGetError(nint display);

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_display_get_fd")]
        internal static partial int DisplayGetFileDescriptor(nint display);

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_display_prepare_read", SetLastError = true)]
        internal static partial int DisplayPrepareRead(nint display);

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_display_read_events", SetLastError = true)]
        internal static partial int DisplayReadEvents(nint display);

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_display_roundtrip", SetLastError = true)]
        internal static partial int DisplayRoundtrip(nint display);

        [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
        internal static partial int Poll(PollDescriptor* descriptors, nuint count, int timeoutMilliseconds);

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_proxy_add_listener")]
        internal static partial int ProxyAddListener(nint proxy, nint implementation, nint data);

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_proxy_destroy")]
        internal static partial void ProxyDestroy(nint proxy);

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_proxy_get_version")]
        internal static partial uint ProxyGetVersion(nint proxy);

        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_proxy_marshal_array_flags")]
        internal static partial nint ProxyMarshalArrayFlags(
            nint proxy,
            uint opcode,
            nint interfacePointer,
            uint version,
            uint flags,
            WaylandArgument* arguments);
    }

    private struct PollDescriptor
    {
        internal int FileDescriptor;
        internal short Events;
        internal short ReturnedEvents;
    }
}
