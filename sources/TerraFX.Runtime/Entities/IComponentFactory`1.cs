// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Provides the immutable component factory binding captured at World registration.</summary>
/// <typeparam name="T">The unmanaged component type.</typeparam>
public interface IComponentFactory<T>
    where T : unmanaged
{
    /// <summary>Gets the closed component factory binding.</summary>
    ComponentFactoryBinding<T> Binding { get; }
}
