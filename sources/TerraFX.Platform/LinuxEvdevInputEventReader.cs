// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TerraFX.Platform;

internal sealed unsafe partial class LinuxEvdevInputEventReader : IDisposable
{
    private const ushort AbsHat0X = 0x0010;
    private const ushort AbsHat0Y = 0x0011;
    private const int AbsInfoRequestBase = 0x40;
    private const ushort AbsoluteAxisEventType = 0x0003;
    private const int AbsoluteInfoByteLength = 24;
    private const int ClockMonotonic = 1;
    private const int EAgain = 11;
    private const int EInterrupted = 4;
    private const int EInvalid = 22;
    private const int ENotTty = 25;
    private const int FcntlGetFileFlags = 3;
    private const int FcntlSetFileFlags = 4;
    private const int InputEventByteLength = 24;
    private const int IoNonBlocking = 0x0800;
    private const int IoctlReadDirection = 2;
    private const uint IoctlType = (uint)'E';
    private const int IoctlWriteDirection = 1;
    private const int KeyBitArrayByteLength = 96;
    private const ushort KeyEventType = 0x0001;
    private const ushort KeyMaximumCode = 0x02FF;
    private const int KeyStateRequestNumber = 0x18;
    private const int MaximumEventCount = 64;
    private const int SetClockRequestNumber = 0xA0;

    private readonly SafeFileHandle _handle;
    private bool _isDisposed;
    private readonly int _ownerThreadId;

