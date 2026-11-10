// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class WaylandClipboardTransferTests
{
    private static async Task<WaylandPlatformBackend.WaylandClipboardReadCompletion> ReadAsync(
        WindowHandle window,
        Stream stream,
        int maximumUtf8Bytes,
        TimeSpan timeout,
        CancellationToken callerCancellationToken)
    {
        var request = new ClipboardReadRequest(window, maximumUtf8Bytes, gestureToken: null);
        var offer = new WaylandPlatformBackend.WaylandClipboardDataOfferState((nint)1);
        using var timeoutSource = new CancellationTokenSource(timeout);
        var transfer = new WaylandPlatformBackend.WaylandClipboardReadTransfer(
            request,
            offer,
            static _ => { },
            static _ => { },
            timeoutSource,
            TimeProvider.System.GetTimestamp(),
            callerCancellationToken);
        return await WaylandPlatformBackend.ReadClipboardTransferAsync(transfer, stream).ConfigureAwait(false);
    }

    private static async Task<WaylandPlatformBackend.WaylandClipboardReadCompletion> ReadAsync(
        WindowHandle window,
        byte[] payload,
        int maximumUtf8Bytes)
    {
        using var stream = new MemoryStream(payload);
        return await ReadAsync(
            window,
            stream,
            maximumUtf8Bytes,
            TimeSpan.FromSeconds(5),
            CancellationToken.None).ConfigureAwait(false);
    }

    [Test]
    public async Task ReadsCompleteUtf8WithinTheRequestLimit()
    {
        var window = new WindowHandle(1, index: 0, generation: 1);
        var payload = Encoding.UTF8.GetBytes("A\u00E9\uD83D\uDE00");

        var completion = await ReadAsync(window, payload, maximumUtf8Bytes: 7).ConfigureAwait(false);

        Assert.That(completion.Failure, Is.Null);
        Assert.That(completion.Response?.Status, Is.EqualTo(ClipboardStatus.Completed));
        Assert.That(completion.Response?.Text, Is.EqualTo("A\u00E9\uD83D\uDE00"));
    }

    [Test]
    public async Task RejectsMalformedUtf8()
    {
        var window = new WindowHandle(2, index: 0, generation: 1);
        var completion = await ReadAsync(window, [0xC3, 0x28], maximumUtf8Bytes: 16).ConfigureAwait(false);

        Assert.That(completion.Failure, Is.Null);
        Assert.That(completion.Response?.Status, Is.EqualTo(ClipboardStatus.InvalidData));
    }

    [Test]
    public async Task RejectsOversizedUtf8()
    {
        var window = new WindowHandle(3, index: 0, generation: 1);
        var completion = await ReadAsync(window, [0x61, 0x62, 0x63], maximumUtf8Bytes: 2).ConfigureAwait(false);

        Assert.That(completion.Failure, Is.Null);
        Assert.That(completion.Response?.Status, Is.EqualTo(ClipboardStatus.TooLarge));
    }

    [Test]
    public async Task ReportsCallerCancellationAndDisposesTheStream()
    {
        var window = new WindowHandle(4, index: 0, generation: 1);
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        using var stream = new WaylandClipboardReadTestStream(failOnRead: false);

        var completion = await ReadAsync(
            window,
            stream,
            maximumUtf8Bytes: 16,
            timeout: TimeSpan.FromSeconds(5),
            callerCancellationToken: cancellationSource.Token).ConfigureAwait(false);

        Assert.That(completion.Failure, Is.Null);
        Assert.That(completion.Response?.Status, Is.EqualTo(ClipboardStatus.Canceled));
        Assert.That(stream.IsDisposed, Is.True);
    }

    [Test]
    public async Task ReportsSelectionExpirationWhenTheStreamFails()
    {
        var window = new WindowHandle(6, index: 0, generation: 1);
        using var stream = new WaylandClipboardReadTestStream(failOnRead: true);

        var completion = await ReadAsync(
            window,
            stream,
            maximumUtf8Bytes: 16,
            timeout: TimeSpan.FromSeconds(5),
            callerCancellationToken: CancellationToken.None).ConfigureAwait(false);

        Assert.That(completion.Failure, Is.Null);
        Assert.That(completion.Response?.Status, Is.EqualTo(ClipboardStatus.SelectionExpired));
        Assert.That(stream.IsDisposed, Is.True);
    }

    [Test]
    public async Task ReportsTimeoutAndDisposesTheStream()
    {
        var window = new WindowHandle(5, index: 0, generation: 1);
        using var stream = new WaylandClipboardReadTestStream(failOnRead: false);

        var completion = await ReadAsync(
            window,
            stream,
            maximumUtf8Bytes: 16,
            timeout: TimeSpan.FromMilliseconds(50),
            callerCancellationToken: CancellationToken.None).ConfigureAwait(false);

        Assert.That(completion.Failure, Is.Null);
        Assert.That(completion.Response?.Status, Is.EqualTo(ClipboardStatus.TimedOut));
        Assert.That(stream.IsDisposed, Is.True);
    }

    private sealed class WaylandClipboardReadTestStream(bool failOnRead) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public bool IsDisposed { get; private set; }

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get
            {
                throw new NotSupportedException();
            }

            set
            {
                throw new NotSupportedException();
            }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (failOnRead)
            {
                throw new IOException("The clipboard data source closed before completing the transfer.");
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
            }

            base.Dispose(disposing);
        }
    }
}
