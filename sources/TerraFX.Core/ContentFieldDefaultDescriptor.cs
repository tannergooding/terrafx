// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX;

/// <summary>Describes one immutable authoring default for a content field.</summary>
public sealed class ContentFieldDefaultDescriptor
{
    private readonly ImmutableArray<byte> _literalPayload;
    private readonly ImmutableArray<long> _meterNumerators;

    private ContentFieldDefaultDescriptor(
        ContentFieldDefaultKind kind,
        ushort wireKind,
        ulong count,
        ImmutableArray<byte> literalPayload,
        ImmutableArray<long> meterNumerators,
        ulong meterDenominator,
        ContentMaterialDefaultProperty materialProperty)
    {
        Kind = kind;
        WireKind = wireKind;
        Count = count;
        _literalPayload = literalPayload;
        _meterNumerators = meterNumerators;
        MeterDenominator = meterDenominator;
        MaterialProperty = materialProperty;
    }

    /// <summary>Gets the default element count.</summary>
    public ulong Count { get; }

    /// <summary>Gets the default representation.</summary>
    public ContentFieldDefaultKind Kind { get; }

    /// <summary>Gets the exact encoded payload for a literal default.</summary>
    public ImmutableArray<byte> LiteralPayload => _literalPayload;

    /// <summary>Gets the selected material property for a material default.</summary>
    public ContentMaterialDefaultProperty MaterialProperty { get; }

    /// <summary>Gets the positive denominator for a rational meter default.</summary>
    public ulong MeterDenominator { get; }

    /// <summary>Gets the exact rational numerators for a meter default.</summary>
    public ImmutableArray<long> MeterNumerators => _meterNumerators;

    /// <summary>Gets the wire kind of a literal default.</summary>
    public ushort WireKind { get; }

    /// <summary>Creates a material-property default.</summary>
    /// <param name="property">The material property to initialize.</param>
    /// <returns>An immutable material-property default.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="property"/> is invalid.</exception>
    public static ContentFieldDefaultDescriptor ForMaterialProperty(ContentMaterialDefaultProperty property)
    {
        if (property is < ContentMaterialDefaultProperty.AlphaMode or > ContentMaterialDefaultProperty.Capabilities)
        {
            throw new ArgumentOutOfRangeException(nameof(property));
        }

        return new ContentFieldDefaultDescriptor(
            ContentFieldDefaultKind.MaterialProperty,
            0,
            1,
            [],
            [],
            0,
            property);
    }

    /// <summary>Creates a literal default from canonical primitive payload bytes.</summary>
    /// <param name="wireKind">The fixed primitive wire kind.</param>
    /// <param name="count">The wire element or byte count.</param>
    /// <param name="payload">The canonical primitive payload bytes.</param>
    /// <returns>An immutable literal default.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="wireKind"/> is not a supported primitive kind.</exception>
    /// <exception cref="ArgumentException"><paramref name="payload"/> is malformed for the selected wire kind and count.</exception>
    public static ContentFieldDefaultDescriptor Literal(ushort wireKind, ulong count, scoped ReadOnlySpan<byte> payload)
    {
        if (wireKind is 0 or > 21)
        {
            throw new ArgumentOutOfRangeException(nameof(wireKind));
        }

        var status = EbinCodec.ScanPayload(wireKind, count, checked((ulong)payload.Length), payload, 0, out var diagnostic);

        if (status != EbinStatus.Complete)
        {
            throw new ArgumentException($"The literal default is invalid: {diagnostic.Category}.", nameof(payload));
        }

        return new ContentFieldDefaultDescriptor(
            ContentFieldDefaultKind.Literal,
            wireKind,
            count,
            [.. payload],
            [],
            0,
            default);
    }

    /// <summary>Creates an exact rational meter default.</summary>
    /// <param name="numerators">The signed numerators, one per geometry lane.</param>
    /// <param name="denominator">The positive denominator.</param>
    /// <returns>An immutable rational meter default.</returns>
    /// <exception cref="ArgumentException"><paramref name="numerators"/> is default or has an unsupported lane count.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="denominator"/> is zero.</exception>
    public static ContentFieldDefaultDescriptor Meters(ImmutableArray<long> numerators, ulong denominator)
    {
        if (numerators.IsDefault || (numerators.Length is < 1 or > 3))
        {
            throw new ArgumentException("A rational meter default requires one through three initialized numerators.", nameof(numerators));
        }

        ArgumentOutOfRangeException.ThrowIfZero(denominator);

        return new ContentFieldDefaultDescriptor(
            ContentFieldDefaultKind.Meters,
            0,
            1,
            [],
            [.. numerators],
            denominator,
            default);
    }
}
