// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Platform.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class WindowsTextInputTsfTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void CreatesUpdatesAndRetiresTsfDocumentContext()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The TSF document-context witness requires Windows 11.");
            return;
        }

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            Assert.Ignore("The Windows Platform backend requires x64.");
            return;
        }

        var ownerId = OwnerIdAllocator.Allocate();
        var window = new WindowHandle(ownerId, index: 0, generation: 1);
        using var backend = new WindowsPlatformBackend(ownerId, ClipboardServicePolicy.NotRequested);

        backend.CreateWindow(
            window,
            new WindowOptions("TerraFX TSF text-store lifecycle") {
                Visible = false,
            },
            nativeMonitorHandle: 0);

        backend.UpdateTextInputSession(window, new TextInputSessionSnapshot(
            1,
            new TextInputSessionOptions(
                window,
                TextInputPurpose.FreeForm,
                documentRevision: 1,
                surroundingText: "hello",
                selectionStart: 2,
                selectionLength: 0,
                caretPosition: new Vector2(8.0f, 4.0f),
                caretSize: new Vector2(1.0f, 16.0f))));

        backend.UpdateTextInputSession(window, new TextInputSessionSnapshot(
            1,
            new TextInputSessionOptions(
                window,
                TextInputPurpose.FreeForm,
                documentRevision: 2,
                surroundingText: "hello!",
                selectionStart: 6,
                selectionLength: 0,
                caretPosition: new Vector2(12.0f, 4.0f),
                caretSize: new Vector2(1.0f, 16.0f))));

        backend.UpdateTextInputSession(window, snapshot: null);
        backend.CloseWindow(window);
    }
}
