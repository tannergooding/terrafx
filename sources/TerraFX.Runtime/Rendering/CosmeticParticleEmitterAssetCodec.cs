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

internal sealed class CosmeticParticleEmitterAssetCodec : IContextualBinarySchemaCodec<CosmeticParticleEmitterAsset>
{
    private const int MaximumRecordBytes = 268_435_456;
    private static readonly Guid s_atlasSchemaId = new ContentSchemaIdentityAssignment("native/atlas").SchemaId;
    private static readonly Guid s_colorKeySchemaId = new ContentSchemaIdentityAssignment("native/particle-color-key").SchemaId;
    private static readonly Guid s_materialSchemaId = new ContentSchemaIdentityAssignment("native/material").SchemaId;
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("native/cosmetic-particle-emitter").SchemaId;
    private static readonly Guid s_sizeKeySchemaId = new ContentSchemaIdentityAssignment("native/particle-size-key").SchemaId;

    public Guid SchemaId => s_schemaId;

    public int SchemaMajor => 1;

    public int SchemaMinor => 0;

    private static BinarySchemaDiagnostic CreateDiagnostic(string category)
        => new(s_schemaId, 0, [], category, null, null);

    private static EbinFieldReadView GetRequiredField(
        EbinRecordReadView record,
        uint tag,
        ushort kind,
        ulong count)
    {
        if (!record.TryGetField(tag, out var field)
            || !field.IsRequired
            || (field.Kind != kind)
            || (field.Count != count))
        {
            throw new InvalidDataException($"The cosmetic emitter field {tag} does not match its required wire shape.");
        }

        return field;
    }

    private static ImmutableArray<CosmeticParticleEmitterAsset.ColorKey> ReadColorKeys(EbinRecordReadView record)
    {
        _ = record.TryGetField(6, out var keyArray);
        var keys = ImmutableArray.CreateBuilder<CosmeticParticleEmitterAsset.ColorKey>(checked((int)keyArray.Count));
        var previousAge = -1.0f;

        for (var index = 0; index < checked((int)keyArray.Count); index++)
        {
            var key = record.GetRecordArrayElement(6, index);
            ValidateHeader(key, s_colorKeySchemaId, fieldCount: 2);
            var age = ReadFloat(key, 1);
            var color = ReadVector4(key, 2);

            if ((index == 0) && (BitConverter.SingleToUInt32Bits(age) != 0))
            {
                throw new InvalidDataException("Cosmetic particle color curves must start at positive zero.");
            }

            if ((age <= previousAge)
                || (color.X < 0.0f)
                || (color.Y < 0.0f)
                || (color.Z < 0.0f)
                || (color.W < 0.0f)
                || (color.W > 1.0f))
            {
                throw new InvalidDataException("Cosmetic particle color keys must ascend and contain finite nonnegative HDR colors with alpha in [0,1].");
            }

            keys.Add(new CosmeticParticleEmitterAsset.ColorKey(age, color));
            previousAge = age;
        }

        if (previousAge != 1.0f)
        {
            throw new InvalidDataException("Cosmetic particle color curves must end at one.");
        }

        return keys.MoveToImmutable();
    }

