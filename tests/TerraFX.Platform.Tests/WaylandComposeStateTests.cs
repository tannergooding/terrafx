// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX.Platform;

namespace TerraFX.Platform.Tests;

internal sealed class WaylandComposeStateTests
{
    [Test]
    public void ComposesDeadAcuteAndLatinEIntoCommittedUnicode()
    {
        if (!OperatingSystem.IsLinux()
            || !NativeLibrary.TryLoad("libxkbcommon.so.0", out var xkbcommonLibrary))
        {
            Assert.Ignore("The Wayland compose witness requires libxkbcommon on Linux.");
            return;
        }

        NativeLibrary.Free(xkbcommonLibrary);

        using var composeState = WaylandComposeState.TryCreate();

        if (composeState is null)
        {
            Assert.Ignore("The system does not provide an xkbcommon compose table for its locale.");
            return;
        }

        Assert.That(composeState.Feed(0xFE51, out var deadKeyText), Is.EqualTo(WaylandComposeStatus.Composing));
        Assert.That(deadKeyText, Is.Null);
        Assert.That(composeState.Feed(0x0065, out var committedText), Is.EqualTo(WaylandComposeStatus.Committed));
        Assert.That(committedText, Is.EqualTo("é"));
    }

    [Test]
    public void ResetDiscardsAnIncompleteComposeSequence()
    {
        if (!OperatingSystem.IsLinux()
            || !NativeLibrary.TryLoad("libxkbcommon.so.0", out var xkbcommonLibrary))
        {
            Assert.Ignore("The Wayland compose witness requires libxkbcommon on Linux.");
            return;
        }

        NativeLibrary.Free(xkbcommonLibrary);

        using var composeState = WaylandComposeState.TryCreate();

        if (composeState is null)
        {
            Assert.Ignore("The system does not provide an xkbcommon compose table for its locale.");
            return;
        }

        Assert.That(composeState.Feed(0xFE51, out _), Is.EqualTo(WaylandComposeStatus.Composing));
        composeState.Reset();
        Assert.That(composeState.Feed(0x0065, out var committedText), Is.EqualTo(WaylandComposeStatus.Ignored));
        Assert.That(committedText, Is.Null);
    }
}
