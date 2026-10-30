// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Reads a fixed-length array field from an immutable owner.</summary>
/// <typeparam name="TValueOwner">The immutable author or runtime owner.</typeparam>
/// <typeparam name="TElement">The unmanaged array element type.</typeparam>
public interface IComponentArrayReader<TValueOwner, TElement>
{
    /// <summary>Gets the current element count.</summary>
    /// <param name="value">The immutable value owner.</param>
    /// <returns>The number of elements.</returns>
    int GetCount(in TValueOwner value);

    /// <summary>Gets the selected array element.</summary>
    /// <param name="value">The immutable value owner.</param>
    /// <param name="index">The zero-based element index.</param>
    /// <returns>The selected element.</returns>
    TElement GetElement(in TValueOwner value, int index);
}
