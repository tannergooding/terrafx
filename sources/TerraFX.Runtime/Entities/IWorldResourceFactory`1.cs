// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Supplies one captured typed factory binding for a World resource.</summary>
/// <typeparam name="T">The unmanaged resource partition type.</typeparam>
public interface IWorldResourceFactory<T>
    where T : unmanaged
{
    /// <summary>Gets the immutable resource factory binding.</summary>
    WorldResourceFactoryBinding<T> Binding { get; }
}
