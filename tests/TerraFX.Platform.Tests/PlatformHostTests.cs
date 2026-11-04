// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class PlatformHostTests
{
    [Test]
    public void ClipboardServiceStatusValuesAreStable()
    {
        Assert.That((int)ClipboardServiceStatus.NotRequested, Is.EqualTo(0));
        Assert.That((int)ClipboardServiceStatus.Ready, Is.EqualTo(1));
        Assert.That((int)ClipboardServiceStatus.Unsupported, Is.EqualTo(2));
    }

    [Test]
    public void HeadlessHostReportsNoMonitorsAndUnrequestedClipboard()
    {
        var host = new PlatformHost(1, PlatformBackend.Headless, ClipboardServicePolicy.NotRequested);

        try
        {
            Assert.That(host.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.NotRequested));
            Assert.That(host.GetMonitorHandles(), Is.Empty);
            host.Pump();
        }
        finally
        {
            host.DisposeFromOwner();
        }
    }

    [Test]
    public void MonitorSnapshotAndPumpAreOwnerThreadBound()
    {
        var host = new PlatformHost(4, PlatformBackend.Headless, ClipboardServicePolicy.NotRequested);

        try
        {
            _ = Assert.Throws<System.InvalidOperationException>(
                () => Task.Run(host.GetMonitorHandles).GetAwaiter().GetResult());
            _ = Assert.Throws<System.InvalidOperationException>(
                () => Task.Run(host.Pump).GetAwaiter().GetResult());
        }
        finally
        {
            host.DisposeFromOwner();
        }
    }

    [Test]
    public void OptionalClipboardIsUnsupportedAndRequiredAdmissionFails()
    {
        var optional = new PlatformHost(2, PlatformBackend.Headless, ClipboardServicePolicy.Optional);

        try
        {
            Assert.That(optional.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.Unsupported));
            var clipboardText = optional.ClipboardText
                ?? throw new System.InvalidOperationException("The optional clipboard service was not exposed.");
            var response = clipboardText.ReadTextAsync(
                new ClipboardReadRequest(new TerraFX.WindowHandle(2, index: 0, generation: 1), 16, gestureToken: null))
                .GetAwaiter()
                .GetResult();
            Assert.That(response.Status, Is.EqualTo(ClipboardStatus.Unsupported));
        }
        finally
        {
            optional.DisposeFromOwner();
        }

        _ = Assert.Throws<System.NotSupportedException>(
            () => _ = new PlatformHost(3, PlatformBackend.Headless, ClipboardServicePolicy.Required));
    }
}
