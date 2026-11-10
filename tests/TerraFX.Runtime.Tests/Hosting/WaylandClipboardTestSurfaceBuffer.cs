// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Hosting;

[SuppressMessage(
    "Reliability",
    "CA2216:Disposable types should declare finalizer",
    Justification = "The test surface shares owner-thread Wayland resources and cannot be safely finalized.")]
internal sealed unsafe partial class WaylandClipboardTestSurfaceBuffer : IDisposable
{
    private const int MapShared = 1;
    private const uint MemfdCloseOnExec = 1;
    private const int ProtectionReadWrite = 3;
    private const uint ShmFormatXrgb8888 = 1;
    private const uint SurfaceAttachOpcode = 1;
    private const uint SurfaceCommitOpcode = 6;

    private nint _buffer;
    private nint* _bufferListener;
    private Exception? _callbackFailure;
    private readonly nint _display;
    private int _fileDescriptor;
    private uint _firstShmFormat;
    private readonly int _height;
    private bool _isAttached;
    private bool _isDisposed;
    private nint _mapping;
    private readonly nuint _mappingLength;
    private readonly int _ownerThreadId;
    private nint _pool;
    private nint _registry;
    private nint* _registryListener;
    private GCHandle _registryListenerHandle;
    private nint _shm;
    private int _shmFormatCount;
    private nint* _shmListener;
    private uint _shmName;
    private uint _shmVersion;
    private bool _supportsXrgb8888;
    private readonly nint _surface;
    private nint _waylandLibrary;
    private readonly int _width;

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Partially allocated test-only Wayland resources must be released if setup fails.")]
    internal WaylandClipboardTestSurfaceBuffer(nint display, nint surface, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _callbackFailure = null;
        _display = display;
        _fileDescriptor = -1;
        _height = height;
        _isAttached = false;
        _isDisposed = false;
        _mapping = 0;
        _mappingLength = checked((nuint)width * (nuint)height * 4);
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _pool = 0;
        _registry = 0;
        _registryListener = null;
        _registryListenerHandle = default;
        _shm = 0;
        _shmListener = null;
        _firstShmFormat = 0;
        _shmFormatCount = 0;
        _shmName = 0;
        _shmVersion = 0;
        _supportsXrgb8888 = false;
        _surface = surface;
        _waylandLibrary = 0;
        _width = width;
        _buffer = 0;
        _bufferListener = null;

        try
        {
            Initialize();
        }
        catch (Exception exception)
        {
            try
            {
                DisposeCore();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("The Wayland test surface setup and cleanup both failed.", exception, cleanupException);
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
        _isDisposed = true;
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    private void AttachBuffer()
    {
        var attachArguments = stackalloc WaylandArgument[3];
        attachArguments[0].Pointer = _buffer;
        attachArguments[1].Unsigned = 0;
        attachArguments[2].Unsigned = 0;
        SendRequest(_surface, SurfaceAttachOpcode, attachArguments);

        var damageArguments = stackalloc WaylandArgument[4];
        damageArguments[0].Unsigned = 0;
        damageArguments[1].Unsigned = 0;
        damageArguments[2].Unsigned = checked((uint)_width);
        damageArguments[3].Unsigned = checked((uint)_height);
        SendRequest(_surface, opcode: 2, damageArguments);
        SendRequest(_surface, SurfaceCommitOpcode, arguments: null);
        _isAttached = true;

        if (WaylandNative.DisplayRoundtrip(_display) < 0)
        {
            throw new IOException("The Wayland test surface could not complete its mapping roundtrip.");
        }
    }

    private void CreateBuffer()
    {
        var descriptor = WaylandNative.MemfdCreate("terrafx-wayland-test-buffer", flags: MemfdCloseOnExec);

        if (descriptor < 0)
        {
            throw CreateNativeIOException("create the shared-memory file");
        }

        _fileDescriptor = descriptor;

        if (WaylandNative.Ftruncate(descriptor, checked((nint)_mappingLength)) < 0)
        {
            throw CreateNativeIOException("size the shared-memory file");
        }

        _mapping = WaylandNative.Mmap(
            address: 0,
            _mappingLength,
            protection: ProtectionReadWrite,
            flags: MapShared,
            descriptor,
            offset: 0);

        if (_mapping == (nint)(-1))
        {
            _mapping = 0;
            throw CreateNativeIOException("map the shared-memory file");
        }

        var pixels = new Span<byte>((void*)_mapping, checked((int)_mappingLength));

        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = byte.MaxValue;
        }

        var poolArguments = stackalloc WaylandArgument[3];
        poolArguments[0] = default;
        poolArguments[1].FileDescriptor = descriptor;
        poolArguments[2].Unsigned = checked((uint)_mappingLength);
        _pool = WaylandNative.ProxyMarshalArrayFlags(
            _shm,
            opcode: 0,
            GetInterface("wl_shm_pool_interface"),
            WaylandNative.ProxyGetVersion(_shm),
            flags: 0,
            poolArguments);

        if (_pool == 0)
        {
            throw new InvalidOperationException("The private Wayland compositor did not create a shared-memory pool.");
        }

        if (WaylandNative.Close(descriptor) < 0)
        {
            throw CreateNativeIOException("close the shared-memory file descriptor");
        }

        _fileDescriptor = -1;

        var bufferArguments = stackalloc WaylandArgument[6];
        bufferArguments[0] = default;
        bufferArguments[1].Unsigned = 0;
        bufferArguments[2].Unsigned = checked((uint)_width);
        bufferArguments[3].Unsigned = checked((uint)_height);
        bufferArguments[4].Unsigned = checked((uint)(_width * 4));
        bufferArguments[5].Unsigned = ShmFormatXrgb8888;
        _buffer = WaylandNative.ProxyMarshalArrayFlags(
            _pool,
            opcode: 0,
            GetInterface("wl_buffer_interface"),
            WaylandNative.ProxyGetVersion(_pool),
            flags: 0,
            bufferArguments);

        if (_buffer == 0)
        {
            throw new InvalidOperationException("The private Wayland compositor did not create a shared-memory buffer.");
        }

        _bufferListener = (nint*)NativeMemory.AllocZeroed(checked((nuint)sizeof(nint)));

        if (_bufferListener is null)
        {
            throw new InvalidOperationException("The Wayland test surface wl_buffer listener allocation failed.");
        }

        _bufferListener[0] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnBufferRelease;

        if (WaylandNative.ProxyAddListener(
                _buffer,
                (nint)_bufferListener,
                GCHandle.ToIntPtr(_registryListenerHandle)) < 0)
        {
            throw new IOException("The Wayland test surface wl_buffer listener could not be installed.");
        }
    }

    private void CreateShmProxy()
    {
        if (_shmName == 0)
        {
            throw new PlatformNotSupportedException("The private Wayland compositor does not advertise wl_shm.");
        }

        var interfaceName = Marshal.StringToCoTaskMemUTF8("wl_shm");

        try
        {
            var version = Math.Min(_shmVersion, 2);
            var arguments = stackalloc WaylandArgument[4];
            arguments[0].Unsigned = _shmName;
            arguments[1].Pointer = interfaceName;
            arguments[2].Unsigned = version;
            arguments[3].Pointer = 0;
            _shm = WaylandNative.ProxyMarshalArrayFlags(
                _registry,
                opcode: 0,
                GetInterface("wl_shm_interface"),
                version,
                flags: 0,
                arguments);
        }
        finally
        {
            Marshal.FreeCoTaskMem(interfaceName);
        }

        if (_shm == 0)
        {
            throw new InvalidOperationException("The private Wayland compositor did not bind wl_shm.");
        }

        _shmListener = (nint*)NativeMemory.AllocZeroed(checked((nuint)sizeof(nint)));

        if (_shmListener is null)
        {
            throw new InvalidOperationException("The Wayland test surface wl_shm listener allocation failed.");
        }

        _shmListener[0] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, void>)&OnShmFormat;

        if (WaylandNative.ProxyAddListener(
                _shm,
                (nint)_shmListener,
                GCHandle.ToIntPtr(_registryListenerHandle)) < 0)
        {
            throw new IOException("The Wayland test surface wl_shm listener could not be installed.");
        }

        if (WaylandNative.DisplayRoundtrip(_display) < 0)
        {
            throw new IOException("The Wayland test surface could not receive its shared-memory formats.");
        }

        if (!_supportsXrgb8888)
        {
            throw new PlatformNotSupportedException(
                $"The private Wayland compositor did not advertise XRGB8888; it sent {_shmFormatCount} shared-memory format(s), "
                + $"starting with 0x{_firstShmFormat:X8}.");
        }
    }

    private void DisposeCore()
    {
        if (_isAttached)
        {
            var attachArguments = stackalloc WaylandArgument[3];
            attachArguments[0] = default;
            attachArguments[1].Unsigned = 0;
            attachArguments[2].Unsigned = 0;
            SendRequest(_surface, SurfaceAttachOpcode, attachArguments);
            SendRequest(_surface, SurfaceCommitOpcode, arguments: null);
            _isAttached = false;
        }

        DestroyProxy(ref _buffer, opcode: 0, sendDestructorRequest: true);

        if (_bufferListener is not null)
        {
            NativeMemory.Free(_bufferListener);
            _bufferListener = null;
        }

        DestroyProxy(ref _pool, opcode: 1, sendDestructorRequest: true);
        DestroyProxy(
            ref _shm,
            opcode: 1,
            sendDestructorRequest: (_shm != 0) && (WaylandNative.ProxyGetVersion(_shm) >= 2));
        DestroyProxy(ref _registry, opcode: 0, sendDestructorRequest: false);

        if (_shmListener is not null)
        {
            NativeMemory.Free(_shmListener);
            _shmListener = null;
        }

        if (_registryListener is not null)
        {
            NativeMemory.Free(_registryListener);
            _registryListener = null;
        }

        if (_registryListenerHandle.IsAllocated)
        {
            _registryListenerHandle.Free();
        }

        if (_mapping != 0)
        {
            _ = WaylandNative.Munmap(_mapping, _mappingLength);
            _mapping = 0;
        }

        if (_fileDescriptor >= 0)
        {
            _ = WaylandNative.Close(_fileDescriptor);
            _fileDescriptor = -1;
        }

        if (_waylandLibrary != 0)
        {
            NativeLibrary.Free(_waylandLibrary);
            _waylandLibrary = 0;
        }
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("The Wayland test surface is bound to its creating owner thread.");
        }
    }

