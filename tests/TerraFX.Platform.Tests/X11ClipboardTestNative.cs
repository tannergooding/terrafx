// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TerraFX.Platform.Tests;

internal static unsafe partial class X11ClipboardTestNative
{
    private const int SelectionNotifyEventType = 31;
    private const int SelectionRequestEventType = 30;

    internal static void CloseDisplay(nint display)
    {
        _ = XCloseDisplay(display);
    }

    internal static int ConvertSelection(
        nint display,
        nuint selection,
        nuint target,
        nuint property,
        nuint requestor)
        => XConvertSelection(display, selection, target, property, requestor, time: 0);

    internal static nuint CreateWindow(nint display)
    {
        var rootWindow = XRootWindow(display, XDefaultScreen(display));
        var window = XCreateSimpleWindow(
            display,
            rootWindow,
            x: 0,
            y: 0,
            width: 1,
            height: 1,
            borderWidth: 0,
            border: 0,
            background: 0);

        if (window == 0)
        {
            throw new InvalidOperationException("The X11 clipboard test could not create an owner window.");
        }

        return window;
    }

    internal static void DestroyWindow(nint display, nuint window)
    {
        _ = XDestroyWindow(display, window);
    }

    internal static void Flush(nint display)
    {
        _ = XFlush(display);
    }

    internal static void Free(nint value)
    {
        if (value != 0)
        {
            _ = XFree(value);
        }
    }

    internal static nuint GetSelectionOwner(nint display, nuint selection)
        => XGetSelectionOwner(display, selection);

    internal static nuint[] GetUnsignedLongs(nint value, int count)
    {
        if ((count < 0) || ((count != 0) && (value == 0)))
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        return (count == 0)
            ? []
            : new ReadOnlySpan<nuint>((void*)value, count).ToArray();
    }

    internal static nuint InternAtom(nint display, string name)
        => XInternAtom(display, name, onlyIfExists: 0);

    internal static bool NextSelectionRequest(nint display, out SelectionRequestEvent selectionRequest)
    {
        for (var index = 0; index < 2000; index++)
        {
            if (XPending(display) != 0)
            {
                var nativeEvent = default(NativeEvent);

                if (XNextEvent(display, &nativeEvent) != 0)
                {
                    throw new InvalidOperationException("The X11 clipboard test could not read an owner event.");
                }

                if (nativeEvent.Type == SelectionRequestEventType)
                {
                    selectionRequest = nativeEvent.SelectionRequest;
                    return true;
                }
            }

            Thread.Sleep(1);
        }

        selectionRequest = default;
        return false;
    }

    internal static nint OpenDisplay(string? displayName)
        => XOpenDisplay(displayName);

    internal static NativeProperty ReadProperty(nint display, nuint window, nuint property, bool delete)
    {
        var actualType = (nuint)0;
        var actualFormat = 0;
        var itemCount = (nuint)0;
        var bytesAfter = (nuint)0;
        nint value = 0;
        var status = XGetWindowProperty(
            display,
            window,
            property,
            longOffset: 0,
            longLength: 64,
            delete: delete ? 1 : 0,
            requestedType: 0,
            &actualType,
            &actualFormat,
            &itemCount,
            &bytesAfter,
            &value);
        return new NativeProperty(status, actualType, actualFormat, itemCount, bytesAfter, value);
    }

    internal static bool SendSelectionNotify(nint display, SelectionRequestEvent selectionRequest, nuint property)
    {
        var nativeEvent = new NativeEvent {
            SelectionNotify = new SelectionNotifyEvent {
                Type = SelectionNotifyEventType,
                Display = display,
                Requestor = selectionRequest.Requestor,
                Selection = selectionRequest.Selection,
                Target = selectionRequest.Target,
                Property = property,
                Time = selectionRequest.Time,
            },
        };
        var sent = XSendEvent(display, selectionRequest.Requestor, propagate: 0, eventMask: 0, &nativeEvent) != 0;
        _ = XFlush(display);
        return sent;
    }

