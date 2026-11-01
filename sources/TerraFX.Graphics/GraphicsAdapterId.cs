// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

/// <summary>Contains one backend-tagged native graphics adapter identity.</summary>
public readonly struct GraphicsAdapterId : IEquatable<GraphicsAdapterId>
{
    private readonly ImmutableArray<byte> _nativeIdentifier;

    internal GraphicsAdapterId(GraphicsBackend backend, ReadOnlySpan<byte> nativeIdentifier)
    {
        if ((backend == GraphicsBackend.None) || !Enum.IsDefined(backend))
        {
            throw new ArgumentOutOfRangeException(nameof(backend));
        }

        var requiredLength = GetRequiredLength(backend);

        if (nativeIdentifier.Length != requiredLength)
        {
            throw new ArgumentException($"A {backend} adapter identifier must contain exactly {requiredLength} native bytes.", nameof(nativeIdentifier));
        }

        Backend = backend;
        _nativeIdentifier = [.. nativeIdentifier];
    }

    /// <summary>Gets the backend that defines the native identifier representation.</summary>
    public GraphicsBackend Backend { get; }

    /// <summary>Gets whether this value contains a valid backend-native identity.</summary>
    public bool IsValid
        => (Backend != GraphicsBackend.None)
            && Enum.IsDefined(Backend)
            && !_nativeIdentifier.IsDefaultOrEmpty
            && (_nativeIdentifier.Length == GetRequiredLength(Backend));

    /// <summary>Gets the copied native identifier bytes.</summary>
    public ImmutableArray<byte> NativeIdentifier => _nativeIdentifier;

    /// <summary>Tests whether two adapter identities differ.</summary>
    public static bool operator !=(GraphicsAdapterId left, GraphicsAdapterId right)
        => !left.Equals(right);

    /// <summary>Tests whether two adapter identities have the same backend and native bytes.</summary>
    public static bool operator ==(GraphicsAdapterId left, GraphicsAdapterId right)
        => left.Equals(right);

    private static int GetRequiredLength(GraphicsBackend backend)
        => backend switch {
            GraphicsBackend.D3D12 => sizeof(ulong),
            GraphicsBackend.Vulkan => 16,
            _ => throw new ArgumentOutOfRangeException(nameof(backend)),
        };

    /// <inheritdoc />
    public bool Equals(GraphicsAdapterId other)
        => (Backend == other.Backend)
            && _nativeIdentifier.AsSpan().SequenceEqual(other._nativeIdentifier.AsSpan());

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => (obj is GraphicsAdapterId other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Backend);

        for (var index = 0; index < _nativeIdentifier.Length; index++)
        {
            hash.Add(_nativeIdentifier[index]);
        }

        return hash.ToHashCode();
    }
}
