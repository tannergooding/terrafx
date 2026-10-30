// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Supplies one captured registration binding for a World resource state codec.</summary>
/// <typeparam name="T">The unmanaged resource partition type.</typeparam>
public interface IWorldResourceStateCodec<T>
    where T : unmanaged
{
    /// <summary>Gets the immutable resource codec binding.</summary>
    WorldResourceCodecBinding<T> Binding { get; }
}
