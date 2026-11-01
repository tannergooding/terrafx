// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX;

/// <summary>Contains an immutable window drawable extent and its independent native target generations.</summary>
public sealed class WindowMetrics
{
    /// <summary>Initializes a window metrics snapshot.</summary>
    /// <param name="logicalClientSize">The finite nonnegative client extent in logical units.</param>
    /// <param name="clientPixelSize">The physical drawable extent; zero dimensions represent a non-drawable window.</param>
    /// <param name="contentScale">The finite positive logical-to-pixel scale.</param>
    /// <param name="effectiveDpi">The reported effective DPI, or null for the assumed 96-DPI baseline.</param>
    /// <param name="dpiSource">The source of the DPI conversion value.</param>
    /// <param name="dpiConfidence">Whether the value was reported or assumed.</param>
    /// <param name="surfaceEpoch">The native presentation-target generation, or zero before creation.</param>
    /// <param name="swapchainEpoch">The back-buffer/image generation, or zero before creation.</param>
    /// <exception cref="ArgumentException">The DPI source, confidence, effective value, and scale disagree.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An extent, scale, DPI, or enum value is invalid.</exception>
    internal WindowMetrics(
        Vector2 logicalClientSize,
        PixelSize clientPixelSize,
        Vector2 contentScale,
        float? effectiveDpi,
        DpiSource dpiSource,
        DpiConfidence dpiConfidence,
        uint surfaceEpoch,
        uint swapchainEpoch)
    {
        if (!logicalClientSize.IsFinite().All()
            || (logicalClientSize.X < 0.0f)
            || (logicalClientSize.Y < 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(logicalClientSize));
        }

        if (!contentScale.IsFinite().All()
            || (contentScale.X <= 0.0f)
            || (contentScale.Y <= 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(contentScale));
        }

        if ((effectiveDpi is float dpi) && (!float.IsFinite(dpi) || (dpi <= 0.0f)))
        {
            throw new ArgumentOutOfRangeException(nameof(effectiveDpi));
        }

        if (!Enum.IsDefined(dpiSource))
        {
            throw new ArgumentOutOfRangeException(nameof(dpiSource));
        }

        if ((dpiConfidence == DpiConfidence.None) || !Enum.IsDefined(dpiConfidence))
        {
            throw new ArgumentOutOfRangeException(nameof(dpiConfidence));
        }

        if (dpiConfidence == DpiConfidence.Assumed96Dpi)
        {
            if ((dpiSource != DpiSource.Unknown)
                || (effectiveDpi is not null)
                || (contentScale.X != 1.0f)
                || (contentScale.Y != 1.0f))
            {
                throw new ArgumentException("The assumed 96-DPI fallback requires an unknown source, null effective DPI, and unit scale.");
            }
        }
        else if ((dpiSource == DpiSource.Unknown) || (effectiveDpi is null))
        {
            throw new ArgumentException("A reported DPI value requires a known source and an effective DPI value.");
        }

        LogicalClientSize = logicalClientSize;
        ClientPixelSize = clientPixelSize;
        ContentScale = contentScale;
        EffectiveDpi = effectiveDpi;
        DpiSource = dpiSource;
        DpiConfidence = dpiConfidence;
        SurfaceEpoch = surfaceEpoch;
        SwapchainEpoch = swapchainEpoch;
    }

    /// <summary>Gets the physical drawable pixel extent.</summary>
    public PixelSize ClientPixelSize { get; }

    /// <summary>Gets the logical-to-pixel scale.</summary>
    public Vector2 ContentScale { get; }

    /// <summary>Gets how the DPI conversion value was obtained.</summary>
    public DpiConfidence DpiConfidence { get; }

    /// <summary>Gets the source of the DPI conversion value.</summary>
    public DpiSource DpiSource { get; }

    /// <summary>Gets the reported effective DPI, or null when the assumed 96-DPI baseline is used.</summary>
    public float? EffectiveDpi { get; }

    /// <summary>Gets the logical client extent; a zero extent suspends presentation.</summary>
    public Vector2 LogicalClientSize { get; }

    /// <summary>Gets the native presentation-target generation, or zero before its first successful creation.</summary>
    public uint SurfaceEpoch { get; }

    /// <summary>Gets the back-buffer/image generation, or zero before its first successful creation.</summary>
    public uint SwapchainEpoch { get; }
}
