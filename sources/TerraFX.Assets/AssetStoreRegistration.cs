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
    private readonly AssetStoreRegistrationFamily _family;

    internal AssetStoreRegistration(CompiledContentSchemaRegistry schemas)
        : this(schemas, [], AssetStoreRegistrationFamily.General)
    {
    }

    private AssetStoreRegistration(
        CompiledContentSchemaRegistry schemas,
        ImmutableArray<IAssetCodec> codecs,
        AssetStoreRegistrationFamily family)
    {
        ArgumentNullException.ThrowIfNull(schemas);

        if (codecs.IsDefault)
        {
            throw new ArgumentException("The codec set must be initialized.", nameof(codecs));
        }

        foreach (var compiledNativeSchema in NativeAssetSchemaCatalogue.Schemas)
        {
            if (!schemas.TryGetSchema(compiledNativeSchema.Schema, out var registeredSchema)
                || !ReferenceEquals(registeredSchema, compiledNativeSchema))
            {
                throw new ArgumentException(
                    "The registration must include the canonical compiled native-asset schemas.",
                    nameof(schemas));
            }
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
        _family = family;
        Schemas = schemas;

        if (family is AssetStoreRegistrationFamily.StaticMeshValidationV1)
        {
            ValidateStaticMeshValidationV1(schemas, codecs);
        }
    }

    /// <summary>Gets the application's immutable compiled schema registry.</summary>
    public CompiledContentSchemaRegistry Schemas { get; }

    internal AssetStoreRegistrationFamily Family => _family;

    internal static AssetStoreRegistration Create(
        CompiledContentSchemaRegistry schemas,
        ImmutableArray<IAssetCodec> codecs)
        => new AssetStoreRegistration(schemas, codecs, AssetStoreRegistrationFamily.General);

    internal static AssetStoreRegistration CreateBoundedStaticMeshValidationV1(
        CompiledContentSchemaRegistry schemas,
        ImmutableArray<IAssetCodec> codecs)
        => new AssetStoreRegistration(schemas, codecs, AssetStoreRegistrationFamily.StaticMeshValidationV1);

    private static void ValidateStaticMeshValidationV1(
        CompiledContentSchemaRegistry schemas,
        ImmutableArray<IAssetCodec> codecs)
    {
        var meshSchemaId = new ContentSchemaIdentityAssignment("native/mesh").SchemaId;
        var materialSchemaId = new ContentSchemaIdentityAssignment("native/material").SchemaId;
        var hasMesh = false;
        var hasMaterial = false;

        if ((schemas.Schemas.Length != 81) || !schemas.Profiles.IsEmpty || (codecs.Length != 2))
        {
            throw new ArgumentException("The bounded static-Mesh registration must contain its exact schema and codec set.", nameof(codecs));
        }

        foreach (var codec in codecs)
        {
            var schema = codec.Schema;

            if ((schema.Major != 1)
                || (schema.Minor != 0)
                || (schema.Scope != ContentSchemaScope.Content)
                || (codec.ProfileOrigin is not null)
                || (codec.RequiredPackageCodec is not null))
            {
                throw new ArgumentException("The bounded static-Mesh codecs must use exact unprofiled native schema version 1.0.", nameof(codecs));
            }

            if (schema.SchemaId == meshSchemaId)
            {
                hasMesh = true;
            }
            else if (schema.SchemaId == materialSchemaId)
            {
                hasMaterial = true;
            }
            else
            {
                throw new ArgumentException("The bounded static-Mesh registration cannot contain another asset codec.", nameof(codecs));
            }
        }

        if (!hasMesh || !hasMaterial)
        {
            throw new ArgumentException("The bounded static-Mesh registration requires one Mesh and one Material codec.", nameof(codecs));
        }
    }

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

    internal bool TryGetProfiledCodecReceipt<T>(object profileOrigin, out AssetCodecRegistrationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(profileOrigin);

        if (_codecsByType.TryGetValue(typeof(T), out var registeredCodec)
            && (registeredCodec is IAssetCodec<T> typedCodec)
            && ReferenceEquals(typedCodec.ProfileOrigin, profileOrigin))
        {
            receipt = new AssetCodecRegistrationReceipt(typedCodec, profileOrigin);
            return true;
        }

        receipt = default;
        return false;
    }

    internal interface IAssetCodec
    {
        object? ProfileOrigin { get; }

        AssetPackagePayloadCodec? RequiredPackageCodec { get; }

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
