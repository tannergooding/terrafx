// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Creates one default unmanaged resource partition.</summary>
/// <typeparam name="T">The unmanaged resource partition type.</typeparam>
public interface IWorldResourceDefaultProvider<T>
    where T : unmanaged
{
    /// <summary>Creates a default partition value.</summary>
    /// <param name="partitionIndex">The zero-based partition index.</param>
    /// <param name="binding">The immutable resource-default invocation metadata.</param>
    /// <returns>The initialized resource partition value.</returns>
    T Create(int partitionIndex, WorldResourceDefaultBinding binding);
}
