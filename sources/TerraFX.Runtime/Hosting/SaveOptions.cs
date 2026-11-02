// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;

namespace TerraFX.Hosting;

/// <summary>Defines one absolute checkpoint destination and its durability policy.</summary>
public sealed class SaveOptions
{
    /// <summary>Initializes one save destination.</summary>
    /// <param name="absoluteSavePath">The absolute checkpoint file path.</param>
    /// <param name="durabilityPolicy">The required or explicitly best-effort replacement policy.</param>
    /// <exception cref="ArgumentException"><paramref name="absoluteSavePath"/> is empty or not absolute.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="durabilityPolicy"/> is invalid.</exception>
    public SaveOptions(
        string absoluteSavePath,
        SaveDurabilityPolicy durabilityPolicy = SaveDurabilityPolicy.RequireDurable)
    {
        ArgumentException.ThrowIfNullOrEmpty(absoluteSavePath);

        if (!Path.IsPathFullyQualified(absoluteSavePath))
        {
            throw new ArgumentException("The save path must be absolute.", nameof(absoluteSavePath));
        }

        if (durabilityPolicy is not (SaveDurabilityPolicy.RequireDurable or SaveDurabilityPolicy.BestEffort))
        {
            throw new ArgumentOutOfRangeException(nameof(durabilityPolicy));
        }

        AbsoluteSavePath = absoluteSavePath;
        DurabilityPolicy = durabilityPolicy;
    }

    /// <summary>Gets the absolute checkpoint destination path.</summary>
    public string AbsoluteSavePath { get; }

    /// <summary>Gets the required durability policy.</summary>
    public SaveDurabilityPolicy DurabilityPolicy { get; }
}
