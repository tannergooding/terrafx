// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;
using ClipboardTextResult = TerraFX.Platform.ClipboardStatus;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class ClipboardTextServiceTests
{
    private static PhysicalInputEvent CreateKeyDownEvent(
        WindowHandle window,
        long eventSequence,
        uint? nativeInputSerial)
        => new(
            PhysicalInputEventKind.KeyDown,
            hostTimestamp: eventSequence,
            hostSequence: eventSequence,
            new InputDeviceHandle(window.OwnerId, index: 0, generation: 1),
            window,
            new PhysicalInputValue(new DigitalControl(new KeyboardControl(7, 4)), isDown: true),
            nativeUsagePage: 7,
            nativeUsage: 4,
            nativeInputSerial: nativeInputSerial);

    [Test]
    public void CancellationAfterPumpCancelsAnInFlightReadAndRetainsOwnerCleanup()
    {
        using var backend = new FakeDeferredClipboardBackend(ownerId: 9);
        var service = new ClipboardTextService(backend, ClipboardServiceStatus.Ready);
        using var cancellationSource = new CancellationTokenSource();
        var pending = service.ReadTextAsync(
            new ClipboardReadRequest(backend.Window, 16, gestureToken: null),
            cancellationSource.Token);

        service.Pump();

        Assert.That(pending.IsCompleted, Is.False);
        Assert.That(backend.PendingReadCancellationToken, Is.EqualTo(cancellationSource.Token));
        cancellationSource.Cancel();
        Assert.That(pending.IsCanceled, Is.True);

        backend.CompletePendingRead();
        service.DisposeFromOwner();
    }

    [Test]
    public void CancellationBeforePumpDoesNotCallBackend()
    {
        using var backend = new FakeClipboardBackend(ownerId: 1);
        var service = new ClipboardTextService(backend, ClipboardServiceStatus.Ready);
        using var cancellationSource = new CancellationTokenSource();
        var pending = service.ReadTextAsync(new ClipboardReadRequest(backend.Window, 16, gestureToken: null), cancellationSource.Token);

        cancellationSource.Cancel();
        Assert.That(pending.IsCompleted, Is.False);

        service.Pump();

        Assert.That(pending.IsCanceled, Is.True);
        Assert.That(backend.ReadCount, Is.Zero);
        service.DisposeFromOwner();
    }

    [Test]
    public void DisposalCompletesPendingRequestsWithCanceledOutcome()
    {
        using var backend = new FakeClipboardBackend(ownerId: 2);
        var service = new ClipboardTextService(backend, ClipboardServiceStatus.Ready);
        var pending = service.ReadTextAsync(new ClipboardReadRequest(backend.Window, 16, gestureToken: null));

        service.DisposeFromOwner();

        Assert.That(pending.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.Canceled));
        Assert.That(backend.ReadCount, Is.Zero);
    }

    [Test]
    public void GestureTokensRequireNativeWaylandInputAndAreConsumedOnce()
    {
        using var backend = new FakeClipboardBackend(ownerId: 10, requiresClipboardGesture: true);
        var service = new ClipboardTextService(backend, ClipboardServiceStatus.Ready);
        var syntheticEvent = CreateKeyDownEvent(backend.Window, eventSequence: 1, nativeInputSerial: null);
        var keyEvent = CreateKeyDownEvent(backend.Window, eventSequence: 2, nativeInputSerial: 73);

        Assert.That(service.TryCreateUserGestureToken(syntheticEvent, out _), Is.False);
        Assert.That(service.TryCreateUserGestureToken(keyEvent, out var readToken), Is.True);
        Assert.That(readToken.EventSequence, Is.EqualTo(keyEvent.HostSequence));
        Assert.That(readToken.Window, Is.EqualTo(backend.Window));
        Assert.That(service.TryCreateUserGestureToken(keyEvent, out _), Is.False);

        var missingToken = service.ReadTextAsync(new ClipboardReadRequest(backend.Window, 16, gestureToken: null));
        var read = service.ReadTextAsync(new ClipboardReadRequest(backend.Window, 16, readToken));
        var replay = service.ReadTextAsync(new ClipboardReadRequest(backend.Window, 16, readToken));
        Assert.That(missingToken.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.PermissionDenied));
        Assert.That(replay.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.PermissionDenied));

        var pointerEvent = new PhysicalInputEvent(
            PhysicalInputEventKind.PointerButtonDown,
            hostTimestamp: 12,
            hostSequence: 3,
            new InputDeviceHandle(backend.OwnerId, index: 1, generation: 1),
            backend.Window,
            new PhysicalInputValue(new DigitalControl(MouseButtonValue.Primary), isDown: true),
            nativeInputSerial: 91);
        Assert.That(service.TryCreateUserGestureToken(pointerEvent, out var writeToken), Is.True);
        var write = service.WriteTextAsync(new ClipboardWriteRequest(backend.Window, "copy", 16, writeToken));

        service.Pump();

        Assert.That(read.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.Completed));
        Assert.That(write.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.Completed));
        Assert.That(backend.LastReadGestureSerial, Is.EqualTo(73));
        Assert.That(backend.LastWriteGestureSerial, Is.EqualTo(91));
        Assert.That(backend.ReadCount, Is.EqualTo(1));
        Assert.That(backend.WriteCount, Is.EqualTo(1));
        service.DisposeFromOwner();
    }

    [Test]
    public void NewGestureTokenExpiresAnUnconsumedPredecessor()
    {
        using var backend = new FakeClipboardBackend(ownerId: 11, requiresClipboardGesture: true);
        var service = new ClipboardTextService(backend, ClipboardServiceStatus.Ready);
        var keyEvent = CreateKeyDownEvent(backend.Window, eventSequence: 1, nativeInputSerial: 73);
        var pointerEvent = new PhysicalInputEvent(
            PhysicalInputEventKind.PointerButtonDown,
            hostTimestamp: 12,
            hostSequence: 2,
            new InputDeviceHandle(backend.OwnerId, index: 1, generation: 1),
            backend.Window,
            new PhysicalInputValue(new DigitalControl(MouseButtonValue.Primary), isDown: true),
            nativeInputSerial: 91);

        Assert.That(service.TryCreateUserGestureToken(keyEvent, out var oldToken), Is.True);
        Assert.That(service.TryCreateUserGestureToken(pointerEvent, out var currentToken), Is.True);
        var expired = service.WriteTextAsync(new ClipboardWriteRequest(backend.Window, "expired", 16, oldToken));
        var current = service.WriteTextAsync(new ClipboardWriteRequest(backend.Window, "current", 16, currentToken));

        Assert.That(expired.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.PermissionDenied));
        service.Pump();

        Assert.That(current.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.Completed));
        Assert.That(backend.LastWriteGestureSerial, Is.EqualTo(91));
        Assert.That(backend.WriteCount, Is.EqualTo(1));
        service.DisposeFromOwner();
    }

    [Test]
    public void OwnerPumpExecutesQueuedReadAndWriteOnOwnerThread()
    {
        using var backend = new FakeClipboardBackend(ownerId: 3);
        var service = new ClipboardTextService(backend, ClipboardServiceStatus.Ready);
        var read = service.ReadTextAsync(new ClipboardReadRequest(backend.Window, 16, gestureToken: null));
        var write = service.WriteTextAsync(new ClipboardWriteRequest(backend.Window, "hello", 16, gestureToken: null));

        Assert.That(read.IsCompleted, Is.False);
        Assert.That(write.IsCompleted, Is.False);

        service.Pump();

        Assert.That(read.GetAwaiter().GetResult().Text, Is.EqualTo("clipboard"));
        Assert.That(write.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.Completed));
        Assert.That(backend.ReadCount, Is.EqualTo(1));
        Assert.That(backend.WriteCount, Is.EqualTo(1));
        service.DisposeFromOwner();
    }

    [Test]
    public void QueueCapacityIsBoundedAndRejectedRequestsDoNotDisplaceAcceptedWork()
    {
        using var backend = new FakeClipboardBackend(ownerId: 4);
        var service = new ClipboardTextService(backend, ClipboardServiceStatus.Ready);
        var accepted = new Task<ClipboardReadResponse>[64];

        for (var index = 0; index < accepted.Length; index++)
        {
            accepted[index] = service.ReadTextAsync(new ClipboardReadRequest(backend.Window, 16, gestureToken: null));
        }

        var rejected = service.ReadTextAsync(new ClipboardReadRequest(backend.Window, 16, gestureToken: null));
        Assert.That(rejected.IsFaulted, Is.True);

        service.Pump();

        Assert.That(Task.WhenAll(accepted).GetAwaiter().GetResult(), Has.Length.EqualTo(64));
        Assert.That(backend.ReadCount, Is.EqualTo(64));
        Assert.That(rejected.Exception?.GetBaseException(), Is.TypeOf<InvalidOperationException>());
        service.DisposeFromOwner();
    }

    [Test]
    public void ReadPayloadRejectsMalformedTruncatedAndOversizedText()
    {
        var window = new WindowHandle(8, index: 0, generation: 1);
        var malformed = ClipboardTextPayload.Read(window, "\uD800\0".AsSpan(), 16, hasUnscannedData: false);
        var unterminated = ClipboardTextPayload.Read(window, "text".AsSpan(), 16, hasUnscannedData: false);
        var oversized = ClipboardTextPayload.Read(window, "A\u00E9\0".AsSpan(), 2, hasUnscannedData: false);
        var unscanned = ClipboardTextPayload.Read(window, "abc".AsSpan(), 2, hasUnscannedData: true);

        Assert.That(malformed.Status, Is.EqualTo(ClipboardStatus.InvalidData));
        Assert.That(unterminated.Status, Is.EqualTo(ClipboardStatus.InvalidData));
        Assert.That(oversized.Status, Is.EqualTo(ClipboardStatus.TooLarge));
        Assert.That(unscanned.Status, Is.EqualTo(ClipboardStatus.TooLarge));
    }

    [Test]
    public void ReadPayloadReturnsCompleteUtf8BoundedTextAndEmptySuccess()
    {
        var window = new WindowHandle(7, index: 0, generation: 1);
        var response = ClipboardTextPayload.Read(window, "A\u00E9\uD83D\uDE00\0ignored".AsSpan(), 7, hasUnscannedData: false);
        var empty = ClipboardTextPayload.Read(window, "\0".AsSpan(), 1, hasUnscannedData: false);

        Assert.That(response.Status, Is.EqualTo(ClipboardStatus.Completed));
        Assert.That(response.Text, Is.EqualTo("A\u00E9\uD83D\uDE00"));
        Assert.That(empty.Status, Is.EqualTo(ClipboardStatus.Completed));
        Assert.That(empty.Text, Is.Empty);
    }

    [Test]
    public void RequestsRejectForeignOwnersAndWritesRejectInvalidOrOversizedText()
    {
        using var backend = new FakeClipboardBackend(ownerId: 5);
        var service = new ClipboardTextService(backend, ClipboardServiceStatus.Ready);
        var foreignWindow = new WindowHandle(6, index: 0, generation: 1);

        _ = Assert.Throws<ArgumentException>(
            () => service.ReadTextAsync(new ClipboardReadRequest(foreignWindow, 16, gestureToken: null)));

        var oversized = service.WriteTextAsync(new ClipboardWriteRequest(backend.Window, "ab", 1, gestureToken: null));
        var invalid = service.WriteTextAsync(new ClipboardWriteRequest(backend.Window, "\uD800", 16, gestureToken: null));

        Assert.That(oversized.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.TooLarge));
        Assert.That(invalid.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.InvalidData));
        Assert.That(backend.WriteCount, Is.Zero);
        service.DisposeFromOwner();
    }

    private class FakeClipboardBackend : IPlatformBackend
    {
        private readonly uint _ownerId;
        private readonly int _ownerThreadId;
        private int _readCount;
        private readonly bool _requiresClipboardGesture;
        private int _writeCount;

        internal FakeClipboardBackend(uint ownerId, bool requiresClipboardGesture = false)
        {
            _ownerId = ownerId;
            _ownerThreadId = Environment.CurrentManagedThreadId;
            _requiresClipboardGesture = requiresClipboardGesture;
            Window = new WindowHandle(ownerId, index: 0, generation: 1);
        }

        public PlatformServiceStatus AudioStatus => PlatformServiceStatus.NotRequested;

        public PlatformBackend Backend => _requiresClipboardGesture ? PlatformBackend.WaylandOnly : PlatformBackend.Headless;

        public ClipboardServiceStatus ClipboardStatus => ClipboardServiceStatus.Ready;

        public PlatformServiceStatus DisplayStatus => PlatformServiceStatus.NotRequested;

        public PlatformServiceStatus GamepadStatus => PlatformServiceStatus.NotRequested;

        public uint OwnerId => _ownerId;

        public PlatformServiceStatus PhysicalInputStatus => PlatformServiceStatus.NotRequested;

        public bool RequiresClipboardGesture => _requiresClipboardGesture;

        internal uint? LastReadGestureSerial { get; private set; }

        internal uint? LastWriteGestureSerial { get; private set; }

        internal int ReadCount => _readCount;

        internal WindowHandle Window { get; }

        internal int WriteCount => _writeCount;

        public void CloseWindow(WindowHandle window)
            => _ = window;

        public void CreateWindow(WindowHandle window, WindowOptions options, nint nativeMonitorHandle)
        {
            _ = window;
            _ = options;
            _ = nativeMonitorHandle;
        }

        public void Dispose()
        {
        }

        public PlatformInputSampleBatch DrainInputSamples()
            => new([], 0);

        public ImmutableArray<WindowHandle> DrainWindowCloseRequests()
            => [];

        public ImmutableArray<MonitorHandle> GetMonitorHandles()
            => [];

        public ImmutableArray<PlatformMonitorInfo> GetMonitorInfo()
            => [];

        public PlatformNativeWindowMetrics GetNativeWindowMetrics(WindowHandle window)
        {
            _ = window;
            return default;
        }

        public PlatformNativeWindowModeState GetNativeWindowModeState(WindowHandle window)
        {
            _ = window;
            return default;
        }

        public void Pump()
        {
        }

        public ClipboardReadResponse ReadClipboardText(ClipboardReadRequest request)
        {
            EnsureOwnerThread();
            LastReadGestureSerial = request.GestureToken?.NativeInputSerial;
            _readCount++;
            return new ClipboardReadResponse(ClipboardTextResult.Completed, "clipboard", diagnostic: null);
        }

        public void RequestGamepadStateReconciliation(nint nativeDeviceIdentity)
            => _ = nativeDeviceIdentity;

        public void RequestInputStateReconciliation(WindowHandle? window)
            => _ = window;

        public void SetMonitorHandles(ImmutableArray<MonitorHandle> monitorHandles)
            => _ = monitorHandles;

        public WindowCommandStatus SetWindowClientSize(WindowHandle window, TerraFX.Numerics.Vector2 logicalClientSize)
        {
            _ = window;
            _ = logicalClientSize;
            return WindowCommandStatus.Unsupported;
        }

        public WindowCommandStatus SetWindowMode(WindowHandle window, WindowMode mode)
        {
            _ = window;
            _ = mode;
            return WindowCommandStatus.Unsupported;
        }

        public WindowCommandStatus SetWindowTitle(WindowHandle window, string title)
        {
            _ = window;
            _ = title;
            return WindowCommandStatus.Unsupported;
        }

        public bool TakeMonitorTopologyChanged()
            => false;

        public bool TryGetNativeWindowBinding(WindowHandle window, out PlatformNativeWindowBinding binding)
        {
            _ = window;
            binding = default;
            return false;
        }

        public ClipboardWriteResponse WriteClipboardText(ClipboardWriteRequest request)
        {
            EnsureOwnerThread();
            LastWriteGestureSerial = request.GestureToken?.NativeInputSerial;
            _writeCount++;
            return new ClipboardWriteResponse(ClipboardTextResult.Completed, diagnostic: null);
        }

        protected void EnsureOwnerThread()
        {
            if (_ownerThreadId != Environment.CurrentManagedThreadId)
            {
                throw new InvalidOperationException("The fake backend must be accessed on its owner thread.");
            }
        }

        protected void RecordRead()
            => _readCount++;
    }

    private sealed class FakeDeferredClipboardBackend : FakeClipboardBackend, IPlatformBackend
    {
        private CancellationToken _pendingReadCancellationToken;
        private Action<ClipboardReadResponse>? _pendingReadComplete;

        internal FakeDeferredClipboardBackend(uint ownerId)
            : base(ownerId)
        {
        }

        internal CancellationToken PendingReadCancellationToken => _pendingReadCancellationToken;

        public void BeginReadClipboardText(
            ClipboardReadRequest request,
            Action<ClipboardReadResponse> complete,
            CancellationToken cancellationToken)
        {
            EnsureOwnerThread();
            RecordRead();
            _pendingReadCancellationToken = cancellationToken;
            _pendingReadComplete = complete;
        }

        internal void CompletePendingRead()
        {
            EnsureOwnerThread();
            var complete = _pendingReadComplete
                ?? throw new InvalidOperationException("The fake backend has no pending clipboard read.");
            _pendingReadComplete = null;
            complete(new ClipboardReadResponse(ClipboardTextResult.Completed, "clipboard", diagnostic: null));
        }
    }
}
