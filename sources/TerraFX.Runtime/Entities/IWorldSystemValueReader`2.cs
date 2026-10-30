// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Reads one required value from an immutable system or author owner.</summary>
/// <typeparam name="TSource">The system or immutable symbolic author owner.</typeparam>
/// <typeparam name="TValue">The unmanaged field value.</typeparam>
public interface IWorldSystemValueReader<TSource, TValue>
    where TSource : notnull
    where TValue : unmanaged
{
    /// <summary>Reads the required value.</summary>
    /// <param name="source">The immutable value owner.</param>
    /// <returns>The selected field value.</returns>
    TValue Invoke(in TSource source);
}
