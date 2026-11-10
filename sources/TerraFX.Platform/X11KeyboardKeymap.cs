// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TerraFX.Platform;

internal sealed unsafe partial class X11KeyboardKeymap : IDisposable
{
    private readonly XkbComposeState? _composeState;
    private readonly nint _connection;
    private readonly nint _context;
    private readonly int _deviceId;
    private bool _isDisposed;
    private nint _keymap;
    private nint _state;

    private X11KeyboardKeymap(
        nint connection,
        nint context,
        int deviceId,
        nint keymap,
        nint state,
        XkbComposeState? composeState)
    {
        _connection = connection;
        _context = context;
        _deviceId = deviceId;
        _keymap = keymap;
        _state = state;
        _composeState = composeState;
    }

    internal static X11KeyboardKeymap? TryCreate(nint display)
    {
        nint context = 0;
        nint keymap = 0;
        nint state = 0;
        XkbComposeState? composeState = null;

        try
        {
            var connection = X11XcbNative.XGetXCBConnection(display);

            if (connection == 0)
            {
                return null;
            }

            context = XkbCommonNative.XkbContextNew(flags: 0);

            if (context == 0)
            {
                return null;
            }

            var majorVersion = (ushort)0;
            var minorVersion = (ushort)0;
            var baseEvent = (byte)0;
            var baseError = (byte)0;

            if (XkbCommonX11Native.XkbX11SetupExtension(
                    connection,
                    majorXkbVersion: 1,
                    minorXkbVersion: 0,
                    flags: 0,
                    &majorVersion,
                    &minorVersion,
                    &baseEvent,
                    &baseError) == 0)
            {
                return null;
            }

            var deviceId = XkbCommonX11Native.XkbX11GetCoreKeyboardDeviceId(connection);

            if (deviceId < 0)
            {
                return null;
            }

            keymap = XkbCommonX11Native.XkbX11KeymapNewFromDevice(
                context,
                connection,
                deviceId,
                flags: 0);

            if (keymap == 0)
            {
                return null;
            }

            state = XkbCommonX11Native.XkbX11StateNewFromDevice(keymap, connection, deviceId);

            if (state == 0)
            {
                return null;
            }

            composeState = XkbComposeState.TryCreate();
            var keyboardKeymap = new X11KeyboardKeymap(connection, context, deviceId, keymap, state, composeState);
            (context, keymap, state, composeState) = (0, 0, 0, null);
            return keyboardKeymap;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (keymap != 0)
            {
                XkbCommonNative.XkbKeymapUnref(keymap);
            }

            if (state != 0)
            {
                XkbCommonNative.XkbStateUnref(state);
            }

            composeState?.Dispose();

            if (context != 0)
            {
                XkbCommonNative.XkbContextUnref(context);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_keymap != 0)
        {
            XkbCommonNative.XkbStateUnref(_state);
            _state = 0;
            XkbCommonNative.XkbKeymapUnref(_keymap);
            _keymap = 0;
        }

        _composeState?.Dispose();
        XkbCommonNative.XkbContextUnref(_context);
        _isDisposed = true;
    }

    internal bool Refresh()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var keymap = XkbCommonX11Native.XkbX11KeymapNewFromDevice(
            _context,
            _connection,
            _deviceId,
            flags: 0);

        if (keymap == 0)
        {
            return false;
        }

        nint state = 0;

        try
        {
            state = XkbCommonX11Native.XkbX11StateNewFromDevice(keymap, _connection, _deviceId);

            if (state == 0)
            {
                return false;
            }

            _composeState?.Reset();
            var previousKeymap = _keymap;
            var previousState = _state;
            _keymap = keymap;
            _state = state;
            keymap = 0;
            state = 0;
            XkbCommonNative.XkbStateUnref(previousState);
            XkbCommonNative.XkbKeymapUnref(previousKeymap);
            return true;
        }
        finally
        {
            if (state != 0)
            {
                XkbCommonNative.XkbStateUnref(state);
            }

            if (keymap != 0)
            {
                XkbCommonNative.XkbKeymapUnref(keymap);
            }
        }
    }

