// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;
using TerraFX.Numerics;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class WindowMetricsTests
{
    private static WindowMetrics CreateMetrics(
        Vector2? logicalClientSize = null,
        Vector2? contentScale = null,
        float? effectiveDpi = 144.0f,
        DpiSource dpiSource = DpiSource.WindowsPerMonitorV2,
        DpiConfidence dpiConfidence = DpiConfidence.Reported)
        => new(
            logicalClientSize ?? new Vector2(1280, 720),
            new PixelSize(1920, 1080),
            contentScale ?? new Vector2(1.5f),
            effectiveDpi,
            dpiSource,
            dpiConfidence,
            surfaceEpoch: 1,
            swapchainEpoch: 1);

    [Test]
    public void AssumedFallbackDoesNotClaimMeasuredDpiAndAllowsMinimizedExtent()
    {
        var metrics = new WindowMetrics(
            new Vector2(0, 0),
            new PixelSize(0, 0),
            Vector2.One,
            effectiveDpi: null,
            dpiSource: DpiSource.Unknown,
            dpiConfidence: DpiConfidence.Assumed96Dpi,
            surfaceEpoch: 1,
            swapchainEpoch: 0);

        Assert.That(metrics.ClientPixelSize, Is.EqualTo(new PixelSize(0, 0)));
        Assert.That(metrics.EffectiveDpi, Is.Null);
        Assert.That(metrics.DpiSource, Is.EqualTo(DpiSource.Unknown));
        Assert.That(metrics.DpiConfidence, Is.EqualTo(DpiConfidence.Assumed96Dpi));
        Assert.That(metrics.SurfaceEpoch, Is.EqualTo(1));
        Assert.That(metrics.SwapchainEpoch, Is.Zero);
    }

    [Test]
    public void DpiEnumValuesAreStable()
    {
        Assert.That((int)DpiSource.Unknown, Is.EqualTo(0));
        Assert.That((int)DpiSource.WindowsPerMonitorV2, Is.EqualTo(1));
        Assert.That((int)DpiSource.WaylandFractionalScale, Is.EqualTo(2));
        Assert.That((int)DpiSource.WaylandIntegerBufferScale, Is.EqualTo(3));
        Assert.That((int)DpiSource.X11Settings, Is.EqualTo(4));
        Assert.That((int)DpiSource.ApplicationOverride, Is.EqualTo(5));
        Assert.That((int)DpiConfidence.None, Is.EqualTo(0));
        Assert.That((int)DpiConfidence.Reported, Is.EqualTo(1));
        Assert.That((int)DpiConfidence.Assumed96Dpi, Is.EqualTo(2));
    }

    [Test]
    public void RejectsInvalidExtentsValuesAndDpiProvenanceCombinations()
    {
        _ = Assert.Throws<System.ArgumentOutOfRangeException>(
            () => _ = CreateMetrics(logicalClientSize: new Vector2(-1, 1)));
        _ = Assert.Throws<System.ArgumentOutOfRangeException>(
            () => _ = CreateMetrics(contentScale: new Vector2(float.PositiveInfinity, 1)));
        _ = Assert.Throws<System.ArgumentOutOfRangeException>(
            () => _ = CreateMetrics(effectiveDpi: float.NaN));
        _ = Assert.Throws<System.ArgumentOutOfRangeException>(
            () => _ = CreateMetrics(dpiSource: (DpiSource)int.MaxValue));
        _ = Assert.Throws<System.ArgumentOutOfRangeException>(
            () => _ = CreateMetrics(dpiConfidence: DpiConfidence.None));
        _ = Assert.Throws<System.ArgumentOutOfRangeException>(
            () => _ = CreateMetrics(dpiConfidence: (DpiConfidence)int.MaxValue));
        _ = Assert.Throws<System.ArgumentException>(
            () => _ = CreateMetrics(effectiveDpi: null));
        _ = Assert.Throws<System.ArgumentException>(
            () => _ = CreateMetrics(
                contentScale: new Vector2(1.25f),
                effectiveDpi: null,
                dpiSource: DpiSource.Unknown,
                dpiConfidence: DpiConfidence.Assumed96Dpi));
    }

    [Test]
    public void ReportedDpiRetainsSourceExtentsAndIndependentEpochs()
    {
        var metrics = new WindowMetrics(
            new Vector2(1280, 720),
            new PixelSize(1920, 1080),
            new Vector2(1.5f),
            144.0f,
            DpiSource.WindowsPerMonitorV2,
            DpiConfidence.Reported,
            surfaceEpoch: 2,
            swapchainEpoch: 4);

        Assert.That(metrics.LogicalClientSize, Is.EqualTo(new Vector2(1280, 720)));
        Assert.That(metrics.ClientPixelSize, Is.EqualTo(new PixelSize(1920, 1080)));
        Assert.That(metrics.ContentScale, Is.EqualTo(new Vector2(1.5f)));
        Assert.That(metrics.EffectiveDpi, Is.EqualTo(144.0f));
        Assert.That(metrics.DpiSource, Is.EqualTo(DpiSource.WindowsPerMonitorV2));
        Assert.That(metrics.DpiConfidence, Is.EqualTo(DpiConfidence.Reported));
        Assert.That(metrics.SurfaceEpoch, Is.EqualTo(2));
        Assert.That(metrics.SwapchainEpoch, Is.EqualTo(4));
    }
}
