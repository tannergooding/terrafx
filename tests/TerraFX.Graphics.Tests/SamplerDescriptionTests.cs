// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class SamplerDescriptionTests
{
    [Test]
    public void SamplerDescriptionAcceptsWireMappedFiltersAndClampAddressing()
    {
        var sampler = new SamplerDescription(
            SamplerFilter.Nearest,
            SamplerFilter.Linear,
            SamplerFilter.None,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Repeat,
            SamplerAddressMode.Clamp,
            maximumAnisotropy: 4.0f);

        Assert.That((int)SamplerFilter.None, Is.EqualTo(0));
        Assert.That((int)SamplerFilter.Nearest, Is.EqualTo(1));
        Assert.That((int)SamplerFilter.Linear, Is.EqualTo(2));
        Assert.That((int)SamplerAddressMode.Clamp, Is.EqualTo(0));
        Assert.That((int)SamplerAddressMode.Repeat, Is.EqualTo(1));
        Assert.That(sampler.Minification, Is.EqualTo(SamplerFilter.Nearest));
        Assert.That(sampler.Magnification, Is.EqualTo(SamplerFilter.Linear));
        Assert.That(sampler.Mip, Is.EqualTo(SamplerFilter.None));
        Assert.That(sampler.MaximumAnisotropy, Is.EqualTo(4.0f));
    }

    [Test]
    public void SamplerDescriptionRejectsInvalidFiltersAddressesAndAnisotropy()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SamplerDescription(
            SamplerFilter.None,
            SamplerFilter.Linear,
            SamplerFilter.None,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            1.0f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SamplerDescription(
            SamplerFilter.Nearest,
            (SamplerFilter)3,
            SamplerFilter.None,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            1.0f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SamplerDescription(
            SamplerFilter.Nearest,
            SamplerFilter.Linear,
            SamplerFilter.None,
            (SamplerAddressMode)2,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            1.0f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SamplerDescription(
            SamplerFilter.Nearest,
            SamplerFilter.Linear,
            SamplerFilter.None,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            0.0f));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SamplerDescription(
            SamplerFilter.Nearest,
            SamplerFilter.Linear,
            SamplerFilter.None,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            float.NaN));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new SamplerDescription(
            SamplerFilter.Nearest,
            SamplerFilter.Linear,
            SamplerFilter.None,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp,
            float.PositiveInfinity));
    }
}
