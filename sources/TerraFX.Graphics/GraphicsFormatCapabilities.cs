// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace TerraFX.Graphics;

/// <summary>Contains the actual usages and sample counts supported for one texture format.</summary>
public sealed class GraphicsFormatCapabilities
{
    private readonly ImmutableArray<int> _sampleCounts;

    internal GraphicsFormatCapabilities(
        TextureFormat format,
        TextureUsage supportedUsages,
        ImmutableArray<int> sampleCounts)
    {
        if ((format == TextureFormat.None) || !Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        const TextureUsage knownUsages =
            TextureUsage.Sampled
            | TextureUsage.Storage
            | TextureUsage.ColorAttachment
            | TextureUsage.DepthAttachment
            | TextureUsage.CopySource
            | TextureUsage.CopyDestination;

        if ((supportedUsages == TextureUsage.None) || ((supportedUsages & ~knownUsages) != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(supportedUsages));
        }

        if (sampleCounts.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Graphics format sample counts must be initialized and nonempty.", nameof(sampleCounts));
        }

        var sortedSampleCounts = sampleCounts.ToArray();
        Array.Sort(sortedSampleCounts);

        for (var index = 0; index < sortedSampleCounts.Length; index++)
        {
            var sampleCount = sortedSampleCounts[index];

            if (sampleCount is <= 0 or > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleCounts));
            }

            if (((sampleCount & (sampleCount - 1)) != 0)
                || ((index > 0) && (sampleCount == sortedSampleCounts[index - 1])))
            {
                throw new ArgumentException("Graphics format sample counts must be unique positive powers of two.", nameof(sampleCounts));
            }
        }

        Format = format;
        SupportedUsages = supportedUsages;
        _sampleCounts = [.. sortedSampleCounts];
    }

    /// <summary>Gets the texture format.</summary>
    public TextureFormat Format { get; }

    /// <summary>Gets the supported sample counts in ascending order.</summary>
    public ImmutableArray<int> SampleCounts => _sampleCounts;

    /// <summary>Gets the supported resource usages.</summary>
    public TextureUsage SupportedUsages { get; }
}
