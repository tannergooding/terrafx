// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphicsDeviceStatusTests
{
    [Test]
    public void GraphicsDeviceStatusUsesExplicitLifecycleValues()
    {
        Assert.That((int)GraphicsDeviceStatus.None, Is.EqualTo(0));
        Assert.That((int)GraphicsDeviceStatus.Ready, Is.EqualTo(1));
        Assert.That((int)GraphicsDeviceStatus.Lost, Is.EqualTo(2));
        Assert.That((int)GraphicsDeviceStatus.Stopping, Is.EqualTo(3));
        Assert.That((int)GraphicsDeviceStatus.Disposed, Is.EqualTo(4));
        Assert.That(Enum.IsDefined((GraphicsDeviceStatus)5), Is.False);
    }
}