    private LinuxEvdevInputEventReader(SafeFileHandle handle, bool usesMonotonicClock)
    {
        _handle = handle;
        UsesMonotonicClock = usesMonotonicClock;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    internal bool UsesMonotonicClock { get; }

    internal static nuint BuildReadRequest(int requestNumber, int size)
        => checked((nuint)(((uint)IoctlReadDirection << 30)
            | (checked((uint)size) << 16)
            | (IoctlType << 8)
            | checked((uint)requestNumber)));

    internal static (LinuxEvdevAxisInfo Info, int Value) DecodeAbsoluteInfo(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length != AbsoluteInfoByteLength)
        {
            throw new InvalidDataException("The evdev input_absinfo buffer has an invalid size.");
        }

        return (
            new LinuxEvdevAxisInfo(
                BinaryPrimitives.ReadInt32LittleEndian(buffer[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(buffer[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(buffer[12..]),
                BinaryPrimitives.ReadInt32LittleEndian(buffer[16..])),
            BinaryPrimitives.ReadInt32LittleEndian(buffer));
    }

    internal static ImmutableArray<LinuxEvdevInputEvent> DecodeBuffer(ReadOnlySpan<byte> buffer)
    {
        if ((buffer.Length % InputEventByteLength) != 0)
        {
            throw new InvalidDataException("The evdev input buffer has a partial input_event record.");
        }

        var events = ImmutableArray.CreateBuilder<LinuxEvdevInputEvent>(buffer.Length / InputEventByteLength);

        for (var offset = 0; offset < buffer.Length; offset += InputEventByteLength)
        {
            events.Add(new LinuxEvdevInputEvent(
                BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(offset, sizeof(long))),
                BinaryPrimitives.ReadInt64LittleEndian(buffer.Slice(offset + sizeof(long), sizeof(long))),
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 16, sizeof(ushort))),
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset + 18, sizeof(ushort))),
                BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(offset + 20, sizeof(int)))));
        }

        return events.MoveToImmutable();
    }

    internal static bool IsEventNodePath(string deviceNode)
    {
        const string eventNodePrefix = "/dev/input/event";

        if (!deviceNode.StartsWith(eventNodePrefix, StringComparison.Ordinal)
            || (deviceNode.Length == eventNodePrefix.Length))
        {
            return false;
        }

        for (var index = eventNodePrefix.Length; index < deviceNode.Length; index++)
        {
            if (deviceNode[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    internal static LinuxEvdevInputEventReader Open(string deviceNode)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceNode);

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Linux evdev input event devices require Linux.");
        }

        if ((IntPtr.Size != sizeof(long)) || !BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("The evdev input event reader requires a 64-bit little-endian Linux ABI.");
        }

        if (!IsEventNodePath(deviceNode))
        {
            throw new ArgumentException("An evdev event device must be a named node under /dev/input.", nameof(deviceNode));
        }

        var handle = File.OpenHandle(
            deviceNode,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            FileOptions.None);

        try
        {
            var fileDescriptor = GetFileDescriptor(handle);
            SetNonBlocking(fileDescriptor);
            return new LinuxEvdevInputEventReader(handle, TrySetMonotonicClock(fileDescriptor));
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static nuint BuildClockRequest()
        => checked((nuint)((IoctlWriteDirection << 30) | (sizeof(int) << 16) | (IoctlType << 8) | SetClockRequestNumber));

    private static IOException CreateNativeIOException(string operation, int error)
        => new($"Could not {operation}; errno {error}.", new System.ComponentModel.Win32Exception(error));

    private static int GetFileDescriptor(SafeFileHandle handle)
    {
        ObjectDisposedException.ThrowIf(handle.IsClosed || handle.IsInvalid, handle);
        return handle.DangerousGetHandle().ToInt32();
    }

    private static void SetNonBlocking(int fileDescriptor)
    {
        var flags = Native.Fcntl(fileDescriptor, FcntlGetFileFlags, 0);

        if (flags < 0)
        {
            throw CreateNativeIOException("read evdev file flags", Marshal.GetLastPInvokeError());
        }

        if (((flags & IoNonBlocking) == 0)
            && (Native.Fcntl(fileDescriptor, FcntlSetFileFlags, flags | IoNonBlocking) < 0))
        {
            throw CreateNativeIOException("set evdev nonblocking mode", Marshal.GetLastPInvokeError());
        }
    }

    private static bool TryReadAbsoluteInfo(
        int fileDescriptor,
        ushort code,
        out LinuxEvdevAxisInfo info,
        out int value)
    {
        Span<byte> buffer = stackalloc byte[AbsoluteInfoByteLength];
        buffer.Clear();

        fixed (byte* pointer = buffer)
        {
            if (Native.Ioctl(
                    fileDescriptor,
                    BuildReadRequest(AbsInfoRequestBase + code, AbsoluteInfoByteLength),
                    pointer) < 0)
            {
                var error = Marshal.GetLastPInvokeError();

                if (error == EInvalid)
                {
                    info = default;
                    value = default;
                    return false;
                }

                throw CreateNativeIOException("read evdev absolute-axis state", error);
            }
        }

        (info, value) = DecodeAbsoluteInfo(buffer);
        return true;
    }

    private static bool TrySetMonotonicClock(int fileDescriptor)
    {
        var clockId = ClockMonotonic;

        if (Native.Ioctl(fileDescriptor, BuildClockRequest(), &clockId) >= 0)
        {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();

        if (error is EInvalid or ENotTty)
        {
            return false;
        }

        throw CreateNativeIOException("select the evdev monotonic clock", error);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        EnsureOwnerThread();
        _isDisposed = true;
        _handle.Dispose();
    }

    internal ImmutableArray<LinuxEvdevInputEvent> ReadAvailable()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        Span<byte> buffer = stackalloc byte[MaximumEventCount * InputEventByteLength];

        fixed (byte* pointer = buffer)
        {
            var byteCount = Native.Read(GetFileDescriptor(_handle), pointer, checked((nuint)buffer.Length));

            if (byteCount < 0)
            {
                var error = Marshal.GetLastPInvokeError();

                if (error is EAgain or EInterrupted)
                {
                    return [];
                }

                throw CreateNativeIOException("read evdev events", error);
            }

            if (byteCount == 0)
            {
                throw new EndOfStreamException("The evdev input event device was disconnected.");
            }

            return DecodeBuffer(buffer[..checked((int)byteCount)]);
        }
    }

    internal LinuxEvdevGamepadState ReadGamepadState()
    {
        EnsureOwnerThread();
        ThrowIfDisposed();
        var fileDescriptor = GetFileDescriptor(_handle);
        var axes = new List<(ushort Code, LinuxEvdevAxisInfo Info, int InitialValue)>(6);

        for (ushort code = 0; code < 6; code++)
        {
            if (TryReadAbsoluteInfo(fileDescriptor, code, out var info, out var value))
            {
                axes.Add((code, info, value));
            }
        }

        var state = new LinuxEvdevGamepadState(axes.ToArray());
        Span<byte> keyState = stackalloc byte[KeyBitArrayByteLength];
        keyState.Clear();

        fixed (byte* pointer = keyState)
        {
            if (Native.Ioctl(
                    fileDescriptor,
                    BuildReadRequest(KeyStateRequestNumber, KeyBitArrayByteLength),
                    pointer) < 0)
            {
                throw CreateNativeIOException("read evdev key state", Marshal.GetLastPInvokeError());
            }
        }

        for (var code = 0; code <= KeyMaximumCode; code++)
        {
            if ((keyState[code >> 3] & (1 << (code & 7))) != 0)
            {
                _ = state.TryProcessEvent(KeyEventType, checked((ushort)code), 1, out _);
            }
        }

        for (var code = AbsHat0X; code <= AbsHat0Y; code++)
        {
            if (TryReadAbsoluteInfo(fileDescriptor, code, out _, out var value))
            {
                _ = state.TryProcessEvent(AbsoluteAxisEventType, code, value, out _);
            }
        }

        return state;
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The evdev input event reader is restricted to its creating thread.");
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_isDisposed, this);

    private static partial class Native
    {
        [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
        internal static partial int Fcntl(int fileDescriptor, int command, int argument);

        [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        internal static partial int Ioctl(int fileDescriptor, nuint request, void* argument);

        [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
        internal static partial nint Read(int fileDescriptor, byte* buffer, nuint byteCount);
    }
}
