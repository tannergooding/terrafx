// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Supplies one captured factory binding for a World system.</summary>
public interface IWorldSystemFactory
{
    /// <summary>Gets the immutable system factory binding.</summary>
    WorldSystemFactoryBinding Binding { get; }
}
