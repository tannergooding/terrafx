// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Assets;

/// <summary>Reads or writes one registered context-independent schema version.</summary>
/// <typeparam name="T">The immutable semantic value type.</typeparam>
public interface IBinarySchemaCodec<T>
{
    /// <summary>Gets the stable schema identity.</summary>
    Guid SchemaId { get; }

    /// <summary>Gets the positive schema major version.</summary>
    int SchemaMajor { get; }

    /// <summary>Gets the nonnegative schema minor version.</summary>
    int SchemaMinor { get; }

    /// <summary>Reads one complete typed record.</summary>
    /// <param name="source">The encoded record prefix.</param>
    /// <param name="value">The decoded value, or its default value when not complete.</param>
    /// <param name="bytesConsumed">The complete record length, or zero when incomplete or invalid.</param>
    /// <returns>The record result.</returns>
    BinaryRecordStatus Read(scoped ReadOnlySpan<byte> source, [MaybeNull] out T value, out int bytesConsumed);

    /// <summary>Writes one complete typed record.</summary>
    /// <param name="value">The immutable semantic value.</param>
    /// <param name="destination">The destination span.</param>
    /// <param name="bytesWritten">The complete record length, or zero when the destination is too small or invalid.</param>
    /// <returns>The record result.</returns>
    BinaryRecordStatus Write(in T value, scoped Span<byte> destination, out int bytesWritten);
}
