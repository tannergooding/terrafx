// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.IO;
using NUnit.Framework;
using TerraFX.Platform;

namespace TerraFX.Platform.Tests;

internal sealed class X11TextInputContextTests
{
    [Test]
    public void AppliesPreeditDeltasByCodepointAndReturnsUtf16Caret()
    {
        var (text, selectionStart) = X11PlatformBackend.X11TextInputContext.ApplyPreeditUpdate(
            "a😀c",
            changeFirst: 1,
            changeLength: 1,
            replacementText: "あ",
            caret: 2);

        Assert.That(text, Is.EqualTo("aあc"));
        Assert.That(selectionStart, Is.EqualTo(2));
    }

    [Test]
    public void ConvertsCaretAfterSupplementaryPreeditToUtf16Offset()
    {
        var (text, selectionStart) = X11PlatformBackend.X11TextInputContext.ApplyPreeditUpdate(
            string.Empty,
            changeFirst: 0,
            changeLength: 0,
            replacementText: "😀a",
            caret: 1);

        Assert.That(text, Is.EqualTo("😀a"));
        Assert.That(selectionStart, Is.EqualTo(2));
    }

    [Test]
    public void RejectsPreeditDeltasOutsideTheCurrentText()
    {
        _ = Assert.Throws<InvalidDataException>(() => _ = X11PlatformBackend.X11TextInputContext.ApplyPreeditUpdate(
                "abc",
                changeFirst: 2,
                changeLength: 2,
                replacementText: string.Empty,
                caret: 0));
    }
}