    private nint GetInterface(string name)
        => NativeLibrary.GetExport(_waylandLibrary, name);

    private void Initialize()
    {
        if (_display == 0)
        {
            throw new ArgumentException("The Wayland display is not valid.");
        }

        if (_surface == 0)
        {
            throw new ArgumentException("The Wayland surface is not valid.");
        }

        _waylandLibrary = NativeLibrary.Load("libwayland-client.so.0");
        var registryArguments = stackalloc WaylandArgument[1];
        registryArguments[0] = default;
        _registry = WaylandNative.ProxyMarshalArrayFlags(
            _display,
            opcode: 1,
            GetInterface("wl_registry_interface"),
            WaylandNative.ProxyGetVersion(_display),
            flags: 0,
            registryArguments);

        if (_registry == 0)
        {
            throw new InvalidOperationException("The Wayland test surface could not create a registry proxy.");
        }

        _registryListenerHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        _registryListener = (nint*)NativeMemory.AllocZeroed(checked((nuint)(2 * sizeof(nint))));

        if (_registryListener is null)
        {
            throw new InvalidOperationException("The Wayland test surface registry listener allocation failed.");
        }

        _registryListener[0] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, nint, uint, void>)&OnGlobal;
        _registryListener[1] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, void>)&OnGlobalRemove;

        if (WaylandNative.ProxyAddListener(
                _registry,
                (nint)_registryListener,
                GCHandle.ToIntPtr(_registryListenerHandle)) < 0)
        {
            throw new IOException("The Wayland test surface registry listener could not be installed.");
        }

        if (WaylandNative.DisplayRoundtrip(_display) < 0)
        {
            throw new IOException("The Wayland test surface could not complete its registry roundtrip.");
        }

        if (_callbackFailure is Exception callbackFailure)
        {
            throw new InvalidOperationException("The Wayland test surface registry callback failed.", callbackFailure);
        }

        CreateShmProxy();
        CreateBuffer();
        AttachBuffer();
    }

    private static partial class WaylandNative
    {
        private const string WaylandClientLibrary = "libwayland-client.so.0";

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        internal static partial int Close(int descriptor);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_display_roundtrip", SetLastError = true)]
        internal static partial int DisplayRoundtrip(nint display);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libc", EntryPoint = "ftruncate", SetLastError = true)]
        internal static partial int Ftruncate(int descriptor, nint length);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libc", EntryPoint = "memfd_create", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int MemfdCreate(string name, uint flags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libc", EntryPoint = "mmap", SetLastError = true)]
        internal static partial nint Mmap(nint address, nuint length, int protection, int flags, int descriptor, nint offset);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libc", EntryPoint = "munmap", SetLastError = true)]
        internal static partial int Munmap(nint address, nuint length);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_proxy_add_listener")]
        internal static partial int ProxyAddListener(nint proxy, nint implementation, nint data);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_proxy_destroy")]
        internal static partial void ProxyDestroy(nint proxy);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_proxy_get_version")]
        internal static partial uint ProxyGetVersion(nint proxy);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport(WaylandClientLibrary, EntryPoint = "wl_proxy_marshal_array_flags")]
        internal static partial nint ProxyMarshalArrayFlags(
            nint proxy,
            uint opcode,
            nint interfacePointer,
            uint version,
            uint flags,
            WaylandArgument* arguments);
    }
}
