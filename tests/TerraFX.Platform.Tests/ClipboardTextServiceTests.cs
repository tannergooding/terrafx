// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using ClipboardTextResult = TerraFX.Platform.ClipboardStatus;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class ClipboardTextServiceTests
{
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

    private sealed class FakeClipboardBackend : IPlatformBackend
    {
        private readonly uint _ownerId;
        private readonly int _ownerThreadId;
        private int _readCount;
        private int _writeCount;

        internal FakeClipboardBackend(uint ownerId)
        {
            _ownerId = ownerId;
            _ownerThreadId = Environment.CurrentManagedThreadId;
            Window = new WindowHandle(ownerId, index: 0, generation: 1);
        }

        public PlatformServiceStatus AudioStatus => PlatformServiceStatus.NotRequested;

        public PlatformBackend Backend => PlatformBackend.Headless;

        public ClipboardServiceStatus ClipboardStatus => ClipboardServiceStatus.Ready;

        public PlatformServiceStatus DisplayStatus => PlatformServiceStatus.NotRequested;

        public uint OwnerId => _ownerId;

        public PlatformServiceStatus PhysicalInputStatus => PlatformServiceStatus.NotRequested;

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

        public void Pump()
        {
        }

        public ClipboardReadResponse ReadClipboardText(ClipboardReadRequest request)
        {
            EnsureOwnerThread();
            _ = request;
            _readCount++;
            return new ClipboardReadResponse(ClipboardTextResult.Completed, "clipboard", diagnostic: null);
        }

        public void SetMonitorHandles(ImmutableArray<MonitorHandle> monitorHandles)
            => _ = monitorHandles;

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
            _ = request;
            _writeCount++;
            return new ClipboardWriteResponse(ClipboardTextResult.Completed, diagnostic: null);
        }

        private void EnsureOwnerThread()
        {
            if (_ownerThreadId != Environment.CurrentManagedThreadId)
            {
                throw new InvalidOperationException("The fake backend must be accessed on its owner thread.");
            }
        }
    }
}