    private static CosmeticParticleEmitterAsset ReadCosmeticParticleEmitter(EbinRecordReadView record, AssetCodecContext context)
    {
        RenderM05SchemaReader.Validate(
            record,
            context.Schemas,
            new ContentSchemaReference(s_schemaId, 1, 0, ContentSchemaScope.Content));
        var spaceValue = ReadUInt8(record, 1);

        if (spaceValue > (byte)CosmeticParticleSpace.CameraFacing3D)
        {
            throw new InvalidDataException("The cosmetic emitter Space value is unsupported.");
        }

        var spawnPerTick = ReadUInt32(record, 2);

        if (spawnPerTick > 131_072)
        {
            throw new InvalidDataException("Cosmetic emitter SpawnPerTick exceeds its finite pool limit.");
        }

        var lifetimeTicks = ReadInt64(record, 3);

        if (lifetimeTicks <= 0)
        {
            throw new InvalidDataException("Cosmetic emitter LifetimeTicks must be positive.");
        }

        var minimum = ReadVector3(record, 4);
        var maximum = ReadVector3(record, 5);

        if ((minimum.X > maximum.X)
            || (minimum.Y > maximum.Y)
            || (minimum.Z > maximum.Z))
        {
            throw new InvalidDataException("Cosmetic emitter velocity minima must not exceed their maxima.");
        }

        var space = (CosmeticParticleSpace)spaceValue;

        if ((space is CosmeticParticleSpace.PlaneXY) && ((minimum.Z != 0.0f) || (maximum.Z != 0.0f)))
        {
            throw new InvalidDataException("PlaneXY emitters require zero minimum and maximum Z velocity.");
        }

        var colorKeys = ReadColorKeys(record);
        var sizeKeys = ReadSizeKeys(record);
        var atlas = ReadReference(record.GetRecord(8), s_atlasSchemaId, allowedMajors: [1], context.Schemas);
        var frameId = ReadGuid(record, 9);

        if (frameId == Guid.Empty)
        {
            throw new InvalidDataException("A cosmetic emitter FrameId must be nonempty.");
        }

        CosmeticParticleEmitterAsset.MaterialReference? material = null;

        if (record.TryGetField(10, out _))
        {
            var reference = ReadReference(record.GetRecord(10), s_materialSchemaId, allowedMajors: [1, 2], context.Schemas);
            material = new CosmeticParticleEmitterAsset.MaterialReference(reference.AssetId, reference.SchemaMajor);
        }

        var seed = ReadUInt64(record, 11);
        var stream = ReadUInt64(record, 12);

        if (stream > long.MaxValue)
        {
            throw new InvalidDataException("Cosmetic emitter Stream must fit its admitted 63-bit domain.");
        }

        return new CosmeticParticleEmitterAsset(
            context.RootAssetId,
            space,
            spawnPerTick,
            lifetimeTicks,
            minimum,
            maximum,
            colorKeys,
            sizeKeys,
            atlas.AssetId,
            frameId,
            material,
            seed,
            stream,
            ReadInt32(record, 13),
            ReadInt32(record, 14),
            ReadUInt64(record, 15),
            [.. record.EncodedBytes]);
    }

    private static float ReadFloat(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, 10, 1);

        if (field.Payload.Length != sizeof(float))
        {
            throw new InvalidDataException($"The cosmetic emitter field {tag} must contain one Float32.");
        }

