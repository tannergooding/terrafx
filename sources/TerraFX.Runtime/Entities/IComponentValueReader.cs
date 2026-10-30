// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Reads one required value from an immutable owner.</summary>
/// <typeparam name="TValueOwner">The immutable author or runtime owner.</typeparam>
/// <typeparam name="TValue">The unmanaged field value.</typeparam>
public interface IComponentValueReader<TValueOwner, TValue>
{
    /// <summary>Reads the required value.</summary>
    /// <param name="value">The immutable value owner.</param>
    /// <returns>The selected field value.</returns>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "The reviewed component accessor contract uses this required member name.")]
    TValue Get(in TValueOwner value);
}