    internal static void SetMalformedProperty(
        nint display,
        SelectionRequestEvent selectionRequest,
        nuint incrAtom,
        bool malformedIncrementalHeader)
    {
        int status;

        if (malformedIncrementalHeader)
        {
            byte value = 1;
            status = XChangeProperty(
                display,
                selectionRequest.Requestor,
                selectionRequest.Property,
                incrAtom,
                format: 8,
                mode: 0,
                &value,
                elementCount: 1);
        }
        else
        {
            nuint value = 0x4142;
            status = XChangeProperty(
                display,
                selectionRequest.Requestor,
                selectionRequest.Property,
                selectionRequest.Target,
                format: 32,
                mode: 0,
                (byte*)&value,
                elementCount: 1);
        }

        if (status == 0)
        {
            throw new InvalidOperationException("The X11 clipboard test could not write a malformed selection property.");
        }
    }

    internal static void SetSelectionOwner(nint display, nuint selection, nuint owner)
    {
        _ = XSetSelectionOwner(display, selection, owner, time: 0);
        _ = XSync(display, discard: 0);
    }

    internal static void Sync(nint display)
    {
        _ = XSync(display, discard: 0);
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XChangeProperty")]
    private static partial int XChangeProperty(
        nint display,
        nuint window,
        nuint property,
        nuint propertyType,
        int format,
        int mode,
        byte* data,
        int elementCount);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    private static partial int XCloseDisplay(nint display);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XConvertSelection")]
    private static partial int XConvertSelection(
        nint display,
        nuint selection,
        nuint target,
        nuint property,
        nuint requestor,
        nuint time);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XCreateSimpleWindow")]
    private static partial nuint XCreateSimpleWindow(
        nint display,
        nuint parent,
        int x,
        int y,
        uint width,
        uint height,
        uint borderWidth,
        nuint border,
        nuint background);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XDefaultScreen")]
    private static partial int XDefaultScreen(nint display);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XDestroyWindow")]
    private static partial int XDestroyWindow(nint display, nuint window);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XFlush")]
    private static partial int XFlush(nint display);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XFree")]
    private static partial int XFree(nint data);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XGetSelectionOwner")]
    private static partial nuint XGetSelectionOwner(nint display, nuint selection);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XGetWindowProperty")]
    private static partial int XGetWindowProperty(
        nint display,
        nuint window,
        nuint property,
        nint longOffset,
        nint longLength,
        int delete,
        nuint requestedType,
        nuint* actualType,
        int* actualFormat,
        nuint* itemCount,
        nuint* bytesAfter,
        nint* propertyValue);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XInternAtom", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nuint XInternAtom(nint display, string name, int onlyIfExists);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XNextEvent")]
    private static partial int XNextEvent(nint display, NativeEvent* nativeEvent);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XOpenDisplay", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint XOpenDisplay(string? displayName);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XPending")]
    private static partial int XPending(nint display);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XRootWindow")]
    private static partial nuint XRootWindow(nint display, int screen);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XSendEvent")]
    private static partial int XSendEvent(
        nint display,
        nuint window,
        int propagate,
        nint eventMask,
        NativeEvent* nativeEvent);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XSetSelectionOwner")]
    private static partial int XSetSelectionOwner(nint display, nuint selection, nuint owner, nuint time);

    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    [LibraryImport("libX11.so.6", EntryPoint = "XSync")]
    private static partial int XSync(nint display, int discard);

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    internal struct NativeEvent
    {
        [FieldOffset(0)]
        internal int Type;

        [FieldOffset(0)]
        internal SelectionNotifyEvent SelectionNotify;

        [FieldOffset(0)]
        internal SelectionRequestEvent SelectionRequest;
    }

    internal readonly record struct NativeProperty(
        int Status,
        nuint Type,
        int Format,
        nuint ItemCount,
        nuint BytesAfter,
        nint Value);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SelectionNotifyEvent
    {
        internal int Type;
        internal nuint Serial;
        internal int SendEvent;
        internal nint Display;
        internal nuint Requestor;
        internal nuint Selection;
        internal nuint Target;
        internal nuint Property;
        internal nuint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SelectionRequestEvent
    {
        internal int Type;
        internal nuint Serial;
        internal int SendEvent;
        internal nint Display;
        internal nuint Owner;
        internal nuint Requestor;
        internal nuint Selection;
        internal nuint Target;
        internal nuint Property;
        internal nuint Time;
    }
}
