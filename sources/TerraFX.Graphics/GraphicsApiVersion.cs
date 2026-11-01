// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Identifies one graphics API or driver version.</summary>
public readonly struct GraphicsApiVersion : IEquatable<GraphicsApiVersion>
{
    /// <summary>Initializes a graphics API version.</summary>
    /// <param name="major">The nonzero major version.</param>
    /// <param name="minor">The minor version.</param>
    /// <param name="patch">The patch version.</param>
    /// <param name="variant">The API-specific variant.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="major"/> is zero.</exception>
    internal GraphicsApiVersion(uint major, uint minor, uint patch, uint variant)
    {
        ArgumentOutOfRangeException.ThrowIfZero(major);
        Major = major;
        Minor = minor;
        Patch = patch;
        Variant = variant;
    }

    /// <summary>Gets the major version.</summary>
    public uint Major { get; }

    /// <summary>Gets the minor version.</summary>
    public uint Minor { get; }

    /// <summary>Gets the patch version.</summary>
    public uint Patch { get; }

    /// <summary>Gets the API-specific variant.</summary>
    public uint Variant { get; }

    /// <summary>Negates version equality.</summary>
    public static bool operator !=(GraphicsApiVersion left, GraphicsApiVersion right)
        => !left.Equals(right);

    /// <summary>Compares graphics API versions.</summary>
    public static bool operator ==(GraphicsApiVersion left, GraphicsApiVersion right)
        => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(GraphicsApiVersion other)
        => (Major == other.Major) && (Minor == other.Minor) && (Patch == other.Patch) && (Variant == other.Variant);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => (obj is GraphicsApiVersion other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Major, Minor, Patch, Variant);
}
