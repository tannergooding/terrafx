// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Pooling;

/// <summary>Identifies one rental from its exact <see cref="ObjectPool{T}"/>.</summary>
/// <typeparam name="T">The reusable reference type.</typeparam>
[SuppressMessage("Performance", "CA1815", Justification = "The rental token is intentionally opaque; token equality is not part of its accepted public contract.")]
public readonly struct PoolRental<T>
    where T : class
{
    private readonly ObjectPool<T>? _owner;
    private readonly int _slot;
    private readonly uint _generation;

    internal PoolRental(ObjectPool<T> owner, int slot, uint generation)
    {
        _owner = owner;
        _slot = slot;
        _generation = generation;
    }

    internal uint Generation => _generation;

    internal ObjectPool<T>? Owner => _owner;

    internal int Slot => _slot;
}
