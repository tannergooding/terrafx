// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TerraFX.Platform;

[SuppressMessage(
    "Reliability",
    "CA2216:Disposable types should declare finalizer",
    Justification = "The keymap belongs to the Wayland keyboard owner thread.")]
internal sealed unsafe partial class WaylandKeyboardKeymap : IDisposable
{
    private const uint XkbKeymapFormatTextV1 = 1;
    private static readonly UTF8Encoding s_utf8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly XkbComposeState? _composeState;
    private readonly nint _context;
    private bool _isDisposed;
    private readonly nint _keymap;
    private readonly nint _state;

    private WaylandKeyboardKeymap(nint context, nint keymap, nint state, XkbComposeState? composeState)
    {
        _context = context;
        _keymap = keymap;
        _state = state;
        _composeState = composeState;
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
        nint state = 0;
        XkbComposeState? composeState = null;

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

            state = Native.XkbStateNew(keymap);

            if (state == 0)
            {
                throw new InvalidOperationException("libxkbcommon could not create a Wayland keyboard state.");
            }

            composeState = XkbComposeState.TryCreate();
            var result = new WaylandKeyboardKeymap(context, keymap, state, composeState);
            context = 0;
            keymap = 0;
            state = 0;
            composeState = null;
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

            if (state != 0)
            {
                Native.XkbStateUnref(state);
            }

            composeState?.Dispose();

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

        _composeState?.Dispose();
        Native.XkbStateUnref(_state);
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

    internal void ResetComposeState()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _composeState?.Reset();
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

    internal string? UpdateKey(uint evdevKeycode, bool isDown)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (evdevKeycode is > 0x2FF)
        {
            return null;
        }

        var keycode = checked(evdevKeycode + 8);
        _ = Native.XkbStateUpdateKey(_state, keycode, isDown ? 1u : 0u);

        if (!isDown)
        {
            return null;
        }

        var keysym = Native.XkbStateKeyGetOneSym(_state, keycode);

        if (_composeState is not XkbComposeState composeState)
        {
            return ReadKeyText(keycode);
        }

        var status = composeState.Feed(keysym, out var committedText);

        return status switch {
            XkbComposeStatus.Composing => null,
            XkbComposeStatus.Committed => committedText,
            XkbComposeStatus.Cancelled => ReadKeyText(keycode),
            _ => ReadKeyText(keycode),
        };
    }

    internal void UpdateModifiers(uint depressed, uint latched, uint locked, uint group)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _ = Native.XkbStateUpdateMask(_state, depressed, latched, locked, group, 0, 0);
    }

    private string? ReadKeyText(uint keycode)
    {
        var byteCount = Native.XkbStateKeyGetUtf8(_state, keycode, buffer: null, size: 0);

        if (byteCount <= 0)
        {
            return null;
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteCount, 4096);
        Span<byte> bytes = stackalloc byte[byteCount + 1];
        int written;

        fixed (byte* text = bytes)
        {
            written = Native.XkbStateKeyGetUtf8(_state, keycode, text, checked((nuint)bytes.Length));
        }

        if (written != byteCount)
        {
            throw new InvalidOperationException("libxkbcommon returned an inconsistent keyboard string length.");
        }

        return s_utf8Encoding.GetString(bytes[..written]);
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

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_state_key_get_one_sym")]
        internal static partial uint XkbStateKeyGetOneSym(nint state, uint key);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_state_key_get_utf8")]
        internal static partial int XkbStateKeyGetUtf8(nint state, uint key, byte* buffer, nuint size);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_state_new")]
        internal static partial nint XkbStateNew(nint keymap);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_state_unref")]
        internal static partial void XkbStateUnref(nint state);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_state_update_key")]
        internal static partial uint XkbStateUpdateKey(nint state, uint key, uint direction);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_state_update_mask")]
        internal static partial uint XkbStateUpdateMask(
            nint state,
            uint depressedModifiers,
            uint latchedModifiers,
            uint lockedModifiers,
            uint depressedLayout,
            uint latchedLayout,
            uint lockedLayout);
    }
}
