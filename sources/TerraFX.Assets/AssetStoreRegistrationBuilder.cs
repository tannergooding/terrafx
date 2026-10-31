// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Assets;

/// <summary>Builds a closed typed-codec registration for one application schema registry.</summary>
public sealed class AssetStoreRegistrationBuilder
{
    private readonly Dictionary<ContentSchemaReference, AssetStoreRegistration.IAssetCodec> _codecsBySchema = [];
    private readonly Dictionary<Type, AssetStoreRegistration.IAssetCodec> _codecsByType = [];
    private readonly CompiledContentSchemaRegistry _schemas;

    /// <summary>Initializes a registration builder for exact compiled schemas.</summary>
    /// <param name="schemas">The application's immutable schema registry.</param>
    /// <exception cref="ArgumentNullException"><paramref name="schemas"/> is null.</exception>
    public AssetStoreRegistrationBuilder(CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        _schemas = schemas;
    }

    /// <summary>Builds an immutable registration containing the codecs admitted so far.</summary>
    /// <returns>The immutable codec registration.</returns>
    public AssetStoreRegistration Build()
        => AssetStoreRegistration.Create(_schemas, [.. _codecsByType.Values]);

    /// <summary>Registers one context-independent typed codec.</summary>
    /// <typeparam name="T">The codec's immutable semantic value type.</typeparam>
    /// <param name="codec">The typed schema codec.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="codec"/> is null.</exception>
    /// <exception cref="ArgumentException">The codec's schema tuple is absent or conflicts with another registration.</exception>
    public AssetStoreRegistrationBuilder RegisterCodec<T>(IBinarySchemaCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        AddCodec(new BinarySchemaCodecAdapter<T>(codec));
        return this;
    }

    /// <summary>Registers one typed codec that uses trusted package context.</summary>
    /// <typeparam name="T">The codec's immutable semantic value type.</typeparam>
    /// <param name="codec">The contextual schema codec.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="codec"/> is null.</exception>
    /// <exception cref="ArgumentException">The codec's schema tuple is absent or conflicts with another registration.</exception>
    public AssetStoreRegistrationBuilder RegisterCodec<T>(IContextualBinarySchemaCodec<T> codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        AddCodec(new ContextualSchemaCodecAdapter<T>(codec));
        return this;
    }

    private void AddCodec(AssetStoreRegistration.IAssetCodec codec)
    {
        if (!_schemas.TryGetSchema(codec.Schema, out var schema)
            || (schema.Role != ContentSchemaRole.NativeAsset))
        {
            throw new ArgumentException("The codec must match an exact compiled native-asset schema tuple.", nameof(codec));
        }

        if (_codecsBySchema.ContainsKey(codec.Schema) || _codecsByType.ContainsKey(codec.ValueType))
        {
            throw new ArgumentException("Codec schema tuples and value types must be unique.", nameof(codec));
        }

        _codecsBySchema.Add(codec.Schema, codec);
        _codecsByType.Add(codec.ValueType, codec);
    }

    private sealed class BinarySchemaCodecAdapter<T> : AssetStoreRegistration.IAssetCodec<T>
    {
        private readonly IBinarySchemaCodec<T> _codec;

        internal BinarySchemaCodecAdapter(IBinarySchemaCodec<T> codec)
        {
            _codec = codec;
            Schema = new ContentSchemaReference(
                codec.SchemaId,
                codec.SchemaMajor,
                codec.SchemaMinor,
                ContentSchemaScope.Content);
        }

        public ContentSchemaReference Schema { get; }

        public Type ValueType => typeof(T);

        public BinaryRecordStatus Read(
            scoped ReadOnlySpan<byte> source,
            AssetCodecContext context,
            [MaybeNull]
            out T value,
            out int bytesConsumed,
            out BinarySchemaDiagnostic? diagnostic)
        {
            ArgumentNullException.ThrowIfNull(context);
            diagnostic = null;
            return _codec.Read(source, out value, out bytesConsumed);
        }

        public BinaryRecordStatus Write(
            in T value,
            AssetCodecContext context,
            scoped Span<byte> destination,
            out int bytesWritten,
            out BinarySchemaDiagnostic? diagnostic)
        {
            ArgumentNullException.ThrowIfNull(context);
            diagnostic = null;
            return _codec.Write(value, destination, out bytesWritten);
        }
    }

    private sealed class ContextualSchemaCodecAdapter<T> : AssetStoreRegistration.IAssetCodec<T>
    {
        private readonly IContextualBinarySchemaCodec<T> _codec;

        internal ContextualSchemaCodecAdapter(IContextualBinarySchemaCodec<T> codec)
        {
            _codec = codec;
            Schema = new ContentSchemaReference(
                codec.SchemaId,
                codec.SchemaMajor,
                codec.SchemaMinor,
                ContentSchemaScope.Content);
        }

        public ContentSchemaReference Schema { get; }

        public Type ValueType => typeof(T);

        public BinaryRecordStatus Read(
            scoped ReadOnlySpan<byte> source,
            AssetCodecContext context,
            [MaybeNull]
            out T value,
            out int bytesConsumed,
            out BinarySchemaDiagnostic? diagnostic)
        {
            ArgumentNullException.ThrowIfNull(context);
            return _codec.Read(source, context, out value, out bytesConsumed, out diagnostic);
        }

        public BinaryRecordStatus Write(
            in T value,
            AssetCodecContext context,
            scoped Span<byte> destination,
            out int bytesWritten,
            out BinarySchemaDiagnostic? diagnostic)
        {
            ArgumentNullException.ThrowIfNull(context);
            return _codec.Write(value, context, destination, out bytesWritten, out diagnostic);
        }
    }
}