        var value = EbinPrimitives.Single(field.Payload);
        return float.IsFinite(value)
            ? value
            : throw new InvalidDataException($"The cosmetic emitter field {tag} must be finite.");
    }

    private static Guid ReadGuid(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, 11, 1);

        if (field.Payload.Length != 16)
        {
            throw new InvalidDataException($"The cosmetic emitter field {tag} must contain one GUID.");
        }

        return new Guid(field.Payload, bigEndian: true);
    }

    private static int ReadInt32(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, 7, 1);

        if (field.Payload.Length != sizeof(int))
        {
            throw new InvalidDataException($"The cosmetic emitter field {tag} must contain one Int32.");
        }

        return BinaryPrimitives.ReadInt32LittleEndian(field.Payload);
    }

    private static long ReadInt64(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, 8, 1);

        if (field.Payload.Length != sizeof(long))
        {
            throw new InvalidDataException($"The cosmetic emitter field {tag} must contain one Int64.");
        }

        return BinaryPrimitives.ReadInt64LittleEndian(field.Payload);
    }

    private static (AssetId AssetId, int SchemaMajor) ReadReference(
        EbinRecordReadView record,
        Guid expectedSchema,
        ReadOnlySpan<int> allowedMajors,
        CompiledContentSchemaRegistry schemas)
    {
        ValidateHeader(
            record,
            new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId,
            fieldCount: 3);
        var idField = GetRequiredField(record, 1, 3, 1);
        var schemaField = GetRequiredField(record, 2, 11, 1);
        var majorField = GetRequiredField(record, 3, 3, 1);

        if ((idField.Payload.Length != sizeof(uint))
            || (schemaField.Payload.Length != 16)
            || (majorField.Payload.Length != sizeof(uint)))
        {
            throw new InvalidDataException("A cosmetic emitter asset reference has an invalid scalar extent.");
        }

        var assetIdValue = BinaryPrimitives.ReadUInt32LittleEndian(idField.Payload);
        var schemaId = new Guid(schemaField.Payload, bigEndian: true);
        var majorValue = BinaryPrimitives.ReadUInt32LittleEndian(majorField.Payload);

        if ((assetIdValue is 0 or > int.MaxValue) || (schemaId != expectedSchema) || (majorValue > int.MaxValue))
        {
            throw new InvalidDataException("A cosmetic emitter asset reference does not select an admitted native asset.");
        }

        var major = checked((int)majorValue);
        var isAllowedMajor = false;

        foreach (var allowedMajor in allowedMajors)
        {
            if (major == allowedMajor)
            {
                isAllowedMajor = true;
                break;
            }
        }

        var schema = new ContentSchemaReference(schemaId, major, 0, ContentSchemaScope.Content);

        if (!isAllowedMajor
            || !schemas.TryGetSchema(schema, out var descriptor)
            || (descriptor.Role != ContentSchemaRole.NativeAsset))
        {
            throw new NotSupportedException("A cosmetic emitter reference selects an absent or unsupported exact native schema.");
        }

        return (new AssetId(checked((int)assetIdValue)), major);
    }

    private static ImmutableArray<CosmeticParticleEmitterAsset.SizeKey> ReadSizeKeys(EbinRecordReadView record)
    {
        _ = record.TryGetField(7, out var keyArray);
        var keys = ImmutableArray.CreateBuilder<CosmeticParticleEmitterAsset.SizeKey>(checked((int)keyArray.Count));
        var previousAge = -1.0f;

        for (var index = 0; index < checked((int)keyArray.Count); index++)
        {
            var key = record.GetRecordArrayElement(7, index);
            ValidateHeader(key, s_sizeKeySchemaId, fieldCount: 2);
            var age = ReadFloat(key, 1);
            var size = ReadVector2(key, 2);

            if ((index == 0) && (BitConverter.SingleToUInt32Bits(age) != 0))
            {
                throw new InvalidDataException("Cosmetic particle size curves must start at positive zero.");
            }

            if ((age <= previousAge) || (size.X <= 0.0f) || (size.Y <= 0.0f))
            {
                throw new InvalidDataException("Cosmetic particle size keys must ascend and contain positive finite sizes.");
            }

            keys.Add(new CosmeticParticleEmitterAsset.SizeKey(age, size));
            previousAge = age;
        }

        if (previousAge != 1.0f)
        {
            throw new InvalidDataException("Cosmetic particle size curves must end at one.");
        }

        return keys.MoveToImmutable();
    }

    private static uint ReadUInt32(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, 3, 1);

        if (field.Payload.Length != sizeof(uint))
        {
            throw new InvalidDataException($"The cosmetic emitter field {tag} must contain one UInt32.");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(field.Payload);
    }

    private static ulong ReadUInt64(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, 4, 1);

        if (field.Payload.Length != sizeof(ulong))
        {
            throw new InvalidDataException($"The cosmetic emitter field {tag} must contain one UInt64.");
        }

        return BinaryPrimitives.ReadUInt64LittleEndian(field.Payload);
    }

    private static byte ReadUInt8(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, 1, 1);

        if (field.Payload.Length != sizeof(byte))
        {
            throw new InvalidDataException($"The cosmetic emitter field {tag} must contain one UInt8.");
        }

        return field.Payload[0];
    }

    private static Vector2 ReadVector2(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, 14, 1);

        if (field.Payload.Length != 2 * sizeof(float))
        {
            throw new InvalidDataException($"The cosmetic emitter field {tag} must contain one Vector2.");
        }

        var value = EbinPrimitives.Vector2(field.Payload);
        return value.IsFinite().All()
            ? value
            : throw new InvalidDataException($"The cosmetic emitter field {tag} must be finite.");
    }

    private static Vector3 ReadVector3(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, 15, 1);

        if (field.Payload.Length != 3 * sizeof(float))
        {
            throw new InvalidDataException($"The cosmetic emitter field {tag} must contain one Vector3.");
        }

        var value = EbinPrimitives.Vector3(field.Payload);
        return value.IsFinite().All()
            ? value
            : throw new InvalidDataException($"The cosmetic emitter field {tag} must be finite.");
    }

    private static Vector4 ReadVector4(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, 16, 1);

        if (field.Payload.Length != 4 * sizeof(float))
        {
            throw new InvalidDataException($"The cosmetic emitter field {tag} must contain one Vector4.");
        }

        var value = EbinPrimitives.Vector4(field.Payload);
        return value.IsFinite().All()
            ? value
            : throw new InvalidDataException($"The cosmetic emitter field {tag} must be finite.");
    }

    private static void ValidateHeader(EbinRecordReadView record, Guid schemaId, int fieldCount)
    {
        var header = record.Header;

        if ((header.SchemaId != schemaId) || (header.Major != 1) || (header.Minor != 0))
        {
            throw new NotSupportedException("The selected cosmetic particle nested schema version is unsupported.");
        }

        if (header.FieldCount != fieldCount)
        {
            throw new InvalidDataException("A cosmetic particle nested record has an invalid field count.");
        }
    }

    public BinaryRecordStatus Read(
        scoped ReadOnlySpan<byte> source,
        AssetCodecContext context,
        [MaybeNull] out CosmeticParticleEmitterAsset value,
        out int bytesConsumed,
        out BinarySchemaDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        value = null;
        bytesConsumed = 0;
        diagnostic = null;

        if (source.Length > MaximumRecordBytes)
        {
            diagnostic = CreateDiagnostic("cosmetic-emitter-record-byte-limit");
            return BinaryRecordStatus.InvalidRecord;
        }

        var action = new ReadAction(context);

        try
        {
            var status = EbinStructuralReader.Read(
                source,
                context.ReadLimits,
                EbinReadPolicy.Canonical,
                ref action,
                out bytesConsumed,
                out var readDiagnostic);

            if (status is not EbinReadStatus.Complete)
            {
                diagnostic = CreateDiagnostic(readDiagnostic?.Category ?? "invalid-cosmetic-emitter-record");
                bytesConsumed = 0;
                return status switch {
                    EbinReadStatus.NeedMoreData => BinaryRecordStatus.NeedMoreData,
                    EbinReadStatus.UnsupportedSchema => BinaryRecordStatus.UnsupportedSchema,
                    _ => BinaryRecordStatus.InvalidRecord,
                };
            }
        }
        catch (NotSupportedException)
        {
            diagnostic = CreateDiagnostic("unsupported-cosmetic-emitter-schema");
            bytesConsumed = 0;
            return BinaryRecordStatus.UnsupportedSchema;
        }
        catch (InvalidDataException)
        {
            diagnostic = CreateDiagnostic("invalid-cosmetic-emitter-record");
            bytesConsumed = 0;
            return BinaryRecordStatus.InvalidRecord;
        }

        if ((bytesConsumed != source.Length) || (action.Value is null))
        {
            diagnostic = CreateDiagnostic("incomplete-cosmetic-emitter-value");
            bytesConsumed = 0;
            return BinaryRecordStatus.InvalidRecord;
        }

        value = action.Value;
        return BinaryRecordStatus.Complete;
    }

    public BinaryRecordStatus Write(
        in CosmeticParticleEmitterAsset value,
        AssetCodecContext context,
        scoped Span<byte> destination,
        out int bytesWritten,
        out BinarySchemaDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        bytesWritten = 0;
        diagnostic = null;

        if ((value is null) || (value.AssetId != context.RootAssetId))
        {
            diagnostic = CreateDiagnostic("invalid-cosmetic-emitter-value");
            return BinaryRecordStatus.InvalidRecord;
        }

        if (destination.Length < value.EncodedRecord.Length)
        {
            return BinaryRecordStatus.DestinationTooSmall;
        }

        value.EncodedRecord.AsSpan().CopyTo(destination);
        bytesWritten = value.EncodedRecord.Length;
        return BinaryRecordStatus.Complete;
    }

    private struct ReadAction : IAction<EbinRecordReadView>
    {
        private readonly AssetCodecContext _context;

        internal ReadAction(AssetCodecContext context)
        {
            _context = context;
            Value = null;
        }

        internal CosmeticParticleEmitterAsset? Value { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Value = ReadCosmeticParticleEmitter(record, _context);
    }
}
