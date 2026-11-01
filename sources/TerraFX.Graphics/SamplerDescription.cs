// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Describes immutable filtering, addressing, and anisotropy requests for one GPU sampler.</summary>
public sealed class SamplerDescription
{
    /// <summary>Initializes a validated GPU sampler description.</summary>
    /// <param name="minification">The required minification filter.</param>
    /// <param name="magnification">The required magnification filter.</param>
    /// <param name="mip">The mip filter, or <see cref="SamplerFilter.None"/> when mip sampling is disabled.</param>
    /// <param name="addressU">The U-coordinate address mode.</param>
    /// <param name="addressV">The V-coordinate address mode.</param>
    /// <param name="addressW">The W-coordinate address mode.</param>
    /// <param name="maximumAnisotropy">The finite anisotropy upper bound requested by the caller.</param>
    /// <exception cref="ArgumentOutOfRangeException">A filter, address mode, or anisotropy value is invalid.</exception>
    public SamplerDescription(
        SamplerFilter minification,
        SamplerFilter magnification,
        SamplerFilter mip,
        SamplerAddressMode addressU,
        SamplerAddressMode addressV,
        SamplerAddressMode addressW,
        float maximumAnisotropy)
    {
        if (minification is not SamplerFilter.Nearest and not SamplerFilter.Linear)
        {
            throw new ArgumentOutOfRangeException(nameof(minification));
        }

        if (magnification is not SamplerFilter.Nearest and not SamplerFilter.Linear)
        {
            throw new ArgumentOutOfRangeException(nameof(magnification));
        }

        if (!Enum.IsDefined(mip))
        {
            throw new ArgumentOutOfRangeException(nameof(mip));
        }

        if (!Enum.IsDefined(addressU))
        {
            throw new ArgumentOutOfRangeException(nameof(addressU));
        }

        if (!Enum.IsDefined(addressV))
        {
            throw new ArgumentOutOfRangeException(nameof(addressV));
        }

        if (!Enum.IsDefined(addressW))
        {
            throw new ArgumentOutOfRangeException(nameof(addressW));
        }

        if (!float.IsFinite(maximumAnisotropy) || (maximumAnisotropy < 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAnisotropy));
        }

        AddressU = addressU;
        AddressV = addressV;
        AddressW = addressW;
        Magnification = magnification;
        MaximumAnisotropy = maximumAnisotropy;
        Minification = minification;
        Mip = mip;
    }

    /// <summary>Gets the U-coordinate address mode.</summary>
    public SamplerAddressMode AddressU { get; }

    /// <summary>Gets the V-coordinate address mode.</summary>
    public SamplerAddressMode AddressV { get; }

    /// <summary>Gets the W-coordinate address mode.</summary>
    public SamplerAddressMode AddressW { get; }

    /// <summary>Gets the magnification filter.</summary>
    public SamplerFilter Magnification { get; }

    /// <summary>Gets the requested anisotropy upper bound; device creation clamps it to the reported device limit.</summary>
    public float MaximumAnisotropy { get; }

    /// <summary>Gets the minification filter.</summary>
    public SamplerFilter Minification { get; }

    /// <summary>Gets the mip filter.</summary>
    public SamplerFilter Mip { get; }
}
