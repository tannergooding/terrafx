// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using TerraFX;
using ClipboardTextResult = TerraFX.Platform.ClipboardStatus;

namespace TerraFX.Platform;

internal sealed unsafe partial class X11PlatformBackend
{
    internal static ClipboardReadResponse DecodeClipboardText(
        WindowHandle window,
        int maximumUtf8Bytes,
        byte[] bytes)
        => ClipboardDecodeText(window, maximumUtf8Bytes, bytes);

    private static ClipboardReadResponse ClipboardCreateReadResponse(
        WindowHandle window,
        ClipboardStatus status,
        string? text,
        string message)
        => new(
            status,
            text,
            ClipboardTextService.CreateDiagnostic(
                $"X11Clipboard{status}",
                window,
                "ClipboardText.ReadTextAsync",
                nativeResult: null,
                message));

    private static ClipboardReadResponse ClipboardDecodeText(
        WindowHandle window,
        int maximumUtf8Bytes,
        byte[] bytes)
    {
        if (bytes.Length > maximumUtf8Bytes)
        {
            return ClipboardCreateReadResponse(
                window,
                ClipboardTextResult.TooLarge,
                text: null,
                "The complete X11 clipboard text exceeds the request byte limit.");
        }

        try
        {
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
            return new ClipboardReadResponse(ClipboardTextResult.Completed, text, diagnostic: null);
        }
        catch (DecoderFallbackException)
        {
            return ClipboardCreateReadResponse(
                window,
                ClipboardTextResult.InvalidData,
                text: null,
                "The X11 clipboard selection contains invalid UTF-8.");
        }
    }

    public void BeginReadClipboardText(
        ClipboardReadRequest request,
        Action<ClipboardReadResponse> complete,
        CancellationToken cancellationToken)
    {
        EnsureActiveOwnerThread();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(complete);

        if (cancellationToken.IsCancellationRequested)
        {
            complete(new ClipboardReadResponse(ClipboardTextResult.Canceled, text: null, diagnostic: null));
            return;
        }

        if (_clipboardStatus is not ClipboardServiceStatus.Ready)
        {
            complete(ClipboardCreateReadResponse(
                request.Window,
                ClipboardTextResult.Unsupported,
                text: null,
                "The X11 clipboard service was not requested."));
            return;
        }

        if (!TryGetNativeWindowBinding(request.Window, out var binding))
        {
            complete(ClipboardCreateReadResponse(
                request.Window,
                ClipboardTextResult.SelectionExpired,
                text: null,
                "The target window is no longer active."));
            return;
        }

        if (_clipboard.PendingReads.Count >= X11ClipboardState.MaximumPendingReads)
        {
            complete(ClipboardCreateReadResponse(
                request.Window,
                ClipboardTextResult.TimedOut,
                text: null,
                "The bounded X11 clipboard read queue is full."));
            return;
        }

        var selectionOwner = X11Native.XGetSelectionOwner(_display, _clipboard.SelectionAtom);

        if (selectionOwner == 0)
        {
            complete(new ClipboardReadResponse(ClipboardTextResult.NoSelection, text: null, diagnostic: null));
            return;
        }

        var propertyName = $"_TERRAFX_CLIPBOARD_{_ownerId:X8}_{checked(++_clipboard.NextPropertyId):X8}";
        var property = X11Native.XInternAtom(_display, propertyName, onlyIfExists: 0);

        if (property == 0)
        {
            throw new InvalidOperationException("X11 failed to allocate a clipboard transfer property.");
        }

        var transfer = new X11ClipboardState.ReadTransfer(
            request,
            checked((nuint)binding.NativeWindowOrSurface),
            property,
            selectionOwner,
            _clipboard.Utf8StringAtom,
            complete,
            Stopwatch.GetTimestamp(),
            cancellationToken);
        _clipboard.PendingReads.Add(property, transfer);

        if (X11Native.XConvertSelection(
            _display,
            _clipboard.SelectionAtom,
            transfer.Target,
            property,
            transfer.Requestor,
            time: 0) == 0)
        {
            ClipboardCompleteRead(
                transfer,
                ClipboardCreateReadResponse(
                    request.Window,
                    ClipboardTextResult.PermissionDenied,
                    text: null,
                    "X11 refused the clipboard selection conversion request."),
                deleteProperty: true);
            return;
        }

        _ = X11Native.XFlush(_display);
    }

