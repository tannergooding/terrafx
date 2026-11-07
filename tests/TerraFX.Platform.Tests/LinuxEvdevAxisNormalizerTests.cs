// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.IO;
using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class LinuxEvdevAxisNormalizerTests
{
    [Test]
    public void NormalizesCenteredAxesAndAppliesFlatZone()
    {
        var axisInfo = new LinuxEvdevAxisInfo(Minimum: -1000, Maximum: 2000, Fuzz: 5, Flat: 100);

        Assert.That(LinuxEvdevAxisNormalizer.NormalizeCenteredAxis(-1000, axisInfo), Is.EqualTo(-1.0f));
        Assert.That(LinuxEvdevAxisNormalizer.NormalizeCenteredAxis(500, axisInfo), Is.Zero);
        Assert.That(LinuxEvdevAxisNormalizer.NormalizeCenteredAxis(600, axisInfo), Is.Zero);
        Assert.That(LinuxEvdevAxisNormalizer.NormalizeCenteredAxis(1300, axisInfo), Is.EqualTo(0.5f));
        Assert.That(LinuxEvdevAxisNormalizer.NormalizeCenteredAxis(2000, axisInfo), Is.EqualTo(1.0f));
    }

    [Test]
    public void NormalizesUnipolarTriggersAndUsesFuzzForChangeSuppression()
    {
        var axisInfo = new LinuxEvdevAxisInfo(Minimum: 0, Maximum: 100, Fuzz: 5, Flat: 10);

        Assert.That(LinuxEvdevAxisNormalizer.NormalizeTriggerAxis(0, axisInfo), Is.Zero);
        Assert.That(LinuxEvdevAxisNormalizer.NormalizeTriggerAxis(10, axisInfo), Is.Zero);
        Assert.That(LinuxEvdevAxisNormalizer.NormalizeTriggerAxis(55, axisInfo), Is.EqualTo(0.5f));
        Assert.That(LinuxEvdevAxisNormalizer.NormalizeTriggerAxis(100, axisInfo), Is.EqualTo(1.0f));
        Assert.That(LinuxEvdevAxisNormalizer.HasSignificantChange(20, 25, axisInfo), Is.False);
        Assert.That(LinuxEvdevAxisNormalizer.HasSignificantChange(20, 26, axisInfo), Is.True);
    }

    [Test]
    public void RejectsInvalidAxisDescriptorsAndSamples()
    {
        _ = Assert.Throws<InvalidDataException>(
            () => LinuxEvdevAxisNormalizer.NormalizeCenteredAxis(0, new LinuxEvdevAxisInfo(1, 1, 0, 0)));
        _ = Assert.Throws<InvalidDataException>(
            () => LinuxEvdevAxisNormalizer.NormalizeCenteredAxis(0, new LinuxEvdevAxisInfo(-10, 10, -1, 0)));
        _ = Assert.Throws<InvalidDataException>(
            () => LinuxEvdevAxisNormalizer.NormalizeCenteredAxis(0, new LinuxEvdevAxisInfo(-10, 10, 0, 10)));
        _ = Assert.Throws<InvalidDataException>(
            () => LinuxEvdevAxisNormalizer.NormalizeTriggerAxis(20, new LinuxEvdevAxisInfo(0, 10, 0, 10)));
        _ = Assert.Throws<InvalidDataException>(
            () => LinuxEvdevAxisNormalizer.NormalizeTriggerAxis(11, new LinuxEvdevAxisInfo(0, 10, 0, 0)));
    }
}
