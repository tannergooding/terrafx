// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using ClipboardTextResult = TerraFX.Platform.ClipboardStatus;

namespace TerraFX.Platform;

internal sealed partial class WaylandPlatformBackend
{
    private const int ClipboardReadBufferBytes = 16 * 1024;
    private const int ClipboardTransferTimeoutMilliseconds = 5000;
    private const int FileDescriptorCloseOnExec = 0x80000;
    private const int MaximumClipboardDrainBytes = ClipboardTextService.MaximumUtf8Bytes * 2;

    private static readonly UTF8Encoding s_strictUtf8Encoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly string[] s_textMimeTypes = ["text/plain;charset=utf-8", "text/plain"];

    private readonly Dictionary<nint, WaylandClipboardDataOfferState> _clipboardDataOffers = [];
    private readonly Dictionary<nint, WaylandClipboardDataSourceState> _clipboardDataSources = [];
    private readonly List<WaylandClipboardReadTransfer> _clipboardReads = [];
    private readonly List<WaylandClipboardWriteTransfer> _clipboardWrites = [];
    private nint _dataDevice;
    private nint _dataDeviceListener;
    private nint _dataDeviceManager;
    private int _dataDeviceManagerVersion;
    private nint _dataOfferListener;
    private nint _dataSourceListener;
    private uint _lastClipboardGestureSerial;
    private WaylandClipboardDataOfferState? _selectedClipboardOffer;
    private nint _selectedClipboardSource;

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Asynchronous Wayland clipboard failures are delivered through the owning request's completion callback.")]
    internal static async Task<WaylandClipboardReadCompletion> ReadClipboardTransferAsync(
        WaylandClipboardReadTransfer transfer,
        Stream stream)
    {
        ClipboardReadResponse? response = null;
        Exception? failure = null;
        byte[]? buffer = null;

        try
        {
            buffer = ArrayPool<byte>.Shared.Rent(ClipboardReadBufferBytes);
            using var payload = new MemoryStream(Math.Min(transfer.Request.MaximumUtf8Bytes, ClipboardReadBufferBytes));
            var totalBytesRead = 0;
            var tooLarge = false;

            while (true)
            {
                var bytesRead = await stream.ReadAsync(
                    buffer.AsMemory(0, ClipboardReadBufferBytes),
                    transfer.CancellationToken).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    break;
                }

                totalBytesRead = checked(totalBytesRead + bytesRead);
                tooLarge |= totalBytesRead > transfer.Request.MaximumUtf8Bytes;

                if (!tooLarge)
                {
                    await payload.WriteAsync(
                        buffer.AsMemory(0, bytesRead),
                        CancellationToken.None).ConfigureAwait(false);
                }

                if (totalBytesRead > MaximumClipboardDrainBytes)
                {
                    tooLarge = true;
                    break;
                }
            }

            response = tooLarge
                ? CreateClipboardReadResponse(
                    transfer.Request.Window,
                    ClipboardTextResult.TooLarge,
                    "The complete Wayland clipboard text exceeds the request byte limit.")
                : ClipboardTextPayload.ReadUtf8(
                    transfer.Request.Window,
                    payload.ToArray(),
                    transfer.Request.MaximumUtf8Bytes);
        }
        catch (OperationCanceledException)
        {
            var status = transfer.TerminalStatus
                ?? (transfer.CallerCancellationToken.IsCancellationRequested
                    ? ClipboardTextResult.Canceled
                    : ClipboardTextResult.TimedOut);
            response = CreateClipboardReadResponse(
                transfer.Request.Window,
                status,
                status is ClipboardTextResult.TimedOut
                    ? "The Wayland clipboard data transfer timed out."
                    : "The Wayland clipboard data transfer was canceled.");
        }
        catch (IOException)
        {
            var status = transfer.TerminalStatus ?? ClipboardTextResult.SelectionExpired;
            response = CreateClipboardReadResponse(
                transfer.Request.Window,
                status,
                "The Wayland clipboard data source ended before the complete text transfer finished.");
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure ??= exception;
                response = null;
            }

            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            transfer.Dispose();
        }

        return new WaylandClipboardReadCompletion(transfer, response, failure);
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Asynchronous Wayland data-source failures are observed by the owner pump.")]
    internal static async Task<Exception?> WriteClipboardPayloadAsync(
        Stream stream,
        byte[] payload,
        CancellationTokenSource cancellationSource)
    {
        Exception? failure = null;

        try
        {
            await stream.WriteAsync(payload.AsMemory(), cancellationSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            try
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure ??= exception;
            }

            cancellationSource.Dispose();
        }

        return failure;
    }

    private static ClipboardReadResponse CreateClipboardReadResponse(
        WindowHandle window,
        ClipboardStatus status,
        string message)
        => new(
            status,
            text: null,
            ClipboardTextService.CreateDiagnostic(
                $"WaylandClipboard{status}",
                window,
                "ClipboardText.ReadTextAsync",
                nativeResult: null,
                message));

    private static ClipboardWriteResponse CreateClipboardWriteResponse(
        WindowHandle window,
        ClipboardStatus status,
        string message)
        => new(
            status,
            ClipboardTextService.CreateDiagnostic(
                $"WaylandClipboard{status}",
                window,
                "ClipboardText.WriteTextAsync",
                nativeResult: null,
                message));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnClipboardDataDeviceDataOffer(nint data, nint dataDevice, nint offer)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleClipboardDataDeviceDataOffer(dataDevice, offer);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnClipboardDataDeviceDrop(nint data, nint dataDevice)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleClipboardDataDeviceLeave(dataDevice);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnClipboardDataDeviceEnter(
        nint data,
        nint dataDevice,
        uint serial,
        nint surface,
        int x,
        int y,
        nint offer)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleClipboardDataDeviceEnter(dataDevice, serial, surface, offer);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }

        _ = x;
        _ = y;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnClipboardDataDeviceLeave(nint data, nint dataDevice)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleClipboardDataDeviceLeave(dataDevice);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnClipboardDataDeviceMotion(nint data, nint dataDevice, uint time, int x, int y)
    {
        _ = data;
        _ = dataDevice;
        _ = time;
        _ = x;
        _ = y;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnClipboardDataDeviceSelection(nint data, nint dataDevice, nint offer)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleClipboardDataDeviceSelection(dataDevice, offer);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnClipboardDataOfferAction(nint data, nint offer, uint action)
    {
        _ = data;
        _ = offer;
        _ = action;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnClipboardDataOfferMimeType(nint data, nint offer, nint mimeType)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleClipboardDataOfferMimeType(offer, mimeType);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnClipboardDataOfferSourceActions(nint data, nint offer, uint actions)
    {
        _ = data;
        _ = offer;
        _ = actions;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnClipboardDataSourceAction(nint data, nint source, uint action)
    {
        _ = data;
        _ = source;
        _ = action;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnClipboardDataSourceCancelled(nint data, nint source)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleClipboardDataSourceCancelled(source);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnClipboardDataSourceDndFinished(nint data, nint source)
    {
        _ = data;
        _ = source;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnClipboardDataSourceDropPerformed(nint data, nint source)
    {
        _ = data;
        _ = source;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Exceptions must not cross the Wayland unmanaged callback boundary.")]
    private static void OnClipboardDataSourceSend(nint data, nint source, nint mimeType, int fileDescriptor)
    {
        if (GCHandle.FromIntPtr(data).Target is WaylandPlatformBackend backend)
        {
            try
            {
                backend.HandleClipboardDataSourceSend(source, mimeType, fileDescriptor);
            }
            catch (Exception exception)
            {
                backend._callbackFailure ??= exception;
            }
        }
        else
        {
            using var handle = new SafeFileHandle((nint)fileDescriptor, ownsHandle: true);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnClipboardDataSourceTarget(nint data, nint source, nint mimeType)
    {
        _ = data;
        _ = source;
        _ = mimeType;
    }

    private static string? SelectClipboardMimeType(HashSet<string> mimeTypes)
    {
        foreach (var mimeType in s_textMimeTypes)
        {
            if (mimeTypes.Contains(mimeType))
            {
                return mimeType;
            }
        }

        return null;
    }

    [SuppressMessage(
        "Reliability",
        "CA2025",
        Justification = "The tracked clipboard read task owns and asynchronously disposes its pipe stream before the owner processes its completion.")]
    public unsafe void BeginReadClipboardText(
        ClipboardReadRequest request,
        Action<ClipboardReadResponse> complete,
        Action<Exception> fail,
        CancellationToken cancellationToken)
    {
        EnsureActiveOwnerThread();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(complete);
        ArgumentNullException.ThrowIfNull(fail);

        if (cancellationToken.IsCancellationRequested)
        {
            complete(new ClipboardReadResponse(ClipboardTextResult.Canceled, text: null, diagnostic: null));
            return;
        }

        if (_clipboardStatus is not ClipboardServiceStatus.Ready)
        {
            complete(CreateClipboardReadResponse(
                request.Window,
                ClipboardTextResult.Unsupported,
                "The Wayland compositor does not currently provide a data-device clipboard."));
            return;
        }

        if ((request.GestureToken is not UserGestureToken gestureToken)
            || (gestureToken.NativeInputSerial == 0)
            || (gestureToken.Window != request.Window))
        {
            complete(CreateClipboardReadResponse(
                request.Window,
                ClipboardTextResult.PermissionDenied,
                "The clipboard read does not have a current gesture for the focused window."));
            return;
        }

        if (!IsClipboardWindowFocused(request.Window)
            || (gestureToken.NativeInputSerial != _lastClipboardGestureSerial))
        {
            complete(CreateClipboardReadResponse(
                request.Window,
                ClipboardTextResult.SelectionExpired,
                "The clipboard gesture expired before the read began."));
            return;
        }

        if ((_selectedClipboardSource != 0)
            && _clipboardDataSources.TryGetValue(_selectedClipboardSource, out var ownedSource))
        {
            complete(ClipboardTextPayload.ReadUtf8(request.Window, ownedSource.Payload, request.MaximumUtf8Bytes));
            return;
        }

        var offer = _selectedClipboardOffer;

        if (offer is null)
        {
            complete(CreateClipboardReadResponse(
                request.Window,
                ClipboardTextResult.NoSelection,
                "The focused Wayland seat has no clipboard selection."));
            return;
        }

        var mimeType = SelectClipboardMimeType(offer.MimeTypes);

        if (mimeType is null)
        {
            complete(CreateClipboardReadResponse(
                request.Window,
                ClipboardTextResult.Unsupported,
                "The Wayland clipboard selection does not advertise a supported UTF-8 text MIME type."));
            return;
        }

        var fileDescriptors = stackalloc int[2];

        if (WaylandClipboardNative.Pipe2(fileDescriptors, FileDescriptorCloseOnExec) < 0)
        {
            var nativeResult = Marshal.GetLastPInvokeError();
            throw new IOException($"The Wayland clipboard pipe could not be created; native error {nativeResult}.");
        }

        SafeFileHandle? readHandle = new((nint)fileDescriptors[0], ownsHandle: true);
        SafeFileHandle? writeHandle = new((nint)fileDescriptors[1], ownsHandle: true);
        WaylandClipboardReadTransfer? transfer = null;
        FileStream? readStream = null;

        try
        {
            var mimePointer = Marshal.StringToCoTaskMemUTF8(mimeType);

            try
            {
                var receiveArguments = stackalloc WaylandArgument[2];
                receiveArguments[0].Pointer = mimePointer;
                receiveArguments[1].FileDescriptor = checked((int)(writeHandle
                    ?? throw new InvalidOperationException("The Wayland clipboard write pipe is unavailable."))
                    .DangerousGetHandle());
                Connection.SendRequest(offer.Proxy, opcode: 1, receiveArguments);
            }
            finally
            {
                Marshal.FreeCoTaskMem(mimePointer);
            }

            Connection.FlushRequests();
            writeHandle.Dispose();
            writeHandle = null;
            readStream = new FileStream(
                readHandle ?? throw new InvalidOperationException("The Wayland clipboard read pipe is unavailable."),
                FileAccess.Read,
                ClipboardReadBufferBytes,
                isAsync: true);
            readHandle = null;
            var timeoutSource = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(ClipboardTransferTimeoutMilliseconds),
                _timeProvider);
            transfer = new WaylandClipboardReadTransfer(
                request,
                offer,
                complete,
                fail,
                timeoutSource,
                cancellationToken);
            offer.ActiveReadCount++;
            _clipboardReads.Add(transfer);
            transfer.CompletionTask = ReadClipboardTransferAsync(transfer, readStream);
            readStream = null;
        }
        finally
        {
            readStream?.Dispose();
            writeHandle?.Dispose();
            readHandle?.Dispose();

            if ((transfer is not null) && !_clipboardReads.Contains(transfer))
            {
                transfer.Dispose();
            }
        }
    }

    public ClipboardReadResponse ReadClipboardText(ClipboardReadRequest request)
    {
        EnsureActiveOwnerThread();
        ArgumentNullException.ThrowIfNull(request);
        return CreateClipboardReadResponse(
            request.Window,
            ClipboardTextResult.Unsupported,
            "Wayland clipboard reads require the asynchronous owner-pumped transfer path.");
    }

    public unsafe ClipboardWriteResponse WriteClipboardText(ClipboardWriteRequest request)
    {
        EnsureActiveOwnerThread();
        ArgumentNullException.ThrowIfNull(request);

        if (_clipboardStatus is not ClipboardServiceStatus.Ready)
        {
            return CreateClipboardWriteResponse(
                request.Window,
                ClipboardTextResult.Unsupported,
                "The Wayland compositor does not currently provide a data-device clipboard.");
        }

        if ((request.GestureToken is not UserGestureToken gestureToken)
            || (gestureToken.NativeInputSerial == 0)
            || (gestureToken.Window != request.Window))
        {
            return CreateClipboardWriteResponse(
                request.Window,
                ClipboardTextResult.PermissionDenied,
                "The clipboard write does not have a current gesture for the focused window.");
        }

        if (!IsClipboardWindowFocused(request.Window)
            || (gestureToken.NativeInputSerial != _lastClipboardGestureSerial))
        {
            return CreateClipboardWriteResponse(
                request.Window,
                ClipboardTextResult.SelectionExpired,
                "The clipboard gesture expired before the selection change began.");
        }

        if (_clipboardDataSources.Count >= 64)
        {
            throw new InvalidOperationException("The bounded Wayland clipboard source table is full.");
        }

        byte[] textBytes;

        try
        {
            textBytes = s_strictUtf8Encoding.GetBytes(request.Text);
        }
        catch (EncoderFallbackException)
        {
            return CreateClipboardWriteResponse(
                request.Window,
                ClipboardTextResult.InvalidData,
                "The requested clipboard text contains an unpaired UTF-16 surrogate.");
        }

        if (textBytes.Length > request.MaximumUtf8Bytes)
        {
            return CreateClipboardWriteResponse(
                request.Window,
                ClipboardTextResult.TooLarge,
                "The complete clipboard text exceeds the request byte limit.");
        }

        var sourceArguments = stackalloc WaylandArgument[1];
        sourceArguments[0] = default;
        var source = Connection.CreateProxy(
            _dataDeviceManager,
            opcode: 0,
            ProtocolMetadata.WlDataSourceInterface,
            checked((uint)_dataDeviceManagerVersion),
            sourceArguments);

        if (source == 0)
        {
            throw new IOException("The Wayland data-device manager could not create a clipboard source.");
        }

        var sourceState = new WaylandClipboardDataSourceState(source, textBytes);

        if (Connection.AddListener(source, _dataSourceListener, GCHandle.ToIntPtr(_callbackHandle)) < 0)
        {
            Connection.DestroyProxy(source);
            throw new IOException("The Wayland clipboard-source listener could not be installed.");
        }

        _clipboardDataSources.Add(source, sourceState);

        try
        {
            var offerArguments = stackalloc WaylandArgument[1];

            foreach (var mimeType in s_textMimeTypes)
            {
                var mimePointer = Marshal.StringToCoTaskMemUTF8(mimeType);

                try
                {
                    offerArguments[0].Pointer = mimePointer;
                    Connection.SendRequest(source, opcode: 0, offerArguments);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(mimePointer);
                }
            }

            var selectionArguments = stackalloc WaylandArgument[2];
            selectionArguments[0].Pointer = source;
            selectionArguments[1].Unsigned = gestureToken.NativeInputSerial;
            Connection.SendRequest(_dataDevice, opcode: 1, selectionArguments);
            Connection.FlushRequests();
        }
        catch
        {
            DestroyClipboardDataSource(sourceState, sendDestroyRequest: true);
            throw;
        }

        _selectedClipboardSource = source;
        return new ClipboardWriteResponse(ClipboardTextResult.Completed, diagnostic: null);
    }

    private unsafe void BindClipboardDataDevice()
    {
        if ((_dataDeviceManager == 0) || (_seat == 0) || (_dataDevice != 0))
        {
            return;
        }

        var version = Math.Min(checked((uint)_dataDeviceManagerVersion), checked((uint)_seatVersion));
        var arguments = stackalloc WaylandArgument[2];
        arguments[0] = default;
        arguments[1].Pointer = _seat;
        var dataDevice = Connection.CreateProxy(
            _dataDeviceManager,
            opcode: 1,
            ProtocolMetadata.WlDataDeviceInterface,
            version,
            arguments);

        if (dataDevice == 0)
        {
            throw new IOException("The Wayland data-device manager could not create a seat data device.");
        }

        if (Connection.AddListener(dataDevice, _dataDeviceListener, GCHandle.ToIntPtr(_callbackHandle)) < 0)
        {
            Connection.DestroyProxy(dataDevice);
            throw new IOException("The Wayland data-device listener could not be installed.");
        }

        _dataDevice = dataDevice;
        _clipboardStatus = ClipboardServiceStatus.Ready;
    }

    private void CancelClipboardReads(ClipboardStatus status)
    {
        foreach (var transfer in _clipboardReads)
        {
            transfer.Cancel(status);
        }
    }

    private void CancelClipboardReads(WaylandClipboardDataOfferState offer, ClipboardStatus status)
    {
        foreach (var transfer in _clipboardReads)
        {
            if (transfer.Offer == offer)
            {
                transfer.Cancel(status);
            }
        }
    }

    private void CancelClipboardReads(WindowHandle window, ClipboardStatus status)
    {
        foreach (var transfer in _clipboardReads)
        {
            if (transfer.Request.Window == window)
            {
                transfer.Cancel(status);
            }
        }
    }

    private void CancelClipboardWrites(nint sourceProxy)
    {
        foreach (var transfer in _clipboardWrites)
        {
            if (transfer.SourceProxy == sourceProxy)
            {
                transfer.Cancel();
            }
        }
    }

    private void DestroyClipboardDataOffer(WaylandClipboardDataOfferState offer, List<Exception>? failures = null)
    {
        if (!_clipboardDataOffers.Remove(offer.Proxy))
        {
            return;
        }

        try
        {
            Connection.SendRequest(offer.Proxy, opcode: 2);
            Connection.DestroyProxy(offer.Proxy);
        }
        catch (Exception exception)
        {
            if (failures is null)
            {
                throw;
            }

            failures.Add(exception);
        }
    }

    private void DestroyClipboardDataSource(
        WaylandClipboardDataSourceState source,
        bool sendDestroyRequest,
        List<Exception>? failures = null)
    {
        if (!_clipboardDataSources.Remove(source.Proxy))
        {
            return;
        }

        CancelClipboardWrites(source.Proxy);

        if (_selectedClipboardSource == source.Proxy)
        {
            _selectedClipboardSource = 0;
        }

        try
        {
            if (sendDestroyRequest)
            {
                Connection.SendRequest(source.Proxy, opcode: 1);
            }

            Connection.DestroyProxy(source.Proxy);
        }
        catch (Exception exception)
        {
            if (failures is null)
            {
                throw;
            }

            failures.Add(exception);
        }
    }

    private void ExpireClipboardSelectionIfUnfocused()
    {
        if ((_keyboardFocusedWindow is not null)
            || (_selectedClipboardOffer is not WaylandClipboardDataOfferState offer))
        {
            return;
        }

        _selectedClipboardOffer = null;
        offer.IsSelected = false;
        CancelClipboardReads(offer, ClipboardTextResult.SelectionExpired);
        RetireClipboardDataOfferIfUnused(offer);
    }

    private void HandleClipboardDataDeviceDataOffer(nint dataDevice, nint offerProxy)
    {
        if ((dataDevice != _dataDevice) || (offerProxy == 0))
        {
            return;
        }

        if (_clipboardDataOffers.ContainsKey(offerProxy))
        {
            throw new InvalidOperationException("The Wayland compositor reused a live data-offer proxy.");
        }

        if (Connection.AddListener(offerProxy, _dataOfferListener, GCHandle.ToIntPtr(_callbackHandle)) < 0)
        {
            Connection.SendRequest(offerProxy, opcode: 2);
            Connection.DestroyProxy(offerProxy);
            throw new IOException("The Wayland data-offer listener could not be installed.");
        }

        _clipboardDataOffers.Add(offerProxy, new WaylandClipboardDataOfferState(offerProxy));
    }

    private unsafe void HandleClipboardDataDeviceEnter(nint dataDevice, uint serial, nint surface, nint offerProxy)
    {
        _ = surface;

        if ((dataDevice != _dataDevice)
            || (offerProxy == 0)
            || !_clipboardDataOffers.TryGetValue(offerProxy, out var offer))
        {
            return;
        }

        offer.IsDragOffer = true;
        var arguments = stackalloc WaylandArgument[2];
        arguments[0].Unsigned = serial;
        arguments[1].Pointer = 0;
        Connection.SendRequest(offerProxy, opcode: 0, arguments);
    }

    private void HandleClipboardDataDeviceLeave(nint dataDevice)
    {
        if (dataDevice != _dataDevice)
        {
            return;
        }

        var offers = new List<WaylandClipboardDataOfferState>(_clipboardDataOffers.Values);

        foreach (var offer in offers)
        {
            offer.IsDragOffer = false;
            RetireClipboardDataOfferIfUnused(offer);
        }
    }

    private void HandleClipboardDataDeviceSelection(nint dataDevice, nint offerProxy)
    {
        if (dataDevice != _dataDevice)
        {
            return;
        }

        WaylandClipboardDataOfferState? nextOffer = null;

        if (offerProxy != 0)
        {
            nextOffer = _clipboardDataOffers.TryGetValue(offerProxy, out var offer)
                ? offer
                : throw new InvalidOperationException("The Wayland selection referenced an unknown data offer.");
        }

        var priorOffer = _selectedClipboardOffer;
        _selectedClipboardSource = 0;
        _selectedClipboardOffer = nextOffer;

        if (priorOffer is not null)
        {
            priorOffer.IsSelected = false;

            if (priorOffer != nextOffer)
            {
                CancelClipboardReads(priorOffer, ClipboardTextResult.SelectionExpired);
                RetireClipboardDataOfferIfUnused(priorOffer);
            }
        }

        if (nextOffer is { } selectedOffer)
        {
            selectedOffer.IsSelected = true;
        }

        var offers = new List<WaylandClipboardDataOfferState>(_clipboardDataOffers.Values);

        foreach (var otherOffer in offers)
        {
            RetireClipboardDataOfferIfUnused(otherOffer);
        }
    }

    private void HandleClipboardDataOfferMimeType(nint offerProxy, nint mimeTypePointer)
    {
        if (!_clipboardDataOffers.TryGetValue(offerProxy, out var offer))
        {
            return;
        }

        var mimeType = Marshal.PtrToStringUTF8(mimeTypePointer)
            ?? throw new InvalidOperationException("The Wayland compositor supplied a null clipboard MIME type.");

        if ((mimeType.Length == 0)
            || (Encoding.UTF8.GetByteCount(mimeType) > 256)
            || mimeType.Contains('\0', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Wayland compositor supplied an invalid clipboard MIME type.");
        }

        _ = offer.MimeTypes.Add(mimeType);
    }

    private void HandleClipboardDataSourceCancelled(nint sourceProxy)
    {
        if (_clipboardDataSources.TryGetValue(sourceProxy, out var source))
        {
            DestroyClipboardDataSource(source, sendDestroyRequest: true);
        }
    }

    private void HandleClipboardDataSourceSend(nint sourceProxy, nint mimeTypePointer, int fileDescriptor)
    {
        if (!_clipboardDataSources.TryGetValue(sourceProxy, out var source))
        {
            new SafeFileHandle((nint)fileDescriptor, ownsHandle: true).Dispose();
            return;
        }

        var mimeType = Marshal.PtrToStringUTF8(mimeTypePointer);

        if ((mimeType is null) || !source.MimeTypes.Contains(mimeType))
        {
            new SafeFileHandle((nint)fileDescriptor, ownsHandle: true).Dispose();
            return;
        }

        SafeFileHandle? handle = new((nint)fileDescriptor, ownsHandle: true);
        FileStream? stream = null;

        try
        {
            stream = new FileStream(
                handle ?? throw new InvalidOperationException("The Wayland clipboard source pipe is unavailable."),
                FileAccess.Write,
                ClipboardReadBufferBytes,
                isAsync: true);
            handle = null;
            var cancellationSource = new CancellationTokenSource();
            var completionTask = WriteClipboardPayloadAsync(stream, source.Payload, cancellationSource);
            _clipboardWrites.Add(new WaylandClipboardWriteTransfer(source.Proxy, completionTask, cancellationSource));
            stream = null;
        }
        finally
        {
            stream?.Dispose();
            handle?.Dispose();
        }
    }

    private void InitializeClipboard()
    {
        if (_clipboardPolicy is ClipboardServicePolicy.NotRequested)
        {
            return;
        }

        if (!Connection.HasGlobal("wl_data_device_manager"))
        {
            if (_clipboardPolicy is ClipboardServicePolicy.Required)
            {
                throw new PlatformNotSupportedException("The Wayland compositor does not advertise wl_data_device_manager.");
            }

            return;
        }

        if (_dataDeviceManager == 0)
        {
            _dataDeviceManager = Connection.BindGlobal("wl_data_device_manager", maximumVersion: 4);
            _dataDeviceManagerVersion = checked((int)Connection.GetProxyVersion(_dataDeviceManager));
        }

        if (_dataDeviceListener == 0)
        {
            InitializeClipboardListeners();
        }

        BindClipboardDataDevice();
    }

    private unsafe void InitializeClipboardListeners()
    {
        _dataDeviceListener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnClipboardDataDeviceDataOffer,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, nint, int, int, nint, void>)&OnClipboardDataDeviceEnter,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnClipboardDataDeviceLeave,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, int, int, void>)&OnClipboardDataDeviceMotion,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnClipboardDataDeviceDrop,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnClipboardDataDeviceSelection);
        _dataOfferListener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnClipboardDataOfferMimeType,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, void>)&OnClipboardDataOfferSourceActions,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, void>)&OnClipboardDataOfferAction);
        _dataSourceListener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnClipboardDataSourceTarget,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, int, void>)&OnClipboardDataSourceSend,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnClipboardDataSourceCancelled,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnClipboardDataSourceDropPerformed,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnClipboardDataSourceDndFinished,
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, void>)&OnClipboardDataSourceAction);
    }

    private bool IsClipboardWindowFocused(WindowHandle window)
        => _windowsByHandle.ContainsKey(window)
            && (_keyboardFocusedWindow?.Window == window);

    private void PumpClipboardTransfers()
    {
        for (var index = _clipboardReads.Count - 1; index >= 0; index--)
        {
            var transfer = _clipboardReads[index];
            var completionTask = transfer.CompletionTask;

            if ((completionTask is null) || !completionTask.IsCompleted)
            {
                continue;
            }

            _clipboardReads.RemoveAt(index);
            transfer.Offer.ActiveReadCount--;
            var completion = completionTask.GetAwaiter().GetResult();

            if (completion.Failure is Exception failure)
            {
                _ = transfer.TryFail(failure);
            }
            else if (completion.Response is ClipboardReadResponse response)
            {
                _ = transfer.TryComplete(response);
            }

            RetireClipboardDataOfferIfUnused(transfer.Offer);
        }

        for (var index = _clipboardWrites.Count - 1; index >= 0; index--)
        {
            var transfer = _clipboardWrites[index];

            if (!transfer.CompletionTask.IsCompleted)
            {
                continue;
            }

            _clipboardWrites.RemoveAt(index);
            var failure = transfer.CompletionTask.GetAwaiter().GetResult();

            if (failure is not null)
            {
                _callbackFailure ??= new InvalidOperationException("A Wayland clipboard data-source transfer failed.", failure);
            }
        }
    }

    private void RefreshClipboardManager()
    {
        if (_clipboardPolicy is ClipboardServicePolicy.NotRequested)
        {
            return;
        }

        var hasManager = Connection.HasGlobal("wl_data_device_manager");

        if ((_dataDeviceManager != 0) && !hasManager)
        {
            var failures = new List<Exception>();
            RetireClipboardManager(failures);

            if (failures.Count > 0)
            {
                throw new AggregateException("The Wayland data-device manager could not be retired.", failures);
            }
        }
        else if ((_dataDeviceManager == 0) && hasManager)
        {
            InitializeClipboard();
        }
        else if (_dataDeviceManager != 0)
        {
            BindClipboardDataDevice();
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Seat retirement must attempt every clipboard data-device and selection release.")]
    private void RetireClipboardDataDevice(List<Exception> failures)
    {
        CancelClipboardReads(ClipboardTextResult.Canceled);
        var offers = new List<WaylandClipboardDataOfferState>(_clipboardDataOffers.Values);

        foreach (var offer in offers)
        {
            offer.IsSelected = false;
            offer.IsDragOffer = false;
            DestroyClipboardDataOffer(offer, failures);
        }

        _selectedClipboardOffer = null;
        _selectedClipboardSource = 0;
        _lastClipboardGestureSerial = 0;
        var dataDevice = _dataDevice;
        _dataDevice = 0;

        if (dataDevice != 0)
        {
            if (Connection.GetProxyVersion(dataDevice) >= 2)
            {
                try
                {
                    Connection.SendRequest(dataDevice, opcode: 2);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            try
            {
                Connection.DestroyProxy(dataDevice);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        var sources = new List<WaylandClipboardDataSourceState>(_clipboardDataSources.Values);

        foreach (var source in sources)
        {
            DestroyClipboardDataSource(source, sendDestroyRequest: true, failures);
        }

        if (_clipboardPolicy is not ClipboardServicePolicy.NotRequested)
        {
            _clipboardStatus = ClipboardServiceStatus.Unsupported;
        }
    }

    private void RetireClipboardDataOfferIfUnused(WaylandClipboardDataOfferState offer)
    {
        if (!offer.IsSelected && !offer.IsDragOffer && (offer.ActiveReadCount == 0))
        {
            DestroyClipboardDataOffer(offer);
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Manager retirement must continue after individual native proxy-release failures.")]
    private void RetireClipboardManager(List<Exception> failures)
    {
        RetireClipboardDataDevice(failures);
        var manager = _dataDeviceManager;
        _dataDeviceManager = 0;
        _dataDeviceManagerVersion = 0;

        if (manager != 0)
        {
            if (Connection.GetProxyVersion(manager) >= 4)
            {
                try
                {
                    Connection.SendRequest(manager, opcode: 2);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            try
            {
                Connection.DestroyProxy(manager);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    private void RetireDragOffers()
    {
        var offers = new List<WaylandClipboardDataOfferState>(_clipboardDataOffers.Values);

        foreach (var offer in offers)
        {
            offer.IsDragOffer = false;
            RetireClipboardDataOfferIfUnused(offer);
        }
    }

    private static unsafe partial class WaylandClipboardNative
    {
        [LibraryImport("libc", EntryPoint = "pipe2", SetLastError = true)]
        internal static partial int Pipe2(int* fileDescriptors, int flags);
    }

    internal sealed class WaylandClipboardDataOfferState
    {
        internal WaylandClipboardDataOfferState(nint proxy)
        {
            MimeTypes = [];
            Proxy = proxy;
        }

        internal int ActiveReadCount { get; set; }

        internal bool IsDragOffer { get; set; }

        internal bool IsSelected { get; set; }

        internal HashSet<string> MimeTypes { get; }

        internal nint Proxy { get; }
    }

    internal readonly record struct WaylandClipboardReadCompletion(
        WaylandClipboardReadTransfer Transfer,
        ClipboardReadResponse? Response,
        Exception? Failure);

    internal sealed class WaylandClipboardReadTransfer
    {
        private int _completionIssued;
        private int _terminalStatus = -1;

        internal WaylandClipboardReadTransfer(
            ClipboardReadRequest request,
            WaylandClipboardDataOfferState offer,
            Action<ClipboardReadResponse> complete,
            Action<Exception> fail,
            CancellationTokenSource timeoutSource,
            CancellationToken callerCancellationToken)
        {
            CallerCancellationToken = callerCancellationToken;
            CompleteCallback = complete;
            FailCallback = fail;
            Offer = offer;
            Request = request;
            TimeoutSource = timeoutSource;
            CancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                callerCancellationToken,
                timeoutSource.Token);
        }

        internal CancellationToken CallerCancellationToken { get; }

        internal CancellationTokenSource CancellationSource { get; }

        internal CancellationToken CancellationToken => CancellationSource.Token;

        internal Action<ClipboardReadResponse> CompleteCallback { get; }

        internal Task<WaylandClipboardReadCompletion>? CompletionTask { get; set; }

        internal Action<Exception> FailCallback { get; }

        internal WaylandClipboardDataOfferState Offer { get; }

        internal ClipboardReadRequest Request { get; }

        internal ClipboardStatus? TerminalStatus
        {
            get
            {
                var status = Volatile.Read(ref _terminalStatus);
                return (status < 0) ? null : (ClipboardStatus)status;
            }
        }

        internal CancellationTokenSource TimeoutSource { get; }

        internal void Cancel(ClipboardStatus status)
        {
            if (Interlocked.CompareExchange(ref _terminalStatus, (int)status, comparand: -1) == -1)
            {
                try
                {
                    CancellationSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                _ = TryComplete(CreateClipboardReadResponse(
                    Request.Window,
                    status,
                    status is ClipboardTextResult.SelectionExpired
                        ? "The Wayland clipboard selection changed before the read completed."
                        : "The Wayland clipboard read was canceled by its owner."));
            }
        }

        internal void Dispose()
        {
            CancellationSource.Dispose();
            TimeoutSource.Dispose();
        }

        internal bool TryComplete(ClipboardReadResponse response)
        {
            if (Interlocked.Exchange(ref _completionIssued, 1) != 0)
            {
                return false;
            }

            CompleteCallback(response);
            return true;
        }

        internal bool TryFail(Exception exception)
        {
            if (Interlocked.Exchange(ref _completionIssued, 1) != 0)
            {
                return false;
            }

            FailCallback(exception);
            return true;
        }
    }

    private sealed class WaylandClipboardDataSourceState
    {
        internal WaylandClipboardDataSourceState(nint proxy, byte[] payload)
        {
            MimeTypes = [.. s_textMimeTypes];
            Payload = payload;
            Proxy = proxy;
        }

        internal HashSet<string> MimeTypes { get; }

        internal byte[] Payload { get; }

        internal nint Proxy { get; }
    }

    private sealed class WaylandClipboardWriteTransfer
    {
        private readonly CancellationTokenSource _cancellationSource;

        internal WaylandClipboardWriteTransfer(
            nint sourceProxy,
            Task<Exception?> completionTask,
            CancellationTokenSource cancellationSource)
        {
            _cancellationSource = cancellationSource;
            CompletionTask = completionTask;
            SourceProxy = sourceProxy;
        }

        internal Task<Exception?> CompletionTask { get; }

        internal nint SourceProxy { get; }

        internal void Cancel()
        {
            try
            {
                _cancellationSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
