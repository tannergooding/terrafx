// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;

namespace TerraFX.Platform;

internal sealed unsafe partial class X11KeyboardKeymap : IDisposable
{
    private readonly nint _connection;
    private readonly nint _context;
    private readonly int _deviceId;
    private bool _isDisposed;
    private nint _keymap;

    private X11KeyboardKeymap(nint connection, nint context, int deviceId, nint keymap)
    {
        _connection = connection;
        _context = context;
        _deviceId = deviceId;
        _keymap = keymap;
    }

    internal static X11KeyboardKeymap? TryCreate(nint display)
    {
        nint context = 0;
        nint keymap = 0;

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

            var keyboardKeymap = new X11KeyboardKeymap(connection, context, deviceId, keymap);
            (context, keymap) = (0, 0);
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
            XkbCommonNative.XkbKeymapUnref(_keymap);
            _keymap = 0;
        }

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

        var previousKeymap = _keymap;
        _keymap = keymap;
        XkbCommonNative.XkbKeymapUnref(previousKeymap);
        return true;
    }

    internal bool TryGetUsage(uint xKeycode, out ushort hidUsage)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var keyName = XkbCommonNative.XkbKeymapKeyGetName(_keymap, xKeycode);
        return XkbKeyNameMapper.TryMap(Marshal.PtrToStringUTF8(keyName), out hidUsage);
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
    }
}
