// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Platform.Tests;

internal sealed partial class HeadlessPlatformBackendTests
{
    [Test]
    public void X11OnlyRefreshesDpiAfterResourceManagerChangesOnAnIsolatedDisplay()
    {
        if (!OperatingSystem.IsLinux()
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_X11") != "1")
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            Assert.Ignore("This witness requires an explicitly isolated Linux X11 display.");
            return;
        }

        using var backend = CreateX11Backend();
        var window = new WindowHandle(ownerId: 7, index: 0, generation: 1);
        backend.CreateWindow(
            window,
            new WindowOptions("TerraFX X11 DPI transition test") {
                ClientSize = new Vector2(640.0f, 360.0f),
                Visible = false,
            },
            nativeMonitorHandle: 0);

        try
        {
            Assert.That(backend.TryGetNativeWindowBinding(window, out var binding), Is.True);

            X11TestNative.SetRootResourceManagerProperty(binding.NativeDisplayOrInstance, "Xft.dpi: 96\n");
            backend.Pump();

            var reported96Dpi = backend.GetNativeWindowMetrics(window);
            Assert.That(reported96Dpi.EffectiveDpi, Is.EqualTo(96.0f));
            Assert.That(reported96Dpi.ContentScale, Is.EqualTo(new Vector2(1.0f, 1.0f)));
            Assert.That(reported96Dpi.DpiSource, Is.EqualTo(DpiSource.X11Settings));
            Assert.That(reported96Dpi.DpiConfidence, Is.EqualTo(DpiConfidence.Reported));

            X11TestNative.SetRootResourceManagerProperty(binding.NativeDisplayOrInstance, "Xft.dpi: 144\n");
            backend.Pump();

            var reported144Dpi = backend.GetNativeWindowMetrics(window);
            Assert.That(reported144Dpi.EffectiveDpi, Is.EqualTo(144.0f));
            Assert.That(reported144Dpi.ContentScale, Is.EqualTo(new Vector2(1.5f, 1.5f)));
            Assert.That(backend.SetWindowClientSize(window, new Vector2(640.0f, 360.0f)), Is.EqualTo(WindowCommandStatus.Applied));

            var resizedWindow = backend.GetNativeWindowMetrics(window);
            Assert.That(resizedWindow.ClientPixelSize.Width, Is.EqualTo(960));
            Assert.That(resizedWindow.ClientPixelSize.Height, Is.EqualTo(540));
            Assert.That(resizedWindow.LogicalClientSize, Is.EqualTo(new Vector2(640.0f, 360.0f)));
        }
        finally
        {
            backend.CloseWindow(window);
        }
    }

    private static unsafe partial class X11TestNative
    {
        internal static void SetRootResourceManagerProperty(nint display, string value)
        {
            ArgumentNullException.ThrowIfNull(value);

            var rootWindow = XRootWindow(display, XDefaultScreen(display));
            var property = XInternAtom(display, "RESOURCE_MANAGER", onlyIfExists: 0);
            var stringType = XInternAtom(display, "STRING", onlyIfExists: 0);
            var resourceBytes = Encoding.ASCII.GetBytes(value);

            if ((rootWindow == 0) || (property == 0) || (stringType == 0))
            {
                throw new InvalidOperationException("The X11 test could not resolve the resource-manager property.");
            }

            fixed (byte* resourceData = resourceBytes)
            {
                if (XChangeProperty(
                    display,
                    rootWindow,
                    property,
                    stringType,
                    format: 8,
                    mode: 0,
                    resourceData,
                    resourceBytes.Length) == 0)
                {
                    throw new InvalidOperationException("The X11 test could not replace the resource-manager property.");
                }
            }

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
        [LibraryImport("libX11.so.6", EntryPoint = "XDefaultScreen")]
        private static partial int XDefaultScreen(nint display);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libX11.so.6", EntryPoint = "XRootWindow")]
        private static partial nuint XRootWindow(nint display, int screen);
    }
}
