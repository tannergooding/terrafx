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
        byte[] payload,
        int maximumUtf8Bytes)
    {
        var request = new ClipboardReadRequest(window, maximumUtf8Bytes, gestureToken: null);
        var offer = new WaylandPlatformBackend.WaylandClipboardDataOfferState((nint)1);
        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var transfer = new WaylandPlatformBackend.WaylandClipboardReadTransfer(
            request,
            offer,
            static _ => { },
            static _ => { },
            timeoutSource,
            CancellationToken.None);
        return await WaylandPlatformBackend.ReadClipboardTransferAsync(transfer, new MemoryStream(payload)).ConfigureAwait(false);
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
}
