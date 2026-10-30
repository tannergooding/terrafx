// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Describes one exact typed system source-state leaf.</summary>
public sealed class WorldSystemSourceLeaf
{
    /// <summary>Initializes one source-state leaf declaration.</summary>
    /// <param name="path">The exact source member path.</param>
    /// <param name="valueType">The closed CLR type of the leaf.</param>
    /// <param name="purpose">The persistence role assigned to the leaf.</param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="purpose"/> is not defined.</exception>
    public WorldSystemSourceLeaf(
        WorldSystemSourcePath path,
        Type valueType,
        WorldSystemSourcePurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(valueType);

        if (purpose is < WorldSystemSourcePurpose.Authoritative or > WorldSystemSourcePurpose.ImmutableConfiguration)
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }

        Path = path;
        ValueType = valueType;
        Purpose = purpose;
    }

    /// <summary>Gets the exact source member path.</summary>
    public WorldSystemSourcePath Path { get; }

    /// <summary>Gets the selected persistence role.</summary>
    public WorldSystemSourcePurpose Purpose { get; }

    /// <summary>Gets the exact closed CLR value type.</summary>
    public Type ValueType { get; }
}
