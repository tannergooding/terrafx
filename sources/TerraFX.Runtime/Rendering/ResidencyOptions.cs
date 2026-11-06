// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

/// <summary>Defines the shader and transfer uses requested for a resident Runtime texture.</summary>
public sealed class ResidencyOptions
{
    private const TextureUsage SupportedUsages = TextureUsage.Sampled | TextureUsage.CopySource;

    /// <summary>Initializes texture residency with shader sampling and optional copy-source use.</summary>
    /// <param name="usage">The consumer uses; sampling is required and copy-source use is optional.</param>
    /// <exception cref="ArgumentOutOfRangeException">Sampling is absent or the usage contains an unsupported flag.</exception>
    public ResidencyOptions(TextureUsage usage = TextureUsage.Sampled)
    {
        if (((usage & TextureUsage.Sampled) == 0) || ((usage & ~SupportedUsages) != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(usage));
        }

        Usage = usage;
    }

    /// <summary>Gets the consumer uses requested for the resident texture.</summary>
    public TextureUsage Usage { get; }
}
