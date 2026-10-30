// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Declares the complete closed source-state shape of one World system type.</summary>
public sealed class WorldSystemSourceStateDeclaration
{
    /// <summary>Initializes an immutable system source-state declaration.</summary>
    /// <param name="systemType">The exact closed concrete system type.</param>
    /// <param name="leaves">The complete source-state leaf declarations.</param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException">The type is not closed or the leaf set is invalid.</exception>
    public WorldSystemSourceStateDeclaration(Type systemType, ImmutableArray<WorldSystemSourceLeaf> leaves)
    {
        ArgumentNullException.ThrowIfNull(systemType);

        if (systemType.ContainsGenericParameters)
        {
            throw new ArgumentException("A system source declaration requires a closed system type.", nameof(systemType));
        }

        if (leaves.IsDefault)
        {
            throw new ArgumentException("The system source leaf array must be initialized.", nameof(leaves));
        }

        for (var index = 0; index < leaves.Length; index++)
        {
            var leaf = leaves[index] ?? throw new ArgumentException("System source leaves cannot contain null entries.", nameof(leaves));

            for (var previousIndex = 0; previousIndex < index; previousIndex++)
            {
                if (leaf.Path.Equals(leaves[previousIndex].Path))
                {
                    throw new ArgumentException("System source paths must be unique.", nameof(leaves));
                }
            }
        }

        SystemType = systemType;
        Leaves = ImmutableArrayCopy.Create(leaves);
    }

    /// <summary>Gets the immutable source-state leaf set.</summary>
    public ImmutableArray<WorldSystemSourceLeaf> Leaves { get; }

    /// <summary>Gets the exact closed concrete system type.</summary>
    public Type SystemType { get; }
}
