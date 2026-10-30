// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Reads and writes one required unmanaged component value.</summary>
/// <typeparam name="T">The unmanaged component type.</typeparam>
/// <typeparam name="TValue">The unmanaged field value.</typeparam>
public interface IComponentValueAccessor<T, TValue> :
    IComponentValueReader<T, TValue>
    where T : unmanaged
{
    /// <summary>Writes the required field value.</summary>
    /// <param name="value">The component value to update.</param>
    /// <param name="fieldValue">The replacement field value.</param>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "The reviewed component accessor contract uses this required member name.")]
    void Set(ref T value, in TValue fieldValue);
}
