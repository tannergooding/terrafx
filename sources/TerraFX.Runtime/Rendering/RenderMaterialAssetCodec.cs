// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal sealed class RenderMaterialAssetCodec : IContextualBinarySchemaCodec<RenderMaterialAsset>
{
    private const ulong AlphaMaskCapability = 0x004;
    private const ulong NormalMapCapability = 0x040;
    private const ulong PbrBaseCapabilities = 0x191;
    private const ulong PremultipliedBlendCapability = 0x008;
    private const ulong ReversedZDepthCapability = 0x010;
    private const ulong SpriteBaseCapabilities = 0x001;

    private static readonly Guid s_assetReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;
    private static readonly Guid s_materialPbrParametersSchemaId = new ContentSchemaIdentityAssignment("native/material-pbr-parameters").SchemaId;
    private static readonly Guid s_materialSpriteParametersSchemaId = new ContentSchemaIdentityAssignment("native/material-sprite-parameters").SchemaId;
    private static readonly Guid s_materialTextureBindingSchemaId = new ContentSchemaIdentityAssignment("native/texture-binding").SchemaId;
    private static readonly Guid s_pbrLayoutId = new ContentSchemaIdentityAssignment("material/layout/pbr/v1").SchemaId;
    private static readonly Guid s_pbrPassId = new ContentSchemaIdentityAssignment("material/pass/pbr/v1").SchemaId;
    private static readonly Guid s_pbrProfileId = new ContentSchemaIdentityAssignment("material/profile/pbr-metallic-roughness/v1").SchemaId;
    private static readonly Guid s_pbrShaderId = new ContentSchemaIdentityAssignment("material/shader/pbr-metallic-roughness/v1").SchemaId;
    private static readonly Guid s_samplerSchemaId = new ContentSchemaIdentityAssignment("native/sampler").SchemaId;
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("native/material").SchemaId;
    private static readonly Guid s_spriteLayoutId = new ContentSchemaIdentityAssignment("material/layout/sprite/v1").SchemaId;
    private static readonly Guid s_spritePassId = new ContentSchemaIdentityAssignment("material/pass/sprite/v1").SchemaId;
    private static readonly Guid s_spriteProfileId = new ContentSchemaIdentityAssignment("material/profile/sprite-premul/v1").SchemaId;
    private static readonly Guid s_spriteShaderId = new ContentSchemaIdentityAssignment("material/shader/sprite-unlit/v1").SchemaId;
    private static readonly Guid s_textureSchemaId = new ContentSchemaIdentityAssignment("native/texture").SchemaId;

    public Guid SchemaId => s_schemaId;

    public int SchemaMajor => 1;

    public int SchemaMinor => 0;

    private static BinarySchemaDiagnostic? CreateDiagnostic(EbinReadDiagnostic? readDiagnostic)
    {
        if (readDiagnostic is not EbinReadDiagnostic diagnostic)
        {
            return null;
        }

        var schemaId = diagnostic.SchemaId == Guid.Empty ? s_schemaId : diagnostic.SchemaId;
        return new BinarySchemaDiagnostic(
            schemaId,
            diagnostic.Tag,
            [],
            diagnostic.Category,
            diagnostic.Claim,
            diagnostic.Limit);
    }

    private static BinarySchemaDiagnostic CreateDiagnostic(string category)
        => new(s_schemaId, 0, [], category, null, null);

    private static EbinFieldReadView GetRequiredField(EbinRecordReadView record, uint tag, ushort kind, ulong count)
    {
        if (!record.TryGetField(tag, out var field)
            || !field.IsRequired
            || (field.Kind != kind)
            || (field.Count != count))
        {
            throw new InvalidDataException($"The Material record field {tag} does not match its required wire shape.");
        }

        return field;
    }

    private static AssetId ReadAssetIdReference(EbinRecordReadView record)
    {
        var header = record.Header;

        if ((header.SchemaId != s_assetReferenceSchemaId) || (header.Major != 1) || (header.Minor != 0)
            || (header.FieldCount != 3))
        {
            throw new InvalidDataException("A Material texture reference has an invalid asset-reference schema.");
        }

        var assetIdValue = ReadUInt32(record, tag: 1);
        var schemaId = ReadGuid(record, tag: 2);
        var schemaMajor = ReadUInt32(record, tag: 3);

        if ((assetIdValue is 0 or > int.MaxValue)
            || (schemaId != s_textureSchemaId)
            || (schemaMajor != 1))
        {
            throw new InvalidDataException("A Material texture reference must identify a native/texture 1 asset.");
        }

        return new AssetId(checked((int)assetIdValue));
    }

    private static bool ReadBoolean(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 9, count: 1);

        if ((field.Payload.Length != sizeof(byte)) || (field.Payload[0] > 1))
        {
            throw new InvalidDataException($"The Material record field {tag} is not a canonical Boolean.");
        }

        return field.Payload[0] != 0;
    }

    private static float ReadFloat(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 10, count: 1);

        if (field.Payload.Length != sizeof(float))
        {
            throw new InvalidDataException($"The Material record field {tag} does not contain one Float32.");
        }

        var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(field.Payload));
        return float.IsFinite(value)
            ? value
            : throw new InvalidDataException($"The Material record field {tag} is not finite.");
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 11, count: 1);

        if (field.Payload.Length != 16)
        {
            throw new InvalidDataException($"The Material record field {tag} does not contain one GUID.");
        }

        return new Guid(field.Payload, bigEndian: true);
    }

    private static RenderMaterialAsset ReadMaterial(EbinRecordReadView record)
    {
        var header = record.Header;

        if ((header.SchemaId != s_schemaId) || (header.Major != 1) || (header.Minor != 0))
        {
            throw new NotSupportedException("The selected native/material schema version is unsupported.");
        }

        if (header.FieldCount != 13)
        {
            throw new InvalidDataException("A Material record must contain exactly thirteen fields.");
        }

        var profileId = ReadGuid(record, tag: 1);
        var isSprite = profileId == s_spriteProfileId;

        if (!isSprite && (profileId != s_pbrProfileId))
        {
            throw new NotSupportedException("The selected Material profile is unsupported.");
        }

        var shaderId = ReadGuid(record, tag: 2);
        var passId = ReadGuid(record, tag: 3);
        var layoutId = ReadGuid(record, tag: 4);
        var expectedShaderId = isSprite ? s_spriteShaderId : s_pbrShaderId;
        var expectedPassId = isSprite ? s_spritePassId : s_pbrPassId;
        var expectedLayoutId = isSprite ? s_spriteLayoutId : s_pbrLayoutId;

        if ((shaderId != expectedShaderId) || (passId != expectedPassId) || (layoutId != expectedLayoutId))
        {
            throw new InvalidDataException("A Material profile, shader, pass, and layout must be one exact declared tuple.");
        }

        _ = GetRequiredField(record, tag: 5, kind: 22, count: 1);
        var parameterRecord = record.GetRecord(5);
        var parameters = isSprite
            ? ReadSpriteParameters(parameterRecord)
            : ReadPbrParameters(parameterRecord);
        var alphaModeValue = ReadUInt8(record, tag: 6);
        var alphaCutoff = ReadFloat(record, tag: 7);
        var doubleSided = ReadBoolean(record, tag: 8);
        var depthTest = ReadBoolean(record, tag: 9);
        var depthWrite = ReadBoolean(record, tag: 10);
        var cullModeValue = ReadUInt8(record, tag: 11);
        var outputAlphaValue = ReadUInt8(record, tag: 12);
        var requiredCapabilities = ReadUInt64(record, tag: 13);

        if ((alphaModeValue > (byte)RenderMaterialAsset.MaterialAlphaMode.Blend)
            || (cullModeValue > (byte)RenderMaterialAsset.MaterialCullMode.Back)
            || (outputAlphaValue > (byte)RenderMaterialAsset.MaterialOutputAlpha.Premultiplied)
            || (alphaCutoff < 0.0f)
            || (alphaCutoff > 1.0f))
        {
            throw new InvalidDataException("A Material state value is outside its admitted range.");
        }

        var alphaMode = (RenderMaterialAsset.MaterialAlphaMode)alphaModeValue;
        var cullMode = (RenderMaterialAsset.MaterialCullMode)cullModeValue;
        var outputAlpha = (RenderMaterialAsset.MaterialOutputAlpha)outputAlphaValue;
        var isBlend = alphaMode is RenderMaterialAsset.MaterialAlphaMode.Blend;

        if ((depthWrite && !depthTest)
            || (isBlend && depthWrite)
            || (doubleSided && (cullMode is not RenderMaterialAsset.MaterialCullMode.None))
            || (outputAlpha != (isBlend ? RenderMaterialAsset.MaterialOutputAlpha.Premultiplied : RenderMaterialAsset.MaterialOutputAlpha.Straight)))
        {
            throw new InvalidDataException("A Material depth, cull, and output-alpha combination is invalid.");
        }

        var expectedCapabilities = isSprite ? SpriteBaseCapabilities : PbrBaseCapabilities;

        if (alphaMode is RenderMaterialAsset.MaterialAlphaMode.Mask)
        {
            expectedCapabilities |= AlphaMaskCapability;
        }
        else if (isBlend)
        {
            expectedCapabilities |= PremultipliedBlendCapability;
        }

        if (!isSprite && parameters.NormalTexture.HasValue)
        {
            expectedCapabilities |= NormalMapCapability;
        }

        if (depthTest || depthWrite)
        {
            expectedCapabilities |= ReversedZDepthCapability;
        }

        if (requiredCapabilities != expectedCapabilities)
        {
            throw new InvalidDataException("The Material RequiredCapabilities value is not the exact derived profile capability mask.");
        }

        return new RenderMaterialAsset(
            profileId,
            isSprite,
            shaderId,
            passId,
            layoutId,
            parameters,
            alphaMode,
            alphaCutoff,
            doubleSided,
            depthTest,
            depthWrite,
            cullMode,
            outputAlpha,
            requiredCapabilities,
            [.. record.EncodedBytes]);
    }

    private static RenderMaterialAsset.TextureBinding? ReadOptionalTextureBinding(
        EbinRecordReadView record,
        uint tag)
    {
        if (!record.TryGetField(tag, out var field))
        {
            return null;
        }

        if ((field.Kind != 22) || (field.Count != 1))
        {
            throw new InvalidDataException($"The Material texture field {tag} does not contain one binding record.");
        }

        if (field.IsRequired)
        {
            throw new InvalidDataException($"The Material texture field {tag} must remain optional.");
        }

        return ReadTextureBinding(record.GetRecord(tag));
    }

    private static RenderMaterialAsset.ParameterValues ReadPbrParameters(EbinRecordReadView record)
    {
        var header = record.Header;

        if ((header.SchemaId != s_materialPbrParametersSchemaId) || (header.Major != 1) || (header.Minor != 0)
            || (header.FieldCount < 5) || (header.FieldCount > 9))
        {
            throw new InvalidDataException("The Material parameters do not match the PBR parameter schema.");
        }

        var color = ReadVector4(record, tag: 1);
        ValidateUnitColor(color, fieldTag: 1);
        var metallic = ReadFloat(record, tag: 2);
        var roughness = ReadFloat(record, tag: 3);
        var normalScale = ReadFloat(record, tag: 4);
        var emissive = ReadVector3(record, tag: 5);

        if ((metallic < 0.0f) || (metallic > 1.0f)
            || (roughness < BitConverter.Int32BitsToSingle(0x3D3851EC)) || (roughness > 1.0f)
            || (normalScale < 0.0f) || (normalScale > 2.0f)
            || (emissive.X < 0.0f) || (emissive.Y < 0.0f) || (emissive.Z < 0.0f))
        {
            throw new InvalidDataException("The PBR Material parameters are outside their admitted ranges.");
        }

        var baseTexture = ReadOptionalTextureBinding(record, tag: 6);
        var packedTexture = ReadOptionalTextureBinding(record, tag: 7);
        var normalTexture = ReadOptionalTextureBinding(record, tag: 8);
        var emissiveTexture = ReadOptionalTextureBinding(record, tag: 9);
        return new RenderMaterialAsset.ParameterValues(
            color,
            metallic,
            roughness,
            normalScale,
            emissive,
            SpriteTexture: null,
            baseTexture,
            packedTexture,
            normalTexture,
            emissiveTexture);
    }

    private static RenderMaterialAsset.Sampler ReadSampler(EbinRecordReadView record)
    {
        var header = record.Header;

        if ((header.SchemaId != s_samplerSchemaId) || (header.Major != 1) || (header.Minor != 0)
            || (header.FieldCount != 5))
        {
            throw new InvalidDataException("A Material sampler has an invalid schema identity or field count.");
        }

        var minification = ReadUInt8(record, tag: 1);
        var magnification = ReadUInt8(record, tag: 2);
        var mip = ReadUInt8(record, tag: 3);
        var addressU = ReadUInt8(record, tag: 4);
        var addressV = ReadUInt8(record, tag: 5);

        if ((minification > 1) || (magnification > 1) || (mip > 2) || (addressU > 1) || (addressV > 1))
        {
            throw new InvalidDataException("A Material sampler value is outside its admitted enum.");
        }

        return new RenderMaterialAsset.Sampler(minification, magnification, mip, addressU, addressV);
    }

    private static RenderMaterialAsset.ParameterValues ReadSpriteParameters(EbinRecordReadView record)
    {
        var header = record.Header;

        if ((header.SchemaId != s_materialSpriteParametersSchemaId) || (header.Major != 1) || (header.Minor != 0)
            || (header.FieldCount is < 1 or > 2))
        {
            throw new InvalidDataException("The Material parameters do not match the Sprite parameter schema.");
        }

        var color = ReadVector4(record, tag: 1);
        ValidateUnitColor(color, fieldTag: 1);
        var texture = ReadOptionalTextureBinding(record, tag: 2);

        if (texture is { UvSet: not 0 })
        {
            throw new InvalidDataException("The Sprite material layout supports only UVSet0.");
        }

        return new RenderMaterialAsset.ParameterValues(
            color,
            Metallic: 0.0f,
            Roughness: 1.0f,
            NormalScale: 1.0f,
            Emissive: Vector3.Zero,
            texture,
            BaseTexture: null,
            PackedMetalRoughOcclusion: null,
            NormalTexture: null,
            EmissiveTexture: null);
    }

    private static RenderMaterialAsset.TextureBinding ReadTextureBinding(EbinRecordReadView record)
    {
        var header = record.Header;

        if ((header.SchemaId != s_materialTextureBindingSchemaId) || (header.Major != 1) || (header.Minor != 0)
            || (header.FieldCount != 3))
        {
            throw new InvalidDataException("A Material texture binding has an invalid schema identity or field count.");
        }

        _ = GetRequiredField(record, tag: 1, kind: 22, count: 1);
        _ = GetRequiredField(record, tag: 2, kind: 22, count: 1);
        var textureAssetId = ReadAssetIdReference(record.GetRecord(1));
        var sampler = ReadSampler(record.GetRecord(2));
        var uvSet = ReadUInt8(record, tag: 3);

        if (uvSet > 1)
        {
            throw new InvalidDataException("A Material texture binding UVSet must be 0 or 1.");
        }

        return new RenderMaterialAsset.TextureBinding(textureAssetId, sampler, uvSet);
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 3, count: 1);

        if (field.Payload.Length != sizeof(uint))
        {
            throw new InvalidDataException($"The Material record field {tag} does not contain one UInt32.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static ulong ReadUInt64(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 4, count: 1);

        if (field.Payload.Length != sizeof(ulong))
        {
            throw new InvalidDataException($"The Material record field {tag} does not contain one UInt64.");
        }

        return BinaryPrimitives.ReadUInt64LittleEndian(field.Payload);
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 1, count: 1);

        if (field.Payload.Length != sizeof(byte))
        {
            throw new InvalidDataException($"The Material record field {tag} does not contain one UInt8.");
        }

        return field.Payload[0];
    }

    private static Vector3 ReadVector3(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 15, count: 1);

        if (field.Payload.Length != 12)
        {
            throw new InvalidDataException($"The Material record field {tag} does not contain one Vector3.");
        }

        var value = new Vector3(
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(field.Payload)),
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(field.Payload[4..])),
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(field.Payload[8..])));
        return value.IsFinite().All()
            ? value
            : throw new InvalidDataException($"The Material record field {tag} is not finite.");
    }

    private static Vector4 ReadVector4(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 16, count: 1);

        if (field.Payload.Length != 16)
        {
            throw new InvalidDataException($"The Material record field {tag} does not contain one Vector4.");
        }

        var value = new Vector4(
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(field.Payload)),
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(field.Payload[4..])),
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(field.Payload[8..])),
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(field.Payload[12..])));
        return value.IsFinite().All()
            ? value
            : throw new InvalidDataException($"The Material record field {tag} is not finite.");
    }

    private static void ValidateUnitColor(Vector4 color, uint fieldTag)
    {
        if ((color.X < 0.0f) || (color.X > 1.0f)
            || (color.Y < 0.0f) || (color.Y > 1.0f)
            || (color.Z < 0.0f) || (color.Z > 1.0f)
            || (color.W < 0.0f) || (color.W > 1.0f))
        {
            throw new InvalidDataException($"The Material color field {fieldTag} is outside [0,1].");
        }
    }

    public BinaryRecordStatus Read(
        scoped ReadOnlySpan<byte> source,
        AssetCodecContext context,
        [MaybeNull] out RenderMaterialAsset value,
        out int bytesConsumed,
        out BinarySchemaDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        value = null;
        bytesConsumed = 0;
        diagnostic = null;
        var action = new ReadAction();
        EbinReadStatus readStatus;
        EbinReadDiagnostic? readDiagnostic;

        try
        {
            readStatus = EbinStructuralReader.Read(
                source,
                context.ReadLimits,
                EbinReadPolicy.Canonical,
                ref action,
                out bytesConsumed,
                out readDiagnostic);
        }
        catch (NotSupportedException)
        {
            diagnostic = CreateDiagnostic("unsupported-material-schema");
            return BinaryRecordStatus.UnsupportedSchema;
        }
        catch (InvalidDataException)
        {
            diagnostic = CreateDiagnostic("invalid-material-record");
            return BinaryRecordStatus.InvalidRecord;
        }

        if (readStatus is not EbinReadStatus.Complete)
        {
            diagnostic = CreateDiagnostic(readDiagnostic);
            bytesConsumed = 0;
            return readStatus switch {
                EbinReadStatus.NeedMoreData => BinaryRecordStatus.NeedMoreData,
                EbinReadStatus.UnsupportedSchema => BinaryRecordStatus.UnsupportedSchema,
                _ => BinaryRecordStatus.InvalidRecord,
            };
        }

        if ((bytesConsumed != source.Length) || (action.Value is null))
        {
            diagnostic = CreateDiagnostic(
                bytesConsumed != source.Length ? "trailing-material-bytes" : "missing-material-value");
            bytesConsumed = 0;
            return BinaryRecordStatus.InvalidRecord;
        }

        value = action.Value;
        return BinaryRecordStatus.Complete;
    }

    public BinaryRecordStatus Write(
        in RenderMaterialAsset value,
        AssetCodecContext context,
        scoped Span<byte> destination,
        out int bytesWritten,
        out BinarySchemaDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        bytesWritten = 0;
        diagnostic = null;

        if (value is null)
        {
            diagnostic = CreateDiagnostic("missing-material-value");
            return BinaryRecordStatus.InvalidRecord;
        }

        var encodedRecord = value.EncodedRecord.AsSpan();

        if (destination.Length < encodedRecord.Length)
        {
            return BinaryRecordStatus.DestinationTooSmall;
        }

        encodedRecord.CopyTo(destination);
        bytesWritten = encodedRecord.Length;
        return BinaryRecordStatus.Complete;
    }

    private struct ReadAction : IAction<EbinRecordReadView>
    {
        internal RenderMaterialAsset? Value { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            Value = ReadMaterial(record);
        }
    }
}
