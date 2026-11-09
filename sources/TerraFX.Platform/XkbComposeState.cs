// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace TerraFX.Platform;

[SuppressMessage(
    "Reliability",
    "CA2216:Disposable types should declare finalizer",
    Justification = "The compose state belongs to the Wayland keyboard owner thread.")]
internal sealed unsafe partial class XkbComposeState : IDisposable
{
    private const int XkbComposeFeedAccepted = 1;
    private const int XkbComposeStatusCancelled = 3;
    private const int XkbComposeStatusComposed = 2;
    private const int XkbComposeStatusComposing = 1;
    private static readonly UTF8Encoding s_utf8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly nint _composeState;
    private readonly nint _composeTable;
    private nint _context;
    private bool _isDisposed;

    private XkbComposeState(nint context, nint composeTable, nint composeState)
    {
        _context = context;
        _composeTable = composeTable;
        _composeState = composeState;
    }

    internal static XkbComposeState? TryCreate()
    {
        var context = Native.XkbContextNew(flags: 0);

        if (context == 0)
        {
            throw new InvalidOperationException("libxkbcommon could not create a compose context.");
        }

        nint composeTable = 0;
        nint composeState = 0;

        try
        {
            foreach (var locale in GetLocales())
            {
                var localeBytes = Encoding.UTF8.GetBytes(string.Concat(locale, '\0'));

                fixed (byte* localeText = localeBytes)
                {
                    composeTable = Native.XkbComposeTableNewFromLocale(context, (nint)localeText, flags: 0);
                }

                if (composeTable != 0)
                {
                    break;
                }
            }

            if (composeTable == 0)
            {
                return null;
            }

            composeState = Native.XkbComposeStateNew(composeTable, flags: 0);

            if (composeState == 0)
            {
                return null;
            }

            var result = new XkbComposeState(context, composeTable, composeState);
            context = 0;
            composeTable = 0;
            composeState = 0;
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
            if (composeState != 0)
            {
                Native.XkbComposeStateUnref(composeState);
            }

            if (composeTable != 0)
            {
                Native.XkbComposeTableUnref(composeTable);
            }

            if (context != 0)
            {
                Native.XkbContextUnref(context);
            }
        }
    }

    private static string[] GetLocales()
    {
        var locale = Environment.GetEnvironmentVariable("LC_ALL");

        if (string.IsNullOrWhiteSpace(locale))
        {
            locale = Environment.GetEnvironmentVariable("LC_CTYPE");
        }

        if (string.IsNullOrWhiteSpace(locale))
        {
            locale = Environment.GetEnvironmentVariable("LANG");
        }

        if (string.IsNullOrWhiteSpace(locale))
        {
            locale = "C.UTF-8";
        }

        return string.Equals(locale, "C.UTF-8", StringComparison.Ordinal)
            ? [locale]
            : [locale, "C.UTF-8"];
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Native.XkbComposeStateUnref(_composeState);
        Native.XkbComposeTableUnref(_composeTable);
        Native.XkbContextUnref(_context);
        _context = 0;
        _isDisposed = true;
    }

    internal XkbComposeStatus Feed(uint keysym, out string? committedText)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        committedText = null;

        if (Native.XkbComposeStateFeed(_composeState, keysym) != XkbComposeFeedAccepted)
        {
            return XkbComposeStatus.Ignored;
        }

        switch (Native.XkbComposeStateGetStatus(_composeState))
        {
            case XkbComposeStatusComposing:
            {
                return XkbComposeStatus.Composing;
            }

            case XkbComposeStatusComposed:
            {
                committedText = ReadComposeText();
                Native.XkbComposeStateReset(_composeState);
                return XkbComposeStatus.Committed;
            }

            case XkbComposeStatusCancelled:
            {
                Native.XkbComposeStateReset(_composeState);
                return XkbComposeStatus.Cancelled;
            }

            default:
            {
                return XkbComposeStatus.Ignored;
            }
        }
    }

    internal void Reset()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        Native.XkbComposeStateReset(_composeState);
    }

    private string? ReadComposeText()
    {
        var byteCount = Native.XkbComposeStateGetUtf8(_composeState, buffer: null, size: 0);

        if (byteCount <= 0)
        {
            return null;
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteCount, 4096);
        Span<byte> bytes = stackalloc byte[byteCount + 1];
        int written;

        fixed (byte* text = bytes)
        {
            written = Native.XkbComposeStateGetUtf8(_composeState, text, checked((nuint)bytes.Length));
        }

        if (written != byteCount)
        {
            throw new InvalidOperationException("libxkbcommon returned an inconsistent compose string length.");
        }

        return s_utf8Encoding.GetString(bytes[..written]);
    }

    private static partial class Native
    {
        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_compose_state_feed")]
        internal static partial int XkbComposeStateFeed(nint composeState, uint keysym);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_compose_state_get_status")]
        internal static partial int XkbComposeStateGetStatus(nint composeState);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_compose_state_get_utf8")]
        internal static partial int XkbComposeStateGetUtf8(nint composeState, byte* buffer, nuint size);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_compose_state_new")]
        internal static partial nint XkbComposeStateNew(nint composeTable, uint flags);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_compose_state_reset")]
        internal static partial void XkbComposeStateReset(nint composeState);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_compose_state_unref")]
        internal static partial void XkbComposeStateUnref(nint composeState);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_compose_table_new_from_locale")]
        internal static partial nint XkbComposeTableNewFromLocale(nint context, nint locale, uint flags);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_compose_table_unref")]
        internal static partial void XkbComposeTableUnref(nint composeTable);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_context_new")]
        internal static partial nint XkbContextNew(uint flags);

        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_context_unref")]
        internal static partial void XkbContextUnref(nint context);
    }
}
