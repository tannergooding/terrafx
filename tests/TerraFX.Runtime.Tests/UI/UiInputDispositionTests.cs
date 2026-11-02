// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Input;
using TerraFX.UI;

namespace TerraFX.Runtime.Tests.UI;

[TestFixture]
internal sealed class UiInputDispositionTests
{
    [Test]
    public void CapturedClassesAreAnImmutableClosedMask()
    {
        var disposition = new UiInputDisposition(InputClass.Keyboard | InputClass.Pointing);

        Assert.That(disposition.CapturedClasses, Is.EqualTo(InputClass.Keyboard | InputClass.Pointing));
        Assert.That(default(UiInputDisposition).CapturedClasses, Is.EqualTo(InputClass.None));
        Assert.That(
            () => new UiInputDisposition((InputClass)8),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
