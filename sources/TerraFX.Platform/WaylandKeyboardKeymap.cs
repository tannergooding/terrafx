// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TerraFX.Platform;

[SuppressMessage(
    "Reliability",
    "CA2216:Disposable types should declare finalizer",
    Justification = "The keymap belongs to the Wayland keyboard owner thread.")]
internal sealed unsafe partial class WaylandKeyboardKeymap : IDisposable
{
    private const uint XkbKeymapFormatTextV1 = 1;
    private readonly nint _context;
    private bool _isDisposed;
    private readonly nint _keymap;

    private WaylandKeyboardKeymap(nint context, nint keymap)
    {
        _context = context;
        _keymap = keymap;
    }

    internal static WaylandKeyboardKeymap? TryCreate(uint format, int fileDescriptor, uint size)
    {
        if (fileDescriptor < 0)
        {
            if ((format == 0) && (size == 0))
            {
                return null;
            }

            throw new InvalidOperationException("The Wayland keyboard keymap descriptor is invalid.");
        }

        using var keymapFile = new SafeFileHandle((nint)fileDescriptor, ownsHandle: true);

        if (format is not XkbKeymapFormatTextV1)
        {
            return null;
        }

        if (size is 0 or > 1_048_576)
        {
            throw new InvalidOperationException("The Wayland keyboard keymap has an invalid size.");
        }

        var keymapBytes = new byte[checked((int)size)];
        var bytesRead = 0;

        while (bytesRead < keymapBytes.Length)
        {
            var read = RandomAccess.Read(keymapFile, keymapBytes.AsSpan(bytesRead), bytesRead);

            if (read == 0)
            {
                throw new IOException("The Wayland keyboard keymap descriptor ended before the advertised size.");
            }

            bytesRead += read;
        }

        if (keymapBytes[^1] != 0)
        {
            throw new InvalidOperationException("The Wayland keyboard keymap is not null-terminated.");
        }

        nint context = 0;
        nint keymap = 0;

        try
        {
            context = Native.XkbContextNew(flags: 0);

            if (context == 0)
            {
                throw new InvalidOperationException("libxkbcommon could not create a keymap context.");
            }

            fixed (byte* keymapText = keymapBytes)
            {
                keymap = Native.XkbKeymapNewFromString(context, (nint)keymapText, format: checked((int)XkbKeymapFormatTextV1), flags: 0);
            }

            if (keymap == 0)
            {
                throw new InvalidOperationException("libxkbcommon rejected the Wayland keyboard keymap.");
            }

            var result = new WaylandKeyboardKeymap(context, keymap);
            context = 0;
            keymap = 0;
            return result;
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
                Native.XkbKeymapUnref(keymap);
            }

            if (context != 0)
            {
                Native.XkbContextUnref(context);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Native.XkbKeymapUnref(_keymap);
        Native.XkbContextUnref(_context);
        _isDisposed = true;
    }

    internal bool CanRepeat(uint evdevKeycode)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (evdevKeycode is > 0x2FF)
        {
            return false;
        }

        return Native.XkbKeymapKeyRepeats(_keymap, checked(evdevKeycode + 8)) != 0;
    }

    internal bool TryGetUsage(uint evdevKeycode, out ushort hidUsage)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        hidUsage = 0;

        if (evdevKeycode is > 0x2FF)
        {
            return false;
        }

        var keyName = Native.XkbKeymapKeyGetName(_keymap, checked(evdevKeycode + 8));
        return XkbKeyNameMapper.TryMap(Marshal.PtrToStringUTF8(keyName), out hidUsage);
    }

    private static partial class Native
    {
        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_context_new")]
        internal static partial nint XkbContextNew(uint flags);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_context_unref")]
        internal static partial void XkbContextUnref(nint context);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_keymap_key_get_name")]
        internal static partial nint XkbKeymapKeyGetName(nint keymap, uint key);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_keymap_key_repeats")]
        internal static partial int XkbKeymapKeyRepeats(nint keymap, uint key);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_keymap_new_from_string")]
        internal static partial nint XkbKeymapNewFromString(nint context, nint keymapString, int format, uint flags);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_keymap_unref")]
        internal static partial void XkbKeymapUnref(nint keymap);
    }
}
