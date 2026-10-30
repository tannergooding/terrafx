// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Reads and writes one optional unmanaged component value.</summary>
/// <typeparam name="T">The unmanaged component type.</typeparam>
/// <typeparam name="TValue">The unmanaged field value.</typeparam>
public interface IComponentOptionalAccessor<T, TValue> :
    IComponentOptionalReader<T, TValue>
    where T : unmanaged
{
    /// <summary>Clears the optional field.</summary>
    /// <param name="value">The component value to update.</param>
    void Clear(ref T value);

    /// <summary>Sets the optional field to a present value.</summary>
    /// <param name="value">The component value to update.</param>
    /// <param name="fieldValue">The replacement field value.</param>
    void SetPresent(ref T value, in TValue fieldValue);
}
