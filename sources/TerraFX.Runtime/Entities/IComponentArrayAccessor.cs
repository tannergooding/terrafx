// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Reads and writes a fixed-length unmanaged component array.</summary>
/// <typeparam name="T">The unmanaged component type.</typeparam>
/// <typeparam name="TElement">The unmanaged array element type.</typeparam>
public interface IComponentArrayAccessor<T, TElement> :
    IComponentArrayReader<T, TElement>
    where T : unmanaged
{
    /// <summary>Writes one array element.</summary>
    /// <param name="value">The component value to update.</param>
    /// <param name="index">The zero-based element index.</param>
    /// <param name="element">The replacement array element.</param>
    void SetElement(ref T value, int index, in TElement element);
}
