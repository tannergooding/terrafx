// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Assets;

/// <summary>Reads or writes one schema version whose semantics depend on trusted package context.</summary>
/// <typeparam name="T">The immutable semantic value type.</typeparam>
public interface IContextualBinarySchemaCodec<T>
{
    /// <summary>Gets the stable schema identity.</summary>
    Guid SchemaId { get; }

    /// <summary>Gets the positive schema major version.</summary>
    int SchemaMajor { get; }

    /// <summary>Gets the nonnegative schema minor version.</summary>
    int SchemaMinor { get; }

    /// <summary>Reads one complete typed record using the trusted asset context.</summary>
    /// <param name="source">The encoded record prefix.</param>
    /// <param name="context">The trusted content-set context.</param>
    /// <param name="value">The decoded value, or its default value when not complete.</param>
    /// <param name="bytesConsumed">The complete record length, or zero when incomplete or invalid.</param>
    /// <param name="diagnostic">The schema failure, when present.</param>
    /// <returns>The record result.</returns>
    BinaryRecordStatus Read(
        scoped ReadOnlySpan<byte> source,
        AssetCodecContext context,
        [MaybeNull] out T value,
        out int bytesConsumed,
        out BinarySchemaDiagnostic? diagnostic);

    /// <summary>Writes one complete typed record using the trusted asset context.</summary>
    /// <param name="value">The immutable semantic value.</param>
    /// <param name="context">The trusted content-set context.</param>
    /// <param name="destination">The destination span.</param>
    /// <param name="bytesWritten">The complete record length, or zero when the destination is too small or invalid.</param>
    /// <param name="diagnostic">The schema failure, when present.</param>
    /// <returns>The record result.</returns>
    BinaryRecordStatus Write(
        in T value,
        AssetCodecContext context,
        scoped Span<byte> destination,
        out int bytesWritten,
        out BinarySchemaDiagnostic? diagnostic);
}
