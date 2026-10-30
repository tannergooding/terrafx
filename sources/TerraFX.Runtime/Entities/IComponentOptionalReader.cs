// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Reads the presence and value of one optional field.</summary>
/// <typeparam name="TValueOwner">The immutable author or runtime owner.</typeparam>
/// <typeparam name="TValue">The unmanaged field value.</typeparam>
public interface IComponentOptionalReader<TValueOwner, TValue>
{
    /// <summary>Reads the present optional value.</summary>
    /// <param name="value">The immutable value owner.</param>
    /// <returns>The selected field value.</returns>
    TValue GetPresent(in TValueOwner value);

    /// <summary>Gets whether the optional field is present.</summary>
    /// <param name="value">The immutable value owner.</param>
    /// <returns><see langword="true"/> when the field is present.</returns>
    bool IsPresent(in TValueOwner value);
}
