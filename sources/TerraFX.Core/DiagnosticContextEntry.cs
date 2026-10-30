// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Stores one immutable key/value diagnostic context entry.</summary>
public readonly struct DiagnosticContextEntry : IEquatable<DiagnosticContextEntry>
{
    /// <summary>Initializes a diagnostic context entry.</summary>
    /// <param name="key">The nonempty context key.</param>
    /// <param name="value">The context value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="value"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
    public DiagnosticContextEntry(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        if (key.Length == 0)
        {
            throw new ArgumentException("A diagnostic context key cannot be empty.", nameof(key));
        }

        Key = key;
        Value = value;
    }

    /// <summary>Gets the context key.</summary>
    public string Key { get; }

    /// <summary>Gets the context value.</summary>
    public string Value { get; }

    /// <summary>Negates equality of the complete context entry.</summary>
    public static bool operator !=(DiagnosticContextEntry left, DiagnosticContextEntry right) => !(left == right);

    /// <summary>Compares the complete context entry.</summary>
    public static bool operator ==(DiagnosticContextEntry left, DiagnosticContextEntry right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(DiagnosticContextEntry other)
        => StringComparer.Ordinal.Equals(Key, other.Key) && StringComparer.Ordinal.Equals(Value, other.Value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DiagnosticContextEntry other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(Key is null ? 0 : StringComparer.Ordinal.GetHashCode(Key), Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value));
}
