// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Assets;

/// <summary>Contains one immutable compiled schema and typed-codec registration.</summary>
public sealed class AssetStoreRegistration
{
    private readonly ImmutableDictionary<ContentSchemaReference, IAssetCodec> _codecsBySchema;
    private readonly ImmutableDictionary<Type, IAssetCodec> _codecsByType;

    internal AssetStoreRegistration(CompiledContentSchemaRegistry schemas)
        : this(schemas, [])
    {
    }

    private AssetStoreRegistration(
        CompiledContentSchemaRegistry schemas,
        ImmutableArray<IAssetCodec> codecs)
    {
        ArgumentNullException.ThrowIfNull(schemas);

        if (codecs.IsDefault)
        {
            throw new ArgumentException("The codec set must be initialized.", nameof(codecs));
        }

        var codecsBySchema = ImmutableDictionary.CreateBuilder<ContentSchemaReference, IAssetCodec>();
        var codecsByType = ImmutableDictionary.CreateBuilder<Type, IAssetCodec>();

        foreach (var codec in codecs)
        {
            if (codec is null)
            {
                throw new ArgumentException("The codec set cannot contain null entries.", nameof(codecs));
            }

            if (!schemas.TryGetSchema(codec.Schema, out var schema)
                || (schema.Role != ContentSchemaRole.NativeAsset))
            {
                throw new ArgumentException("Each codec must match an exact compiled native-asset schema tuple.", nameof(codecs));
            }

            if (codecsBySchema.ContainsKey(codec.Schema) || codecsByType.ContainsKey(codec.ValueType))
            {
                throw new ArgumentException("Codec schema tuples and value types must be unique.", nameof(codecs));
            }

            codecsBySchema.Add(codec.Schema, codec);
            codecsByType.Add(codec.ValueType, codec);
        }

        _codecsBySchema = codecsBySchema.ToImmutable();
        _codecsByType = codecsByType.ToImmutable();
        Schemas = schemas;
    }

    /// <summary>Gets the application's immutable compiled schema registry.</summary>
    public CompiledContentSchemaRegistry Schemas { get; }

    internal static AssetStoreRegistration Create(
        CompiledContentSchemaRegistry schemas,
        ImmutableArray<IAssetCodec> codecs)
        => new AssetStoreRegistration(schemas, codecs);

    internal bool TryGetCodec(ContentSchemaReference schema, [NotNullWhen(true)] out IAssetCodec? codec)
        => _codecsBySchema.TryGetValue(schema, out codec);

    internal bool TryGetCodec<T>([NotNullWhen(true)] out IAssetCodec<T>? codec)
    {
        if (_codecsByType.TryGetValue(typeof(T), out var registeredCodec)
            && (registeredCodec is IAssetCodec<T> typedCodec))
        {
            codec = typedCodec;
            return true;
        }

        codec = null;
        return false;
    }

    internal interface IAssetCodec
    {
        ContentSchemaReference Schema { get; }

        Type ValueType { get; }
    }

    internal interface IAssetCodec<T> : IAssetCodec
    {
        BinaryRecordStatus Read(
            scoped ReadOnlySpan<byte> source,
            AssetCodecContext context,
            [MaybeNull] out T value,
            out int bytesConsumed,
            out BinarySchemaDiagnostic? diagnostic);

        BinaryRecordStatus Write(
            in T value,
            AssetCodecContext context,
            scoped Span<byte> destination,
            out int bytesWritten,
            out BinarySchemaDiagnostic? diagnostic);
    }
}
