// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Pooling;

/// <summary>Defines creation, reset, and terminal cleanup for objects owned by an <see cref="ObjectPool{T}"/>.</summary>
/// <typeparam name="T">The reusable reference type.</typeparam>
public interface IPoolPolicy<T>
    where T : class
{
    /// <summary>Creates one fully initialized object for the pool.</summary>
    /// <returns>A new object that is not currently owned by the pool.</returns>
    T Create();

    /// <summary>Releases the terminal resources held by an object.</summary>
    /// <param name="value">The object being permanently removed from the pool.</param>
    void Destroy(T value);

    /// <summary>Restores an object to its reusable state.</summary>
    /// <param name="value">The object being returned to the pool.</param>
    void Reset(T value);
}
