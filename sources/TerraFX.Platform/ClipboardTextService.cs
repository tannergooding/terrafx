// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TerraFX;

namespace TerraFX.Platform;

/// <summary>Marshals bounded clipboard text operations through the platform owner's pump.</summary>
public sealed class ClipboardTextService
{
    internal const int MaximumUtf8Bytes = 1_048_576;
    private const int MaximumPendingRequests = 64;

    private static readonly UTF8Encoding s_strictUtf8Encoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IPlatformBackend _backend;
    private bool _isDisposed;
    private readonly int _ownerThreadId;
    private readonly Lock _requestGate = new();
    private readonly Queue<PendingRequest> _requests = [];
    private readonly ClipboardServiceStatus _serviceStatus;

    internal ClipboardTextService(IPlatformBackend backend, ClipboardServiceStatus serviceStatus)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)serviceStatus, (int)ClipboardServiceStatus.Unsupported);
        ArgumentOutOfRangeException.ThrowIfNegative((int)serviceStatus);
        _backend = backend;
        _serviceStatus = serviceStatus;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    internal static Diagnostic CreateDiagnostic(
        string code,
        WindowHandle window,
        string operation,
        int? nativeResult,
        string message)
        => new(
            code,
            DiagnosticSeverity.Warning,
            Stopwatch.GetTimestamp(),
            hostId: window.OwnerId,
            worldGuid: null,
            deviceId: null,
            surfaceId: null,
            deviceEpoch: null,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: null,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation,
            nativeResult,
            message,
            context: [
                new DiagnosticContextEntry("WindowIndex", window.Index.ToString(CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("WindowGeneration", window.Generation.ToString(CultureInfo.InvariantCulture)),
            ]);

    internal static void ValidateGestureWindow(WindowHandle window, UserGestureToken? gestureToken)
    {
        if (gestureToken is UserGestureToken token
            && ((token.EventSequence <= 0) || (token.Window != window)))
        {
            throw new ArgumentException("A clipboard gesture token must name the exact request window.", nameof(gestureToken));
        }
    }

    /// <summary>Queues a bounded clipboard read on the platform owner.</summary>
    /// <param name="request">The target window, result limit, and optional user gesture.</param>
    /// <param name="cancellationToken">The token that cancels the queued task.</param>
    /// <returns>The complete text result or a typed platform outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">The request targets another platform owner.</exception>
    public Task<ClipboardReadResponse> ReadTextAsync(
        ClipboardReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequestWindow(request.Window);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<ClipboardReadResponse>(cancellationToken);
        }

        if (_serviceStatus is not ClipboardServiceStatus.Ready)
        {
            return Task.FromResult(new ClipboardReadResponse(
                ClipboardStatus.Unsupported,
                text: null,
                CreateDiagnostic(
                    "ClipboardUnsupported",
                    request.Window,
                    "ClipboardText.ReadTextAsync",
                    nativeResult: null,
                    "The selected host profile does not provide clipboard text.")));
        }

        var completion = new TaskCompletionSource<ClipboardReadResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingRequest = new PendingRequest(
            () => completion.TrySetCanceled(cancellationToken),
            () => completion.TrySetResult(new ClipboardReadResponse(
                ClipboardStatus.Canceled,
                text: null,
                CreateDiagnostic(
                    "ClipboardCanceled",
                    request.Window,
                    "ClipboardText.ReadTextAsync",
                    nativeResult: null,
                    "The platform owner stopped before the clipboard read began."))),
            exception => completion.TrySetException(exception),
            () => completion.TrySetResult(_backend.ReadClipboardText(request)),
            cancellationToken);
        return Enqueue(pendingRequest, completion.Task);
    }

    /// <summary>Queues a bounded clipboard write on the platform owner.</summary>
    /// <param name="request">The target window, complete text, result limit, and optional user gesture.</param>
    /// <param name="cancellationToken">The token that cancels the queued task.</param>
    /// <returns>The complete write result or a typed platform outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">The request targets another platform owner.</exception>
    public Task<ClipboardWriteResponse> WriteTextAsync(
        ClipboardWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequestWindow(request.Window);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<ClipboardWriteResponse>(cancellationToken);
        }

        int utf8ByteCount;

        try
        {
            utf8ByteCount = s_strictUtf8Encoding.GetByteCount(request.Text);
        }
        catch (EncoderFallbackException)
        {
            return Task.FromResult(new ClipboardWriteResponse(
                ClipboardStatus.InvalidData,
                CreateDiagnostic(
                    "ClipboardInvalidData",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "The requested clipboard text contains an unpaired UTF-16 surrogate.")));
        }

        if (utf8ByteCount > request.MaximumUtf8Bytes)
        {
            return Task.FromResult(new ClipboardWriteResponse(
                ClipboardStatus.TooLarge,
                CreateDiagnostic(
                    "ClipboardTooLarge",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "The complete clipboard text exceeds the request byte limit.")));
        }

        if (_serviceStatus is not ClipboardServiceStatus.Ready)
        {
            return Task.FromResult(new ClipboardWriteResponse(
                ClipboardStatus.Unsupported,
                CreateDiagnostic(
                    "ClipboardUnsupported",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "The selected host profile does not provide clipboard text.")));
        }

        var completion = new TaskCompletionSource<ClipboardWriteResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingRequest = new PendingRequest(
            () => completion.TrySetCanceled(cancellationToken),
            () => completion.TrySetResult(new ClipboardWriteResponse(
                ClipboardStatus.Canceled,
                CreateDiagnostic(
                    "ClipboardCanceled",
                    request.Window,
                    "ClipboardText.WriteTextAsync",
                    nativeResult: null,
                    "The platform owner stopped before the clipboard write began."))),
            exception => completion.TrySetException(exception),
            () => completion.TrySetResult(_backend.WriteClipboardText(request)),
            cancellationToken);
        return Enqueue(pendingRequest, completion.Task);
    }

    internal void DisposeFromOwner()
    {
        EnsureOwnerThread();

        lock (_requestGate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            while (_requests.TryDequeue(out var request))
            {
                request.CancelForDisposal();
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A native request failure is delivered to its task without terminating the platform owner pump.")]
    internal void Pump()
    {
        EnsureOwnerThread();

        while (true)
        {
            PendingRequest? request;

            lock (_requestGate)
            {
                if (_requests.Count == 0)
                {
                    return;
                }

                request = _requests.Dequeue();
            }

            if (request.CancellationToken.IsCancellationRequested)
            {
                request.CancelFromCaller();
                continue;
            }

            try
            {
                request.Execute();
            }
            catch (Exception exception)
            {
                request.Fail(exception);
            }
        }
    }

    private Task<TResponse> Enqueue<TResponse>(
        PendingRequest request,
        Task<TResponse> task)
    {
        lock (_requestGate)
        {
            if (_isDisposed)
            {
                return Task.FromException<TResponse>(new ObjectDisposedException(nameof(ClipboardTextService)));
            }

            if (_requests.Count >= MaximumPendingRequests)
            {
                return Task.FromException<TResponse>(new InvalidOperationException("The bounded clipboard request queue is full."));
            }

            _requests.Enqueue(request);
            return task;
        }
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Clipboard native access is restricted to the platform owner thread.");
        }
    }

    private void ValidateRequestWindow(WindowHandle window)
    {
        if (!window.IsValid || (window.OwnerId != _backend.OwnerId))
        {
            throw new ArgumentException("The clipboard request must target a live window from this platform owner.", nameof(window));
        }
    }

    private sealed class PendingRequest(
        Action cancelFromCaller,
        Action cancelForDisposal,
        Action<Exception> fail,
        Action execute,
        CancellationToken cancellationToken)
    {
        internal CancellationToken CancellationToken { get; } = cancellationToken;

        internal void CancelForDisposal() => cancelForDisposal();

        internal void CancelFromCaller() => cancelFromCaller();

        internal void Execute() => execute();

        internal void Fail(Exception exception) => fail(exception);
    }
}
