// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class X11ClipboardTests
{
    private static IPlatformBackend CreateBackend(uint ownerId)
        => PlatformBackendFactory.Create(
            PlatformBackend.X11Only,
            ownerId,
            ClipboardServicePolicy.Optional);

    private static void CreateWindow(IPlatformBackend backend, WindowHandle window)
        => backend.CreateWindow(
            window,
            new WindowOptions("TerraFX isolated X11 clipboard") {
                Visible = false,
            },
            nativeMonitorHandle: 0);

    private static ClipboardReadResponse PumpRead(
        IPlatformBackend owner,
        IPlatformBackend requestor,
        ClipboardTextService clipboard,
        Task<ClipboardReadResponse> read)
    {
        requestor.Pump();
        clipboard.Pump();

        for (var index = 0; index < 256; index++)
        {
            owner.Pump();
            requestor.Pump();
        }

        Assert.That(read.IsCompleted, Is.True, "The isolated X11 selection transfer did not complete.");
        return read.GetAwaiter().GetResult();
    }

    private static void RequireIsolatedX11()
    {
        if (!OperatingSystem.IsLinux()
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_X11") != "1")
            || (Environment.GetEnvironmentVariable("DISPLAY") != ":99"))
        {
            Assert.Ignore("The X11 clipboard witness requires the test-owned :99 display.");
        }
    }

    [Test]
    public void DecodesCompleteAndEmptyUtf8ClipboardPayloads()
    {
        var window = new WindowHandle(ownerId: 80, index: 0, generation: 1);
        var decoded = X11PlatformBackend.DecodeClipboardText(
            window,
            7,
            [0x41, 0xC3, 0xA9, 0xF0, 0x9F, 0x98, 0x80]);
        var empty = X11PlatformBackend.DecodeClipboardText(window, 1, []);

        Assert.That(decoded.Status, Is.EqualTo(ClipboardStatus.Completed));
        Assert.That(decoded.Text, Is.EqualTo("A\u00E9\U0001F600"));
        Assert.That(empty.Status, Is.EqualTo(ClipboardStatus.Completed));
        Assert.That(empty.Text, Is.Empty);
    }

    [Test]
    public void RejectsMalformedAndOversizedUtf8ClipboardPayloads()
    {
        var window = new WindowHandle(ownerId: 80, index: 0, generation: 1);
        var malformed = X11PlatformBackend.DecodeClipboardText(window, 8, [0xC3, 0x28]);
        var oversized = X11PlatformBackend.DecodeClipboardText(window, 2, [0x61, 0x62, 0x63]);

        Assert.That(malformed.Status, Is.EqualTo(ClipboardStatus.InvalidData));
        Assert.That(malformed.Text, Is.Null);
        Assert.That(oversized.Status, Is.EqualTo(ClipboardStatus.TooLarge));
        Assert.That(oversized.Text, Is.Null);
    }

    [Test]
    public void X11SelectionAdvertisesSupportedTextTargets()
    {
        RequireIsolatedX11();

        using var owner = CreateBackend(ownerId: 86);
        using var requestor = CreateBackend(ownerId: 87);
        var ownerClipboard = new ClipboardTextService(owner, ClipboardServiceStatus.Ready);
        var ownerWindow = new WindowHandle(ownerId: 86, index: 0, generation: 1);
        var requestorWindow = new WindowHandle(ownerId: 87, index: 0, generation: 1);
        var ownerWindowCreated = false;
        var requestorWindowCreated = false;

        try
        {
            CreateWindow(owner, ownerWindow);
            ownerWindowCreated = true;
            CreateWindow(requestor, requestorWindow);
            requestorWindowCreated = true;

            var write = ownerClipboard.WriteTextAsync(
                new ClipboardWriteRequest(ownerWindow, "advertised", 32, gestureToken: null));
            ownerClipboard.Pump();
            Assert.That(write.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.Completed));

            Assert.That(requestor.TryGetNativeWindowBinding(requestorWindow, out var binding), Is.True);
            var display = binding.NativeDisplayOrInstance;
            var nativeWindow = checked((nuint)binding.NativeWindowOrSurface);
            var clipboardAtom = X11ClipboardTestNative.InternAtom(display, "CLIPBOARD");
            var targetsAtom = X11ClipboardTestNative.InternAtom(display, "TARGETS");
            var atomAtom = X11ClipboardTestNative.InternAtom(display, "ATOM");
            var utf8StringAtom = X11ClipboardTestNative.InternAtom(display, "UTF8_STRING");
            var textPlainUtf8Atom = X11ClipboardTestNative.InternAtom(display, "text/plain;charset=utf-8");
            var property = X11ClipboardTestNative.InternAtom(display, "_TERRAFX_TEST_CLIPBOARD_TARGETS");

            Assert.That(
                X11ClipboardTestNative.ConvertSelection(display, clipboardAtom, targetsAtom, property, nativeWindow),
                Is.Not.Zero);
            X11ClipboardTestNative.Flush(display);
            owner.Pump();
            requestor.Pump();

            var response = X11ClipboardTestNative.ReadProperty(display, nativeWindow, property, delete: true);

            try
            {
                Assert.That(response.Status, Is.Zero);
                Assert.That(response.Type, Is.EqualTo(atomAtom));
                Assert.That(response.Format, Is.EqualTo(32));
                Assert.That(response.ItemCount, Is.EqualTo((nuint)3));
                Assert.That(response.BytesAfter, Is.EqualTo((nuint)0));
                Assert.That(
                    X11ClipboardTestNative.GetUnsignedLongs(response.Value, checked((int)response.ItemCount)),
                    Is.EqualTo([targetsAtom, utf8StringAtom, textPlainUtf8Atom]));
            }
            finally
            {
                X11ClipboardTestNative.Free(response.Value);
            }
        }
        finally
        {
            ownerClipboard.DisposeFromOwner();

            if (requestorWindowCreated)
            {
                requestor.CloseWindow(requestorWindow);
            }

            if (ownerWindowCreated)
            {
                owner.CloseWindow(ownerWindow);
            }
        }
    }

    [Test]
    public void X11SelectionReadTimesOutWhenIncrementalOwnerStopsResponding()
    {
        if (!OperatingSystem.IsLinux()
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_X11") != "1")
            || (Environment.GetEnvironmentVariable("DISPLAY") != ":99"))
        {
            Assert.Ignore("The X11 clipboard timeout witness requires the test-owned :99 display.");
            return;
        }

        using var owner = CreateBackend(ownerId: 84);
        using var requestor = CreateBackend(ownerId: 85);
        var ownerClipboard = new ClipboardTextService(owner, ClipboardServiceStatus.Ready);
        var requestorClipboard = new ClipboardTextService(requestor, ClipboardServiceStatus.Ready);
        var ownerWindow = new WindowHandle(ownerId: 84, index: 0, generation: 1);
        var requestorWindow = new WindowHandle(ownerId: 85, index: 0, generation: 1);
        var ownerWindowCreated = false;
        var requestorWindowCreated = false;

        try
        {
            CreateWindow(owner, ownerWindow);
            ownerWindowCreated = true;
            CreateWindow(requestor, requestorWindow);
            requestorWindowCreated = true;

            var largeText = new string('x', X11ClipboardState.TransferChunkBytes + 1);
            var write = ownerClipboard.WriteTextAsync(
                new ClipboardWriteRequest(ownerWindow, largeText, 1_048_576, gestureToken: null));
            ownerClipboard.Pump();
            Assert.That(write.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.Completed));

            var read = requestorClipboard.ReadTextAsync(
                new ClipboardReadRequest(requestorWindow, largeText.Length, gestureToken: null));
            requestor.Pump();
            requestorClipboard.Pump();
            owner.Pump();
            requestor.Pump();

            Thread.Sleep(TimeSpan.FromMilliseconds(X11ClipboardState.TransferTimeoutMilliseconds + 100));
            requestor.Pump();
            Assert.That(read.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.TimedOut));

            owner.Pump();
            requestor.Pump();
        }
        finally
        {
            requestorClipboard.DisposeFromOwner();
            ownerClipboard.DisposeFromOwner();

            if (requestorWindowCreated)
            {
                requestor.CloseWindow(requestorWindow);
            }

            if (ownerWindowCreated)
            {
                owner.CloseWindow(ownerWindow);
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void X11SelectionRejectsMalformedWirePayloads(bool malformedIncrementalHeader)
    {
        RequireIsolatedX11();

        using var requestor = CreateBackend(ownerId: 88);
        var requestorClipboard = new ClipboardTextService(requestor, ClipboardServiceStatus.Ready);
        var requestorWindow = new WindowHandle(ownerId: 88, index: 0, generation: 1);
        var requestorWindowCreated = false;
        nint ownerDisplay = 0;
        nuint ownerWindow = 0;
        nuint selectionAtom = 0;

        try
        {
            CreateWindow(requestor, requestorWindow);
            requestorWindowCreated = true;
            Assert.That(requestor.TryGetNativeWindowBinding(requestorWindow, out var requestorBinding), Is.True);
            var nativeRequestorWindow = checked((nuint)requestorBinding.NativeWindowOrSurface);
            ownerDisplay = X11ClipboardTestNative.OpenDisplay(displayName: null);

            if (ownerDisplay == 0)
            {
                throw new InvalidOperationException("The X11 clipboard test could not open the test-owned display.");
            }

            ownerWindow = X11ClipboardTestNative.CreateWindow(ownerDisplay);
            selectionAtom = X11ClipboardTestNative.InternAtom(ownerDisplay, "CLIPBOARD");
            var utf8StringAtom = X11ClipboardTestNative.InternAtom(ownerDisplay, "UTF8_STRING");
            var incrAtom = X11ClipboardTestNative.InternAtom(ownerDisplay, "INCR");
            X11ClipboardTestNative.SetSelectionOwner(ownerDisplay, selectionAtom, ownerWindow);
            Assert.That(X11ClipboardTestNative.GetSelectionOwner(ownerDisplay, selectionAtom), Is.EqualTo(ownerWindow));

            var read = requestorClipboard.ReadTextAsync(
                new ClipboardReadRequest(requestorWindow, 64, gestureToken: null));
            requestor.Pump();
            requestorClipboard.Pump();

            Assert.That(X11ClipboardTestNative.NextSelectionRequest(ownerDisplay, out var selectionRequest), Is.True);
            Assert.That(selectionRequest.Requestor, Is.EqualTo(nativeRequestorWindow));
            Assert.That(selectionRequest.Selection, Is.EqualTo(selectionAtom));
            Assert.That(selectionRequest.Target, Is.EqualTo(utf8StringAtom));
            Assert.That(selectionRequest.Property, Is.Not.Zero);
            X11ClipboardTestNative.SetMalformedProperty(
                ownerDisplay,
                selectionRequest,
                incrAtom,
                malformedIncrementalHeader);
            Assert.That(
                X11ClipboardTestNative.SendSelectionNotify(ownerDisplay, selectionRequest, selectionRequest.Property),
                Is.True);

            for (var index = 0; index < 256 && !read.IsCompleted; index++)
            {
                requestor.Pump();
                requestorClipboard.Pump();
                Thread.Sleep(1);
            }

            Assert.That(read.IsCompleted, Is.True);
            Assert.That(read.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.InvalidData));
        }
        finally
        {
            requestorClipboard.DisposeFromOwner();

            if (ownerDisplay != 0)
            {
                if (selectionAtom != 0)
                {
                    X11ClipboardTestNative.SetSelectionOwner(ownerDisplay, selectionAtom, 0);
                }

                if (ownerWindow != 0)
                {
                    X11ClipboardTestNative.DestroyWindow(ownerDisplay, ownerWindow);
                }

                X11ClipboardTestNative.CloseDisplay(ownerDisplay);
            }

            if (requestorWindowCreated)
            {
                requestor.CloseWindow(requestorWindow);
            }
        }
    }

    [Test]
    public void X11SelectionRoundTripsBoundedUtf8AndExpiresWhenOwnershipChanges()
    {
        if (!OperatingSystem.IsLinux()
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_X11") != "1")
            || (Environment.GetEnvironmentVariable("DISPLAY") != ":99"))
        {
            Assert.Ignore("The X11 clipboard witness requires the test-owned :99 display.");
            return;
        }

        using var firstOwner = CreateBackend(ownerId: 81);
        using var secondOwner = CreateBackend(ownerId: 82);
        using var requestor = CreateBackend(ownerId: 83);
        var firstClipboard = new ClipboardTextService(firstOwner, ClipboardServiceStatus.Ready);
        var secondClipboard = new ClipboardTextService(secondOwner, ClipboardServiceStatus.Ready);
        var requestorClipboard = new ClipboardTextService(requestor, ClipboardServiceStatus.Ready);
        var firstWindow = new WindowHandle(ownerId: 81, index: 0, generation: 1);
        var secondWindow = new WindowHandle(ownerId: 82, index: 0, generation: 1);
        var requestorWindow = new WindowHandle(ownerId: 83, index: 0, generation: 1);
        var firstWindowCreated = false;
        var secondWindowCreated = false;
        var requestorWindowCreated = false;

        try
        {
            CreateWindow(firstOwner, firstWindow);
            firstWindowCreated = true;
            CreateWindow(secondOwner, secondWindow);
            secondWindowCreated = true;
            CreateWindow(requestor, requestorWindow);
            requestorWindowCreated = true;

            var noSelection = requestorClipboard.ReadTextAsync(
                new ClipboardReadRequest(requestorWindow, 16, gestureToken: null));
            requestor.Pump();
            requestorClipboard.Pump();
            Assert.That(noSelection.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.NoSelection));

            const int largeTextLength = 500_000;
            var largeText = new string('x', largeTextLength);
            var write = firstClipboard.WriteTextAsync(
                new ClipboardWriteRequest(firstWindow, largeText, 1_048_576, gestureToken: null));
            firstClipboard.Pump();
            Assert.That(write.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.Completed));

            var read = requestorClipboard.ReadTextAsync(
                new ClipboardReadRequest(requestorWindow, largeTextLength, gestureToken: null));
            var response = PumpRead(firstOwner, requestor, requestorClipboard, read);
            Assert.That(response.Status, Is.EqualTo(ClipboardStatus.Completed));
            Assert.That(response.Text, Is.EqualTo(largeText));

            var oversizedRead = requestorClipboard.ReadTextAsync(
                new ClipboardReadRequest(requestorWindow, 32, gestureToken: null));
            var oversizedResponse = PumpRead(firstOwner, requestor, requestorClipboard, oversizedRead);
            Assert.That(oversizedResponse.Status, Is.EqualTo(ClipboardStatus.TooLarge));
            Assert.That(oversizedResponse.Text, Is.Null);

            var ownerChangedRead = requestorClipboard.ReadTextAsync(
                new ClipboardReadRequest(requestorWindow, largeTextLength, gestureToken: null));
            requestor.Pump();
            requestorClipboard.Pump();

            var secondWrite = secondClipboard.WriteTextAsync(
                new ClipboardWriteRequest(secondWindow, "new selection", 32, gestureToken: null));
            secondClipboard.Pump();
            Assert.That(secondWrite.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.Completed));

            firstOwner.Pump();
            requestor.Pump();
            Assert.That(ownerChangedRead.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.SelectionExpired));

            var secondRead = requestorClipboard.ReadTextAsync(
                new ClipboardReadRequest(requestorWindow, 32, gestureToken: null));
            var secondResponse = PumpRead(secondOwner, requestor, requestorClipboard, secondRead);
            Assert.That(secondResponse.Status, Is.EqualTo(ClipboardStatus.Completed));
            Assert.That(secondResponse.Text, Is.EqualTo("new selection"));

            secondOwner.CloseWindow(secondWindow);
            secondWindowCreated = false;

            var selectionLostRead = requestorClipboard.ReadTextAsync(
                new ClipboardReadRequest(requestorWindow, 32, gestureToken: null));
            requestor.Pump();
            requestorClipboard.Pump();
            Assert.That(selectionLostRead.GetAwaiter().GetResult().Status, Is.EqualTo(ClipboardStatus.NoSelection));
        }
        finally
        {
            requestorClipboard.DisposeFromOwner();
            secondClipboard.DisposeFromOwner();
            firstClipboard.DisposeFromOwner();

            if (requestorWindowCreated)
            {
                requestor.CloseWindow(requestorWindow);
            }

            if (secondWindowCreated)
            {
                secondOwner.CloseWindow(secondWindow);
            }

            if (firstWindowCreated)
            {
                firstOwner.CloseWindow(firstWindow);
            }
        }
    }
}