    public ClipboardWriteResponse WriteClipboardText(ClipboardWriteRequest request)
    {
        EnsureActiveOwnerThread();
        ArgumentNullException.ThrowIfNull(request);

        if (_clipboardStatus is not ClipboardServiceStatus.Ready)
        {
            return new ClipboardWriteResponse(
                ClipboardTextResult.Unsupported,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardUnsupported",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "The X11 clipboard service was not requested."));
        }

        if (!TryGetNativeWindowBinding(request.Window, out var binding))
        {
            return new ClipboardWriteResponse(
                ClipboardTextResult.SelectionExpired,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardWindowUnavailable",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "The target window is no longer active."));
        }

        byte[] textBytes;

        try
        {
            textBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetBytes(request.Text);
        }
        catch (EncoderFallbackException)
        {
            return new ClipboardWriteResponse(
                ClipboardTextResult.InvalidData,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardInvalidData",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "The requested clipboard text contains an unpaired UTF-16 surrogate."));
        }

        if (textBytes.Length > request.MaximumUtf8Bytes)
        {
            return new ClipboardWriteResponse(
                ClipboardTextResult.TooLarge,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardTooLarge",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "The complete clipboard text exceeds the request byte limit."));
        }

        var nativeWindow = checked((nuint)binding.NativeWindowOrSurface);

        if (X11Native.XSetSelectionOwner(_display, _clipboard.SelectionAtom, nativeWindow, time: 0) == 0)
        {
            return new ClipboardWriteResponse(
                ClipboardTextResult.PermissionDenied,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardOwnershipRefused",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "X11 refused the clipboard ownership request."));
        }

        _ = X11Native.XSync(_display, discard: 0);

        if (X11Native.XGetSelectionOwner(_display, _clipboard.SelectionAtom) != nativeWindow)
        {
            return new ClipboardWriteResponse(
                ClipboardTextResult.PermissionDenied,
                ClipboardTextService.CreateDiagnostic(
                    "ClipboardOwnershipRefused",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "The X11 server did not assign clipboard selection ownership to the target window."));
        }

        _clipboard.OwnedWindow = nativeWindow;
        _clipboard.OwnedTextBytes = textBytes;
        return new ClipboardWriteResponse(ClipboardTextResult.Completed, diagnostic: null);
    }

    private void CancelClipboardReads(ClipboardStatus status)
    {
        var transfers = new X11ClipboardState.ReadTransfer[_clipboard.PendingReads.Count];
        _clipboard.PendingReads.Values.CopyTo(transfers, 0);

        foreach (var transfer in transfers)
        {
            ClipboardCompleteRead(
                transfer,
                transfer.ResponseCompleted
                    ? null
                    : ClipboardCreateReadResponse(
                        transfer.Request.Window,
                        status,
                        text: null,
                        "The X11 clipboard transfer ended before a complete selection was received."),
                deleteProperty: true);
        }
    }

    private void CancelClipboardReadsForWindow(WindowHandle window, ClipboardStatus status)
    {
        var transfers = new List<X11ClipboardState.ReadTransfer>();

        foreach (var transfer in _clipboard.PendingReads.Values)
        {
            if (transfer.Request.Window == window)
            {
                transfers.Add(transfer);
            }
        }

        foreach (var transfer in transfers)
        {
            ClipboardCompleteRead(
                transfer,
                transfer.ResponseCompleted
                    ? null
                    : ClipboardCreateReadResponse(
                        window,
                        status,
                        text: null,
                        "The clipboard transfer window closed before the selection completed."),
                deleteProperty: true);
        }
    }

    private void CancelClipboardWrites()
    {
        var keys = new (nuint Requestor, nuint Property)[_clipboard.PendingWrites.Count];
        _clipboard.PendingWrites.Keys.CopyTo(keys, 0);

        foreach (var key in keys)
        {
            ClipboardRemoveWrite(key, releaseRequestorEvents: false);
        }

        _clipboard.PendingRequestorReleases.Clear();
        _clipboard.PendingWriteAdvances.Clear();
        _clipboard.SelectedRequestors.Clear();
    }

    private void CancelClipboardWritesForWindow(nuint window)
    {
        var keys = new List<(nuint Requestor, nuint Property)>();

        foreach (var entry in _clipboard.PendingWrites)
        {
            if (entry.Value.OwnerWindow == window)
            {
                keys.Add(entry.Key);
            }
        }

        foreach (var key in keys)
        {
            ClipboardRemoveWrite(key, releaseRequestorEvents: false);
        }
    }

    private void ClipboardAdvanceWrites()
    {
        if (_clipboard.PendingWriteAdvances.Count == 0)
        {
            return;
        }

        var keys = new (nuint Requestor, nuint Property)[_clipboard.PendingWriteAdvances.Count];
        _clipboard.PendingWriteAdvances.CopyTo(keys);
        _clipboard.PendingWriteAdvances.Clear();

        foreach (var key in keys)
        {
            if (_clipboard.PendingWrites.TryGetValue(key, out var transfer))
            {
                ClipboardWriteNextChunk(key.Requestor, key.Property, transfer);
            }
        }
    }

    private void ClipboardCompleteRead(
        X11ClipboardState.ReadTransfer transfer,
        ClipboardReadResponse? response,
        bool deleteProperty)
    {
        _ = _clipboard.PendingReads.Remove(transfer.Property);

        if (deleteProperty)
        {
            _ = X11Native.XDeleteProperty(_display, transfer.Requestor, transfer.Property);
        }

        transfer.Payload.Dispose();

        if (response is not null)
        {
            transfer.Complete(response);
        }
    }

    private void ClipboardExpireReads()
    {
        var now = Stopwatch.GetTimestamp();
        var expired = new List<X11ClipboardState.ReadTransfer>();

        foreach (var transfer in _clipboard.PendingReads.Values)
        {
            if (transfer.CancellationToken.IsCancellationRequested
                || !ClipboardIsSelectionOwner(transfer)
                || (Stopwatch.GetElapsedTime(transfer.StartTimestamp, now).TotalMilliseconds
                    >= X11ClipboardState.TransferTimeoutMilliseconds))
            {
                expired.Add(transfer);
            }
        }

        foreach (var transfer in expired)
        {
            var status = transfer.CancellationToken.IsCancellationRequested
                ? ClipboardTextResult.Canceled
                : !ClipboardIsSelectionOwner(transfer)
                    ? ClipboardTextResult.SelectionExpired
                    : ClipboardTextResult.TimedOut;

            ClipboardCompleteRead(
                transfer,
                transfer.ResponseCompleted
                    ? null
                    : ClipboardCreateReadResponse(
                        transfer.Request.Window,
                        status,
                        text: null,
                        status switch {
                            ClipboardTextResult.Canceled => "The X11 clipboard transfer was canceled.",
                            ClipboardTextResult.SelectionExpired => "The X11 clipboard selection owner changed during the transfer.",
                            _ => "The X11 clipboard selection did not complete within its bounded transfer interval.",
                        }),
                deleteProperty: true);
        }
    }

    private void ClipboardExpireWrites()
    {
        var now = Stopwatch.GetTimestamp();
        var expired = new List<(nuint Requestor, nuint Property)>();

        foreach (var transfer in _clipboard.PendingWrites)
        {
            if (Stopwatch.GetElapsedTime(transfer.Value.StartTimestamp, now).TotalMilliseconds
                >= X11ClipboardState.TransferTimeoutMilliseconds)
            {
                expired.Add(transfer.Key);
            }
        }

        foreach (var key in expired)
        {
            ClipboardRemoveWrite(key, releaseRequestorEvents: false);
        }
    }

    private X11ClipboardState.ReadTransfer? ClipboardFindRead(
        nuint requestor,
        nuint selection,
        nuint target,
        nuint property)
    {
        if (property != 0)
        {
            return _clipboard.PendingReads.TryGetValue(property, out var transfer)
                && (transfer.Requestor == requestor)
                && (selection == _clipboard.SelectionAtom)
                && (target == transfer.Target)
                    ? transfer
                    : null;
        }

        foreach (var transfer in _clipboard.PendingReads.Values)
        {
            if ((transfer.Requestor == requestor)
                && (selection == _clipboard.SelectionAtom)
                && (target == transfer.Target))
            {
                return transfer;
            }
        }

        return null;
    }

    private bool ClipboardIsSelectionOwner(X11ClipboardState.ReadTransfer transfer)
        => X11Native.XGetSelectionOwner(_display, _clipboard.SelectionAtom) == transfer.SelectionOwner;

    private bool ClipboardIsTextTarget(nuint target)
        => (target == _clipboard.Utf8StringAtom) || (target == _clipboard.TextPlainUtf8Atom);

    private void ClipboardPropertyChanged(X11Native.PropertyEvent propertyEvent)
    {
        if (propertyEvent.State == X11Native.PropertyDelete)
        {
            if (_clipboard.PendingWrites.TryGetValue(
                (propertyEvent.Window, propertyEvent.Atom),
                out _))
            {
                _ = _clipboard.PendingWriteAdvances.Add((propertyEvent.Window, propertyEvent.Atom));
            }
            else if (_clipboard.SelectedRequestors.Contains(propertyEvent.Window))
            {
                _ = _clipboard.PendingRequestorReleases.Add(propertyEvent.Window);
            }

            return;
        }

        if ((propertyEvent.State != X11Native.PropertyNewValue)
            || !_clipboard.PendingReads.TryGetValue(propertyEvent.Atom, out var transfer)
            || (transfer.Requestor != propertyEvent.Window)
            || !transfer.IsIncremental)
        {
            return;
        }

        if (!ClipboardIsSelectionOwner(transfer))
        {
            ClipboardCompleteRead(
                transfer,
                transfer.ResponseCompleted
                    ? null
                    : ClipboardCreateReadResponse(
                        transfer.Request.Window,
                        ClipboardTextResult.SelectionExpired,
                        text: null,
                        "The X11 clipboard selection owner changed during the transfer."),
                deleteProperty: true);
            return;
        }

        var actualType = (nuint)0;
        var actualFormat = 0;
        var itemCount = (nuint)0;
        var bytesAfter = (nuint)0;
        nint propertyValue = 0;
        var status = X11Native.XGetWindowProperty(
            _display,
            transfer.Requestor,
            transfer.Property,
            longOffset: 0,
            longLength: checked((nint)(((transfer.Request.MaximumUtf8Bytes + 3L) / 4L) + 1)),
            delete: 1,
            requestedType: 0,
            &actualType,
            &actualFormat,
            &itemCount,
            &bytesAfter,
            &propertyValue);

        try
        {
            if (status != 0)
            {
                ClipboardCompleteRead(
                    transfer,
                    transfer.ResponseCompleted
                        ? null
                        : ClipboardCreateReadResponse(
                            transfer.Request.Window,
                            ClipboardTextResult.InvalidData,
                            text: null,
                            "X11 could not read a clipboard transfer chunk."),
                    deleteProperty: true);
                return;
            }

            if ((actualType == transfer.Target)
                && (actualFormat == 8)
                && (itemCount == 0)
                && (bytesAfter == 0))
            {
                var response = transfer.ResponseCompleted
                    ? null
                    : ClipboardDecodeText(
                        transfer.Request.Window,
                        transfer.Request.MaximumUtf8Bytes,
                        transfer.Payload.ToArray());
                ClipboardCompleteRead(transfer, response, deleteProperty: false);
                return;
            }

            if ((actualType != transfer.Target)
                || (actualFormat != 8)
                || (propertyValue == 0))
            {
                if (!transfer.ResponseCompleted)
                {
                    transfer.ResponseCompleted = true;
                    transfer.Complete(ClipboardCreateReadResponse(
                        transfer.Request.Window,
                        ClipboardTextResult.InvalidData,
                        text: null,
                        "The X11 clipboard owner returned an invalid UTF-8 transfer chunk."));
                }

                _ = X11Native.XDeleteProperty(_display, transfer.Requestor, transfer.Property);
                return;
            }

            if (bytesAfter != 0)
            {
                if (!transfer.ResponseCompleted)
                {
                    transfer.ResponseCompleted = true;
                    transfer.Complete(ClipboardCreateReadResponse(
                        transfer.Request.Window,
                        ClipboardTextResult.TooLarge,
                        text: null,
                        "The complete X11 clipboard transfer exceeds the request byte limit."));
                }

                _ = X11Native.XDeleteProperty(_display, transfer.Requestor, transfer.Property);
                return;
            }

            var chunkLength = checked((int)itemCount);

            if (!transfer.ResponseCompleted
                && (chunkLength <= (transfer.Request.MaximumUtf8Bytes - transfer.Payload.Length)))
            {
                transfer.Payload.Write(new ReadOnlySpan<byte>((void*)propertyValue, chunkLength));
            }
            else if (!transfer.ResponseCompleted)
            {
                transfer.ResponseCompleted = true;
                transfer.Complete(ClipboardCreateReadResponse(
                    transfer.Request.Window,
                    ClipboardTextResult.TooLarge,
                    text: null,
                    "The complete X11 clipboard transfer exceeds the request byte limit."));
            }
        }
        finally
        {
            if (propertyValue != 0)
            {
                _ = X11Native.XFree(propertyValue);
            }
        }
    }

    private void ClipboardReleaseRequestorEvents()
    {
        if (_clipboard.PendingRequestorReleases.Count == 0)
        {
            return;
        }

        var requestors = new nuint[_clipboard.PendingRequestorReleases.Count];
        _clipboard.PendingRequestorReleases.CopyTo(requestors);
        _clipboard.PendingRequestorReleases.Clear();

        foreach (var requestor in requestors)
        {
            if (!_clipboard.SelectedRequestors.Contains(requestor)
                || _windowsByNativeId.ContainsKey(requestor))
            {
                continue;
            }

            var hasPendingWrites = false;

            foreach (var (pendingRequestor, _) in _clipboard.PendingWrites.Keys)
            {
                if (pendingRequestor == requestor)
                {
                    hasPendingWrites = true;
                    break;
                }
            }

            if (hasPendingWrites)
            {
                continue;
            }

            if (X11Native.XSelectInput(_display, requestor, eventMask: 0) == 0)
            {
                throw new InvalidOperationException("X11 failed to release clipboard events from a live requestor.");
            }

            _ = _clipboard.SelectedRequestors.Remove(requestor);
        }
    }

    private void ClipboardRemoveWrite(
        (nuint Requestor, nuint Property) key,
        bool releaseRequestorEvents)
    {
        if (!_clipboard.PendingWrites.Remove(key))
        {
            return;
        }

        _ = _clipboard.PendingWriteAdvances.Remove(key);

        if (!releaseRequestorEvents || _windowsByNativeId.ContainsKey(key.Requestor))
        {
            return;
        }

        foreach (var (requestor, _) in _clipboard.PendingWrites.Keys)
        {
            if (requestor == key.Requestor)
            {
                return;
            }
        }

        _ = _clipboard.PendingRequestorReleases.Add(key.Requestor);
    }

    private void ClipboardRequestorDestroyed(nuint requestor)
    {
        _ = _clipboard.SelectedRequestors.Remove(requestor);
        _ = _clipboard.PendingRequestorReleases.Remove(requestor);
        var keys = new List<(nuint Requestor, nuint Property)>();

        foreach (var key in _clipboard.PendingWrites.Keys)
        {
            if (key.Requestor == requestor)
            {
                keys.Add(key);
            }
        }

        foreach (var key in keys)
        {
            ClipboardRemoveWrite(key, releaseRequestorEvents: false);
        }
    }

    private bool ClipboardRetryTextPlain(X11ClipboardState.ReadTransfer transfer)
    {
        if (transfer.Target != _clipboard.Utf8StringAtom)
        {
            return false;
        }

        transfer.Target = _clipboard.TextPlainUtf8Atom;

        if (X11Native.XConvertSelection(
            _display,
            _clipboard.SelectionAtom,
            transfer.Target,
            transfer.Property,
            transfer.Requestor,
            time: 0) == 0)
        {
            return false;
        }

        _ = X11Native.XFlush(_display);
        return true;
    }

    private void ClipboardSelectionCleared(X11Native.SelectionClearEvent selectionClear)
    {
        if ((selectionClear.Selection == _clipboard.SelectionAtom)
            && (selectionClear.Window == _clipboard.OwnedWindow))
        {
            _clipboard.OwnedWindow = 0;
            _clipboard.OwnedTextBytes = null;
        }
    }

    private void ClipboardSelectionNotified(X11Native.SelectionNotifyEvent selectionNotify)
    {
        var transfer = ClipboardFindRead(
            selectionNotify.Requestor,
            selectionNotify.Selection,
            selectionNotify.Target,
            selectionNotify.Property);

        if (transfer is null)
        {
            return;
        }

        if (!ClipboardIsSelectionOwner(transfer))
        {
            ClipboardCompleteRead(
                transfer,
                ClipboardCreateReadResponse(
                    transfer.Request.Window,
                    ClipboardTextResult.SelectionExpired,
                    text: null,
                    "The X11 clipboard selection owner changed before the transfer completed."),
                deleteProperty: true);
            return;
        }

        if (selectionNotify.Property == 0)
        {
            if (ClipboardRetryTextPlain(transfer))
            {
                return;
            }

            ClipboardCompleteRead(
                transfer,
                new ClipboardReadResponse(ClipboardTextResult.NoSelection, text: null, diagnostic: null),
                deleteProperty: false);
            return;
        }

        var actualType = (nuint)0;
        var actualFormat = 0;
        var itemCount = (nuint)0;
        var bytesAfter = (nuint)0;
        nint propertyValue = 0;
        var status = X11Native.XGetWindowProperty(
            _display,
            transfer.Requestor,
            transfer.Property,
            longOffset: 0,
            longLength: checked((nint)(((transfer.Request.MaximumUtf8Bytes + 3L) / 4L) + 1)),
            delete: 1,
            requestedType: 0,
            &actualType,
            &actualFormat,
            &itemCount,
            &bytesAfter,
            &propertyValue);

        try
        {
            if (status != 0)
            {
                ClipboardCompleteRead(
                    transfer,
                    ClipboardCreateReadResponse(
                        transfer.Request.Window,
                        ClipboardTextResult.InvalidData,
                        text: null,
                        "X11 could not read the clipboard selection property."),
                    deleteProperty: true);
                return;
            }

            if (actualType == _clipboard.IncrAtom)
            {
                if ((actualFormat != 32) || (itemCount != 1) || (bytesAfter != 0) || (propertyValue == 0))
                {
                    ClipboardCompleteRead(
                        transfer,
                        ClipboardCreateReadResponse(
                            transfer.Request.Window,
                            ClipboardTextResult.InvalidData,
                            text: null,
                            "The X11 clipboard owner returned a malformed INCR header."),
                        deleteProperty: true);
                    return;
                }

                transfer.IsIncremental = true;
                _ = X11Native.XDeleteProperty(_display, transfer.Requestor, transfer.Property);
                _ = X11Native.XFlush(_display);
                return;
            }

            if (!ClipboardIsTextTarget(actualType))
            {
                if (ClipboardRetryTextPlain(transfer))
                {
                    return;
                }

                ClipboardCompleteRead(
                    transfer,
                    ClipboardCreateReadResponse(
                        transfer.Request.Window,
                        ClipboardTextResult.NoSelection,
                        text: null,
                        "The X11 clipboard owner does not provide UTF8_STRING text."),
                    deleteProperty: true);
                return;
            }

            if (actualFormat != 8)
            {
                ClipboardCompleteRead(
                    transfer,
                    ClipboardCreateReadResponse(
                        transfer.Request.Window,
                        ClipboardTextResult.InvalidData,
                        text: null,
                        "The X11 clipboard owner returned text with a non-byte format."),
                    deleteProperty: true);
                return;
            }

            if ((bytesAfter != 0) || (itemCount > (nuint)transfer.Request.MaximumUtf8Bytes))
            {
                ClipboardCompleteRead(
                    transfer,
                    ClipboardCreateReadResponse(
                        transfer.Request.Window,
                        ClipboardTextResult.TooLarge,
                        text: null,
                        "The complete X11 clipboard selection exceeds the request byte limit."),
                    deleteProperty: true);
                return;
            }

            if ((itemCount != 0) && (propertyValue == 0))
            {
                throw new InvalidOperationException("X11 returned a clipboard property length without storage.");
            }

            var bytes = (itemCount == 0)
                ? []
                : new ReadOnlySpan<byte>((void*)propertyValue, checked((int)itemCount)).ToArray();
            ClipboardCompleteRead(
                transfer,
                ClipboardDecodeText(transfer.Request.Window, transfer.Request.MaximumUtf8Bytes, bytes),
                deleteProperty: true);
        }
        finally
        {
            if (propertyValue != 0)
            {
                _ = X11Native.XFree(propertyValue);
            }
        }
    }

    private void ClipboardSelectionRequested(X11Native.SelectionRequestEvent selectionRequest)
    {
        var property = (selectionRequest.Property == 0) ? selectionRequest.Target : selectionRequest.Property;
        var text = _clipboard.OwnedTextBytes;
        var propertyWritten = (selectionRequest.Selection == _clipboard.SelectionAtom)
            && (selectionRequest.Owner == _clipboard.OwnedWindow)
            && (X11Native.XGetSelectionOwner(_display, _clipboard.SelectionAtom) == _clipboard.OwnedWindow)
            && (text is not null);

        if (propertyWritten && (selectionRequest.Target == _clipboard.TargetsAtom))
        {
            var targets = stackalloc nuint[3] {
                _clipboard.TargetsAtom,
                _clipboard.Utf8StringAtom,
                _clipboard.TextPlainUtf8Atom,
            };
            propertyWritten = X11Native.XChangeProperty(
                _display,
                selectionRequest.Requestor,
                property,
                _atomAtom,
                format: 32,
                mode: 0,
                (byte*)targets,
                elementCount: 3) != 0;
        }
        else if (propertyWritten
            && ClipboardIsTextTarget(selectionRequest.Target)
            && (text is byte[] textBytes))
        {
            if (textBytes.Length <= X11ClipboardState.TransferChunkBytes)
            {
                fixed (byte* bytes = textBytes)
                {
                    propertyWritten = X11Native.XChangeProperty(
                        _display,
                        selectionRequest.Requestor,
                        property,
                        selectionRequest.Target,
                        format: 8,
                        mode: 0,
                        bytes,
                        textBytes.Length) != 0;
                }
            }
            else
            {
                var requestor = selectionRequest.Requestor;
                var requestorIsOwned = _windowsByNativeId.ContainsKey(selectionRequest.Requestor);
                var requestorAlreadySelected = _clipboard.SelectedRequestors.Contains(requestor);
                propertyWritten = !_clipboard.PendingWrites.ContainsKey((requestor, property))
                    && (_clipboard.PendingWrites.Count < X11ClipboardState.MaximumPendingReads)
                    && (requestorIsOwned
                        || requestorAlreadySelected
                        || (_clipboard.SelectedRequestors.Count < X11ClipboardState.MaximumSelectedRequestors));

                if (propertyWritten && !requestorIsOwned && !requestorAlreadySelected)
                {
                    propertyWritten = X11Native.XSelectInput(
                        _display,
                        requestor,
                        checked((nint)(PropertyChangeMask | StructureNotifyMask))) != 0;

                    if (propertyWritten)
                    {
                        _ = _clipboard.SelectedRequestors.Add(requestor);
                    }
                }

                if (propertyWritten)
                {
                    var byteCount = checked((nuint)textBytes.Length);
                    propertyWritten = X11Native.XChangeProperty(
                        _display,
                        requestor,
                        property,
                        _clipboard.IncrAtom,
                        format: 32,
                        mode: 0,
                        (byte*)&byteCount,
                        elementCount: 1) != 0;
                }

                if (propertyWritten)
                {
                    _clipboard.PendingWrites.Add(
                        (requestor, property),
                        new X11ClipboardState.WriteTransfer(
                            selectionRequest.Owner,
                            selectionRequest.Target,
                            textBytes,
                            Stopwatch.GetTimestamp()));
                }
                else if (!requestorIsOwned && _clipboard.SelectedRequestors.Contains(requestor))
                {
                    _ = _clipboard.PendingRequestorReleases.Add(requestor);
                }
            }
        }
        else
        {
            propertyWritten = false;
        }

        var notification = new X11Native.NativeEvent {
            SelectionNotify = new X11Native.SelectionNotifyEvent {
                Type = SelectionNotifyEvent,
                Display = _display,
                Requestor = selectionRequest.Requestor,
                Selection = selectionRequest.Selection,
                Target = selectionRequest.Target,
                Property = propertyWritten ? property : 0,
                Time = selectionRequest.Time,
            },
        };
        _ = X11Native.XSendEvent(_display, selectionRequest.Requestor, propagate: 0, eventMask: 0, &notification);
        _ = X11Native.XFlush(_display);
    }

    private void ClipboardWriteNextChunk(
        nuint requestor,
        nuint property,
        X11ClipboardState.WriteTransfer transfer)
    {
        var bytesRemaining = transfer.Payload.Length - transfer.Offset;

        if (bytesRemaining < 0)
        {
            throw new InvalidOperationException("The X11 clipboard write offset exceeds its retained payload.");
        }

        if (bytesRemaining == 0)
        {
            if (X11Native.XChangeProperty(
                _display,
                requestor,
                property,
                transfer.Target,
                format: 8,
                mode: 0,
                data: null,
                elementCount: 0) == 0)
            {
                ClipboardRemoveWrite((requestor, property), releaseRequestorEvents: false);
                return;
            }

            ClipboardRemoveWrite((requestor, property), releaseRequestorEvents: true);
            _ = X11Native.XFlush(_display);
            return;
        }

        var chunkLength = Math.Min(bytesRemaining, X11ClipboardState.TransferChunkBytes);

        fixed (byte* bytes = transfer.Payload)
        {
            if (X11Native.XChangeProperty(
                _display,
                requestor,
                property,
                transfer.Target,
                format: 8,
                mode: 0,
                bytes + transfer.Offset,
                chunkLength) == 0)
            {
                ClipboardRemoveWrite((requestor, property), releaseRequestorEvents: false);
                return;
            }
        }

        transfer.Offset = checked(transfer.Offset + chunkLength);
        _ = X11Native.XFlush(_display);
    }
}
