// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Reads the presence and value of one optional system or author field.</summary>
/// <typeparam name="TSource">The system or immutable symbolic author owner.</typeparam>
/// <typeparam name="TValue">The unmanaged field value.</typeparam>
public interface IWorldSystemOptionalReader<TSource, TValue>
    where TSource : notnull
    where TValue : unmanaged
{
    /// <summary>Reads the selected value when the optional field is present.</summary>
    /// <param name="source">The immutable value owner.</param>
    /// <returns>The present field value.</returns>
    TValue GetPresent(in TSource source);

    /// <summary>Gets whether the optional field is present.</summary>
    /// <param name="source">The immutable value owner.</param>
    /// <returns><see langword="true"/> when the field is present.</returns>
    bool HasValue(in TSource source);
}
