// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;

namespace TerraFX.Entities;

/// <summary>Defines one typed component value or declared-default request for entity creation.</summary>
public abstract class ComponentInitialization
{
    private readonly Type _componentType;

    private protected ComponentInitialization(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        _componentType = componentType;
    }

    internal Type ComponentType => _componentType;

    internal abstract bool IsDefault { get; }

    /// <summary>Creates one request to initialize T through its registered default provider.</summary>
    /// <typeparam name="T">The unmanaged component type.</typeparam>
    /// <returns>The immutable default initialization request.</returns>
    public static ComponentInitialization Defaults<T>()
        where T : unmanaged
        => new DefaultValue<T>();

    /// <summary>Creates one request containing a copied typed component value.</summary>
    /// <typeparam name="T">The unmanaged component type.</typeparam>
    /// <param name="value">The copied component value.</param>
    /// <returns>The immutable value initialization request.</returns>
    public static ComponentInitialization Value<T>(in T value)
        where T : unmanaged
        => new ExplicitValue<T>(value);

    internal abstract bool TryGetValue<TValue>(out TValue value)
        where TValue : unmanaged;

    private sealed class DefaultValue<T> : ComponentInitialization
        where T : unmanaged
    {
        internal DefaultValue()
            : base(typeof(T))
        {
        }

        internal override bool IsDefault => true;

        internal override bool TryGetValue<TValue>(out TValue value)
        {
            value = default;
            return false;
        }
    }

    private sealed class ExplicitValue<T> : ComponentInitialization
        where T : unmanaged
    {
        private readonly T _value;

        internal ExplicitValue(in T value)
            : base(typeof(T))
        {
            _value = value;
        }

        internal override bool IsDefault => false;

        internal override bool TryGetValue<TValue>(out TValue value)
        {
            if (typeof(TValue) != typeof(T))
            {
                value = default;
                return false;
            }

            value = Unsafe.As<T, TValue>(ref Unsafe.AsRef(in _value));
            return true;
        }
    }
}
