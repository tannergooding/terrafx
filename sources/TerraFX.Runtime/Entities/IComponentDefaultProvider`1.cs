// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Creates one fresh component value from its declared defaults.</summary>
/// <typeparam name="T">The unmanaged component type.</typeparam>
public interface IComponentDefaultProvider<T>
    where T : unmanaged
{
    /// <summary>Creates one default component value using the engine-issued binding.</summary>
    /// <param name="binding">The operation-scoped default binding.</param>
    /// <returns>The initialized component value.</returns>
    T Create(ComponentDefaultBinding binding);
}