    internal void ResetComposeState()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _composeState?.Reset();
    }

    internal bool TryGetUsage(uint xKeycode, out ushort hidUsage)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var keyName = XkbCommonNative.XkbKeymapKeyGetName(_keymap, xKeycode);
        return XkbKeyNameMapper.TryMap(Marshal.PtrToStringUTF8(keyName), out hidUsage);
    }

    internal string? UpdateKey(uint xKeycode, bool isDown, bool allowCommittedText = true)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _ = XkbCommonNative.XkbStateUpdateKey(_state, xKeycode, isDown ? 1u : 0u);

        if (!isDown || !allowCommittedText)
        {
            return null;
        }

        var keysym = XkbCommonNative.XkbStateKeyGetOneSym(_state, xKeycode);

        if (_composeState is not XkbComposeState composeState)
        {
            return ReadKeyText(xKeycode);
        }

        var status = composeState.Feed(keysym, out var committedText);

        return status switch {
            XkbComposeStatus.Composing => null,
            XkbComposeStatus.Committed => committedText,
            XkbComposeStatus.Cancelled => ReadKeyText(xKeycode),
            _ => ReadKeyText(xKeycode),
        };
    }

    private string? ReadKeyText(uint keycode)
    {
        var byteCount = XkbCommonNative.XkbStateKeyGetUtf8(_state, keycode, buffer: null, size: 0);

        if (byteCount <= 0)
        {
            return null;
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteCount, 4096);
        Span<byte> bytes = stackalloc byte[byteCount + 1];
        int written;

        fixed (byte* text = bytes)
        {
            written = XkbCommonNative.XkbStateKeyGetUtf8(_state, keycode, text, checked((nuint)bytes.Length));
        }

        if (written != byteCount)
        {
            throw new InvalidOperationException("libxkbcommon returned an inconsistent keyboard string length.");
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(bytes[..written]);
    }

    private static partial class X11XcbNative
    {
        [LibraryImport("libX11-xcb.so.1", EntryPoint = "XGetXCBConnection")]
        internal static partial nint XGetXCBConnection(nint display);
    }

    private static partial class XkbCommonNative
    {
        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_context_new")]
        internal static partial nint XkbContextNew(uint flags);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_context_unref")]
        internal static partial void XkbContextUnref(nint context);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_keymap_key_get_name")]
        internal static partial nint XkbKeymapKeyGetName(nint keymap, uint key);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_keymap_unref")]
        internal static partial void XkbKeymapUnref(nint keymap);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_state_key_get_one_sym")]
        internal static partial uint XkbStateKeyGetOneSym(nint state, uint key);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_state_key_get_utf8")]
        internal static partial int XkbStateKeyGetUtf8(nint state, uint key, byte* buffer, nuint size);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_state_unref")]
        internal static partial void XkbStateUnref(nint state);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_state_update_key")]
        internal static partial uint XkbStateUpdateKey(nint state, uint key, uint direction);
    }

    private static unsafe partial class XkbCommonX11Native
    {
        [LibraryImport("libxkbcommon-x11.so.0", EntryPoint = "xkb_x11_get_core_keyboard_device_id")]
        internal static partial int XkbX11GetCoreKeyboardDeviceId(nint connection);

        [LibraryImport("libxkbcommon-x11.so.0", EntryPoint = "xkb_x11_keymap_new_from_device")]
        internal static partial nint XkbX11KeymapNewFromDevice(
            nint context,
            nint connection,
            int deviceId,
            uint flags);

        [LibraryImport("libxkbcommon-x11.so.0", EntryPoint = "xkb_x11_setup_xkb_extension")]
        internal static partial int XkbX11SetupExtension(
            nint connection,
            ushort majorXkbVersion,
            ushort minorXkbVersion,
            uint flags,
            ushort* majorXkbVersionOut,
            ushort* minorXkbVersionOut,
            byte* baseEventOut,
            byte* baseErrorOut);

        [LibraryImport("libxkbcommon-x11.so.0", EntryPoint = "xkb_x11_state_new_from_device")]
        internal static partial nint XkbX11StateNewFromDevice(nint keymap, nint connection, int deviceId);
    }
}
