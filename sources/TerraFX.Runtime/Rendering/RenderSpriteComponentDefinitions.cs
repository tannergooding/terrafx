// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Generators;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal static partial class RenderComponentDefinitions
{
    private static readonly Guid s_assetReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;
    private static readonly Guid s_atlasSchemaId = new ContentSchemaIdentityAssignment("native/atlas").SchemaId;
    private static readonly Guid s_materialSchemaId = new ContentSchemaIdentityAssignment("native/material").SchemaId;
    private static readonly Guid s_spriteSchemaId = new ContentSchemaIdentityAssignment("render/sprite").SchemaId;

    internal static SpriteAssetReferences GetSpriteAssetReferences(scoped ReadOnlySpan<byte> source)
    {
        var action = new SpriteAssetReferenceReadAction();
        var status = EbinStructuralReader.Read(
            source,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out var bytesConsumed,
            out var diagnostic);

        if ((status is not EbinReadStatus.Complete)
            || (bytesConsumed != source.Length)
            || (action.Value is not SpriteAssetReferences references))
        {
            throw new InvalidDataException(
                $"The Sprite asset references could not be read: {diagnostic?.Category ?? status.ToString()}.");
        }

        return references;
    }

    internal static WorldRegistrationBuilder RegisterSprite(
        WorldRegistrationBuilder builder,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schemas);

        var schemaReference = new ContentSchemaReference(s_spriteSchemaId, 1, 0, ContentSchemaScope.Content);
        var componentLayout = new ComponentLayout(
            new ContentSchemaIdentityAssignment("render/sprite/layout/v1").SchemaId,
            Unsafe.SizeOf<SpriteComponent>());
        var schema = ComponentSchema<SpriteComponent>.FromRegistry(schemas, schemaReference, componentLayout);
        var atlasAssetPath = SpriteComponent.Field1StoragePath;
        var frameIdPath = SpriteComponent.Field2StoragePath;
        var pivotPath = SpriteComponent.Field3StoragePath;
        var sizePath = SpriteComponent.Field4StoragePath;
        var tintPath = SpriteComponent.Field5StoragePath;
        var flipMaskPath = SpriteComponent.Field6StoragePath;
        var sortingLayerPath = SpriteComponent.Field7StoragePath;
        var sortingOrderPath = SpriteComponent.Field8StoragePath;
        var materialAssetPath = SpriteComponent.Field9StoragePath;
        var pixelSnapPath = SpriteComponent.Field10StoragePath;
        var atlasAsset = ComponentFieldBinding<SpriteComponent, SpriteAuthor>.AssetReference<
            SpriteComponent.Field1Accessor,
            SpriteComponent.Field1Reader>(
                schema.GetField(SpriteComponent.Field1FieldPath),
                [atlasAssetPath],
                default,
                default);
        var frameId = ComponentFieldBinding<SpriteComponent, SpriteAuthor>.Scalar<
            Guid,
            SpriteComponent.Field2Accessor,
            SpriteComponent.Field2Reader>(
                schema.GetField(SpriteComponent.Field2FieldPath),
                [frameIdPath],
                default,
                default);
        var pivot = ComponentFieldBinding<SpriteComponent, SpriteAuthor>.Scalar<
            Vector2,
            SpriteComponent.Field3Accessor,
            SpriteComponent.Field3Reader>(
                schema.GetField(SpriteComponent.Field3FieldPath),
                [pivotPath],
                default,
                default);
        var size = ComponentFieldBinding<SpriteComponent, SpriteAuthor>.Scalar<
            Vector2,
            SpriteComponent.Field4Accessor,
            SpriteComponent.Field4Reader>(
                schema.GetField(SpriteComponent.Field4FieldPath),
                [sizePath],
                default,
                default);
        var tint = ComponentFieldBinding<SpriteComponent, SpriteAuthor>.Scalar<
            Vector4,
            SpriteComponent.Field5Accessor,
            SpriteComponent.Field5Reader>(
                schema.GetField(SpriteComponent.Field5FieldPath),
                [tintPath],
                default,
                default);
        var flipMask = ComponentFieldBinding<SpriteComponent, SpriteAuthor>.Scalar<
            byte,
            SpriteComponent.Field6Accessor,
            SpriteComponent.Field6Reader>(
                schema.GetField(SpriteComponent.Field6FieldPath),
                [flipMaskPath],
                default,
                default);
        var sortingLayer = ComponentFieldBinding<SpriteComponent, SpriteAuthor>.Scalar<
            int,
            SpriteComponent.Field7Accessor,
            SpriteComponent.Field7Reader>(
                schema.GetField(SpriteComponent.Field7FieldPath),
                [sortingLayerPath],
                default,
                default);
        var sortingOrder = ComponentFieldBinding<SpriteComponent, SpriteAuthor>.Scalar<
            int,
            SpriteComponent.Field8Accessor,
            SpriteComponent.Field8Reader>(
                schema.GetField(SpriteComponent.Field8FieldPath),
                [sortingOrderPath],
                default,
                default);
        var materialAsset = ComponentFieldBinding<SpriteComponent, SpriteAuthor>.OptionalAssetReference<
            SpriteMaterialAssetAccessor,
            SpriteMaterialAssetAuthorReader>(
                schema.GetField(SpriteComponent.Field9FieldPath),
                [materialAssetPath],
                default,
                default);
        var pixelSnap = ComponentFieldBinding<SpriteComponent, SpriteAuthor>.Scalar<
            bool,
            SpriteComponent.Field10Accessor,
            SpriteComponent.Field10Reader>(
                schema.GetField(SpriteComponent.Field10FieldPath),
                [pixelSnapPath],
                default,
                default);
        var fieldSizes = new[] {
            Unsafe.SizeOf<int>(),
            Unsafe.SizeOf<Guid>(),
            Unsafe.SizeOf<Vector2>(),
            Unsafe.SizeOf<Vector2>(),
            Unsafe.SizeOf<Vector4>(),
            Unsafe.SizeOf<byte>(),
            Unsafe.SizeOf<int>(),
            Unsafe.SizeOf<int>(),
            Unsafe.SizeOf<OptionalAssetId>(),
            Unsafe.SizeOf<bool>(),
        };
        var fieldOffsets = new[] {
            Marshal.OffsetOf<SpriteComponent>("_atlasAssetId").ToInt32(),
            Marshal.OffsetOf<SpriteComponent>("_frameId").ToInt32(),
            Marshal.OffsetOf<SpriteComponent>("_pivot").ToInt32(),
            Marshal.OffsetOf<SpriteComponent>("_size").ToInt32(),
            Marshal.OffsetOf<SpriteComponent>("_tint").ToInt32(),
            Marshal.OffsetOf<SpriteComponent>("_flipMask").ToInt32(),
            Marshal.OffsetOf<SpriteComponent>("_sortingLayer").ToInt32(),
            Marshal.OffsetOf<SpriteComponent>("_sortingOrder").ToInt32(),
            Marshal.OffsetOf<SpriteComponent>("_materialAssetId").ToInt32(),
            Marshal.OffsetOf<SpriteComponent>("_pixelSnap").ToInt32(),
        };
        var logicalBytes = 0;

        foreach (var fieldSize in fieldSizes)
        {
            logicalBytes = checked(logicalBytes + fieldSize);
        }

        if (componentLayout.LogicalBytes != logicalBytes)
        {
            throw new InvalidOperationException("The Sprite component fields do not exactly cover their declared layout.");
        }

        var storage = new ComponentStorageDeclaration(
            logicalBytes,
            [
                new ComponentStorageLeaf(atlasAssetPath, ComponentStorageKind.Value, fieldOffsets[0], fieldSizes[0], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(flipMaskPath, ComponentStorageKind.Value, fieldOffsets[5], fieldSizes[5], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(frameIdPath, ComponentStorageKind.Value, fieldOffsets[1], fieldSizes[1], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(materialAssetPath, ComponentStorageKind.OptionalValue, fieldOffsets[8], fieldSizes[8], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(pixelSnapPath, ComponentStorageKind.Value, fieldOffsets[9], fieldSizes[9], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(pivotPath, ComponentStorageKind.Value, fieldOffsets[2], fieldSizes[2], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(sizePath, ComponentStorageKind.Value, fieldOffsets[3], fieldSizes[3], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(sortingLayerPath, ComponentStorageKind.Value, fieldOffsets[6], fieldSizes[6], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(sortingOrderPath, ComponentStorageKind.Value, fieldOffsets[7], fieldSizes[7], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(tintPath, ComponentStorageKind.Value, fieldOffsets[4], fieldSizes[4], 0, 0, ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var factory = new SpriteFactory(
            ComponentFactoryBinding<SpriteComponent>.Create(
                new ContentSchemaIdentityAssignment("render/sprite/factory/v1").SchemaId,
                1,
                schema,
                storage,
                ComponentTransientRestore.None,
                [],
                [
                    atlasAsset,
                    frameId,
                    pivot,
                    size,
                    tint,
                    flipMask,
                    sortingLayer,
                    sortingOrder,
                    materialAsset,
                    pixelSnap,
                ],
                new SpriteDefaultProvider(),
                new SpriteRestoreProvider(),
                new SpriteStateCodec(schemaReference)));

        return builder.RegisterComponent(schema, factory);
    }

    private static Diagnostic CreateSpriteValidationDiagnostic(string field, string message)
        => new(
            "RenderSpriteInvalid",
            DiagnosticSeverity.Error,
            0,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "Component.Validate",
            null,
            message,
            [new DiagnosticContextEntry("Field", field)]);

    private static bool IsSpriteField(EbinFieldReadView field, ushort kind, int payloadBytes)
        => field.IsRequired
        && (field.Kind == kind)
        && (field.Count == 1)
        && (field.Payload.Length == payloadBytes);

    private static bool TryReadAssetReference(
        EbinRecordReadView record,
        Guid expectedSchemaId,
        out int assetId)
    {
        var header = record.Header;

        if ((header.SchemaId != s_assetReferenceSchemaId)
            || (header.Major != 1)
            || (header.Minor != 0)
            || (header.FieldCount != 3)
            || !record.TryGetField(1, out var assetIdField)
            || !IsSpriteField(assetIdField, kind: 3, payloadBytes: sizeof(uint))
            || !record.TryGetField(2, out var schemaIdField)
            || !IsSpriteField(schemaIdField, kind: 11, payloadBytes: 16)
            || !record.TryGetField(3, out var schemaMajorField)
            || !IsSpriteField(schemaMajorField, kind: 3, payloadBytes: sizeof(uint)))
        {
            assetId = 0;
            return false;
        }

        var assetIdValue = BinaryPrimitives.ReadUInt32LittleEndian(assetIdField.Payload);
        var schemaId = new Guid(schemaIdField.Payload, bigEndian: true);
        var schemaMajor = BinaryPrimitives.ReadUInt32LittleEndian(schemaMajorField.Payload);

        if ((assetIdValue is 0 or > int.MaxValue)
            || (schemaId != expectedSchemaId)
            || (schemaMajor != 1))
        {
            assetId = 0;
            return false;
        }

        assetId = checked((int)assetIdValue);
        return true;
    }

    internal readonly struct OptionalAssetId
    {
        private readonly byte _hasValue;
        private readonly int _value;

        internal OptionalAssetId(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _hasValue = 1;
            _value = value;
        }

        internal bool HasValue => _hasValue != 0;

        internal int Value
        {
            get
            {
                if (!HasValue)
                {
                    throw new InvalidOperationException("The optional Material asset identity is absent.");
                }

                return _value;
            }
        }
    }

    internal readonly record struct SpriteAssetReferences(int AtlasAssetId, Guid FrameId, int? MaterialAssetId);

    internal readonly record struct SpriteAuthor(
        int AtlasAssetId,
        Guid FrameId,
        Vector2 Pivot,
        Vector2 Size,
        Vector4 Tint,
        byte FlipMask,
        int SortingLayer,
        int SortingOrder,
        OptionalAssetId MaterialAssetId,
        bool PixelSnap);

    [GenerateComponentBindings(typeof(SpriteAuthor))]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal partial struct SpriteComponent
    {
        [GenerateComponentFieldAccessor(1, nameof(SpriteAuthor.AtlasAssetId), "AtlasAssetId")]
        private int _atlasAssetId;

        [GenerateComponentFieldAccessor(6, nameof(SpriteAuthor.FlipMask), "FlipMask")]
        private byte _flipMask;

        [GenerateComponentFieldAccessor(2, nameof(SpriteAuthor.FrameId), "FrameId")]
        private Guid _frameId;

        [GenerateComponentFieldAccessor(9, nameof(SpriteAuthor.MaterialAssetId), "MaterialAssetId")]
        private OptionalAssetId _materialAssetId;

        [GenerateComponentFieldAccessor(10, nameof(SpriteAuthor.PixelSnap), "PixelSnap")]
        [MarshalAs(UnmanagedType.U1)]
        private bool _pixelSnap;

        [GenerateComponentFieldAccessor(3, nameof(SpriteAuthor.Pivot), "Pivot")]
        private Vector2 _pivot;

        [GenerateComponentFieldAccessor(4, nameof(SpriteAuthor.Size), "Size")]
        private Vector2 _size;

        [GenerateComponentFieldAccessor(7, nameof(SpriteAuthor.SortingLayer), "SortingLayer")]
        private int _sortingLayer;

        [GenerateComponentFieldAccessor(8, nameof(SpriteAuthor.SortingOrder), "SortingOrder")]
        private int _sortingOrder;

        [GenerateComponentFieldAccessor(5, nameof(SpriteAuthor.Tint), "Tint")]
        private Vector4 _tint;

        internal SpriteComponent(
            int atlasAssetId,
            Guid frameId,
            Vector2 pivot,
            Vector2 size,
            Vector4 tint,
            byte flipMask,
            int sortingLayer,
            int sortingOrder,
            OptionalAssetId materialAssetId,
            bool pixelSnap)
        {
            _atlasAssetId = atlasAssetId;
            _frameId = frameId;
            _flipMask = flipMask;
            _materialAssetId = materialAssetId;
            _pixelSnap = pixelSnap;
            _pivot = pivot;
            _size = size;
            _sortingLayer = sortingLayer;
            _sortingOrder = sortingOrder;
            _tint = tint;
        }

        internal readonly int AtlasAssetId => _atlasAssetId;

        internal readonly byte FlipMask => _flipMask;

        internal readonly Guid FrameId => _frameId;

        internal readonly OptionalAssetId MaterialAssetId => _materialAssetId;

        internal OptionalAssetId MaterialAssetStorage
        {
            readonly get
            {
                return _materialAssetId;
            }

            set
            {
                _materialAssetId = value;
            }
        }

        internal readonly Vector2 Pivot => _pivot;

        internal readonly bool PixelSnap => _pixelSnap;

        internal readonly Vector2 Size => _size;

        internal readonly int SortingLayer => _sortingLayer;

        internal readonly int SortingOrder => _sortingOrder;

        internal readonly Vector4 Tint => _tint;
    }

    private struct SpriteAssetReferenceReadAction : IAction<EbinRecordReadView>
    {
        internal SpriteAssetReferences? Value { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
        {
            var header = record.Header;

            if ((header.SchemaId != s_spriteSchemaId)
                || (header.Major != 1)
                || (header.Minor != 0)
                || (header.FieldCount is not 9 and not 10)
                || !record.TryGetField(1, out var atlasAsset)
                || !atlasAsset.IsRequired
                || (atlasAsset.Kind != 22)
                || (atlasAsset.Count != 1)
                || !record.TryGetField(2, out var frameIdField)
                || !IsSpriteField(frameIdField, kind: 11, payloadBytes: 16))
            {
                throw new InvalidDataException("A Sprite reference record has an invalid header or required field shape.");
            }

            var hasMaterial = record.TryGetField(9, out var materialAsset);

            if ((header.FieldCount != (hasMaterial ? 10 : 9))
                || (hasMaterial && (materialAsset.IsRequired || (materialAsset.Kind != 22) || (materialAsset.Count != 1))))
            {
                throw new InvalidDataException("A Sprite optional Material field has an invalid presence or wire shape.");
            }

            var frameId = new Guid(frameIdField.Payload, bigEndian: true);

            if (frameId == Guid.Empty
                || !TryReadAssetReference(record.GetRecord(1), s_atlasSchemaId, out var atlasAssetId))
            {
                throw new InvalidDataException("A Sprite must reference a nonempty frame in a native/atlas 1 asset.");
            }

            int? materialAssetId = null;

            if (hasMaterial)
            {
                if (!TryReadAssetReference(record.GetRecord(9), s_materialSchemaId, out var materialId))
                {
                    throw new InvalidDataException("A Sprite Material reference must select a native/material 1 asset.");
                }

                materialAssetId = materialId;
            }

            Value = new SpriteAssetReferences(atlasAssetId, frameId, materialAssetId);
        }
    }

    private sealed class SpriteDefaultProvider : IComponentDefaultProvider<SpriteComponent>
    {
        public SpriteComponent Create(ComponentDefaultBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            throw new InvalidOperationException("A Sprite requires an explicit Atlas asset and FrameId.");
        }
    }

    private sealed class SpriteFactory : IComponentFactory<SpriteComponent>
    {
        private readonly ComponentFactoryBinding<SpriteComponent> _binding;

        internal SpriteFactory(ComponentFactoryBinding<SpriteComponent> binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            _binding = binding;
        }

        public ComponentFactoryBinding<SpriteComponent> Binding => _binding;
    }

    private readonly struct SpriteMaterialAssetAccessor : IComponentOptionalAccessor<SpriteComponent, int>
    {
        public void Clear(ref SpriteComponent value) => value.MaterialAssetStorage = default;

        public int GetPresent(in SpriteComponent value) => value.MaterialAssetId.Value;

        public bool IsPresent(in SpriteComponent value) => value.MaterialAssetId.HasValue;

        public void SetPresent(ref SpriteComponent value, in int fieldValue)
            => value.MaterialAssetStorage = new OptionalAssetId(fieldValue);
    }

    private readonly struct SpriteMaterialAssetAuthorReader : IComponentOptionalReader<SpriteAuthor, int>
    {
        public int GetPresent(in SpriteAuthor value) => value.MaterialAssetId.Value;

        public bool IsPresent(in SpriteAuthor value) => value.MaterialAssetId.HasValue;
    }

    private sealed class SpriteRestoreProvider : IComponentRestoreProvider<SpriteComponent, SpriteAuthor>
    {
        public SpriteComponent RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => default;

        public SpriteComponent Restore(
            in SpriteAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new SpriteComponent(
                author.AtlasAssetId,
                author.FrameId,
                author.Pivot,
                author.Size,
                author.Tint,
                author.FlipMask,
                author.SortingLayer,
                author.SortingOrder,
                author.MaterialAssetId,
                author.PixelSnap);
        }
    }

    private sealed class SpriteStateCodec : IComponentStateCodec<SpriteComponent, SpriteAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal SpriteStateCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        private static EbinRecord CreateAssetReferenceRecord(int assetId, Guid schemaId)
            => new(
                s_assetReferenceSchemaId,
                1,
                0,
                [
                    EbinPrimitives.UInt32(1, checked((uint)assetId)),
                    EbinPrimitives.Guid(2, schemaId),
                    EbinPrimitives.UInt32(3, 1),
                ]);

        private static byte[] CreateBytes(ContentSchemaReference schema, in SpriteComponent value)
        {
            var status = EbinCodec.Prepare(CreateRecord(schema, in value), out var bytes, out var diagnostic);

            if (status is not EbinStatus.Complete)
            {
                throw new InvalidOperationException($"The Sprite record could not be encoded: {diagnostic.Category}.");
            }

            return bytes;
        }

        private static EbinRecord CreateRecord(ContentSchemaReference schema, in SpriteComponent value)
        {
            var fields = new EbinField[value.MaterialAssetId.HasValue ? 10 : 9];
            var index = 0;
            fields[index++] = EbinPrimitives.Record(
                1,
                CreateAssetReferenceRecord(value.AtlasAssetId, s_atlasSchemaId));
            fields[index++] = EbinPrimitives.Guid(2, value.FrameId);
            fields[index++] = EbinPrimitives.Geometry(3, value.Pivot);
            fields[index++] = EbinPrimitives.Geometry(4, value.Size);
            fields[index++] = EbinPrimitives.Geometry(5, value.Tint);
            fields[index++] = EbinPrimitives.UInt8(6, value.FlipMask);
            fields[index++] = EbinPrimitives.Int32(7, value.SortingLayer);
            fields[index++] = EbinPrimitives.Int32(8, value.SortingOrder);

            if (value.MaterialAssetId.HasValue)
            {
                var requiredRecordField = EbinPrimitives.Record(
                    9,
                    CreateAssetReferenceRecord(value.MaterialAssetId.Value, s_materialSchemaId));
                fields[index++] = new EbinField(9, 22, 0, 1, requiredRecordField.Payload);
            }

            fields[index++] = EbinPrimitives.Boolean(10, value.PixelSnap);

            if (index != fields.Length)
            {
                throw new InvalidOperationException("The Sprite field count did not match its optional Material value.");
            }

            return new EbinRecord(
                schema.SchemaId,
                checked((uint)schema.Major),
                checked((uint)schema.Minor),
                fields);
        }

        private static EbinFieldReadView GetRequiredField(
            EbinRecordReadView record,
            uint tag,
            ushort kind,
            int payloadBytes)
        {
            if (!record.TryGetField(tag, out var field)
                || !field.IsRequired
                || (field.Kind != kind)
                || (field.Count != 1)
                || (field.Payload.Length != payloadBytes))
            {
                throw new InvalidDataException($"The Sprite record field {tag} does not match its required wire shape.");
            }

            return field;
        }

        private static bool IsField(EbinFieldReadView field, ushort kind, int payloadBytes)
            => field.IsRequired
            && (field.Kind == kind)
            && (field.Count == 1)
            && (field.Payload.Length == payloadBytes);

        private static Guid ReadGuid(EbinRecordReadView record, uint tag)
        {
            var field = GetRequiredField(record, tag, kind: 11, payloadBytes: 16);
            return new Guid(field.Payload, bigEndian: true);
        }

        private static Vector2 ReadVector2(EbinRecordReadView record, uint tag)
        {
            var field = GetRequiredField(record, tag, kind: 14, payloadBytes: 8);
            return new Vector2(
                EbinPrimitives.Single(field.Payload),
                EbinPrimitives.Single(field.Payload[4..]));
        }

        private static Vector4 ReadVector4(EbinRecordReadView record, uint tag)
        {
            var field = GetRequiredField(record, tag, kind: 16, payloadBytes: 16);
            return new Vector4(
                EbinPrimitives.Single(field.Payload),
                EbinPrimitives.Single(field.Payload[4..]),
                EbinPrimitives.Single(field.Payload[8..]),
                EbinPrimitives.Single(field.Payload[12..]));
        }

        private static bool TryGetInvalidField(in SpriteComponent value, out string field, out string message)
        {
            if (value.AtlasAssetId <= 0)
            {
                field = "Atlas";
                message = "Sprite Atlas identity must be positive.";
                return true;
            }

            if (value.FrameId == Guid.Empty)
            {
                field = "FrameId";
                message = "Sprite FrameId must be nonempty.";
                return true;
            }

            if (!value.Pivot.IsFinite().All()
                || (value.Pivot.X < 0.0f)
                || (value.Pivot.X > 1.0f)
                || (value.Pivot.Y < 0.0f)
                || (value.Pivot.Y > 1.0f))
            {
                field = "Pivot";
                message = "Sprite pivot must be finite and within [0,1].";
                return true;
            }

            if (!value.Size.IsFinite().All() || (value.Size.X <= 0.0f) || (value.Size.Y <= 0.0f))
            {
                field = "Size";
                message = "Sprite size must be finite and positive.";
                return true;
            }

            if (!value.Tint.IsFinite().All())
            {
                field = "Tint";
                message = "Sprite tint must be finite.";
                return true;
            }

            if ((value.FlipMask & ~0x03) != 0)
            {
                field = "FlipMask";
                message = "Sprite FlipMask contains an undefined bit.";
                return true;
            }

            if (value.MaterialAssetId.HasValue && (value.MaterialAssetId.Value <= 0))
            {
                field = "Material";
                message = "Sprite Material identity must be positive when present.";
                return true;
            }

            field = string.Empty;
            message = string.Empty;
            return false;
        }

        public nint Count(
            in SpriteComponent value,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateBytes(_schema, in value).Length;
        }

        public ComponentReadResult<SpriteAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<SpriteAuthor> binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = new ReadAction(_schema, binding);
            var status = EbinStructuralReader.Read(
                source,
                EbinReadLimits.Foundation,
                EbinReadPolicy.Canonical,
                ref action,
                out _,
                out var diagnostic);

            if (status is not EbinReadStatus.Complete)
            {
                return binding.Reject(status, diagnostic);
            }

            return action.Result
                ?? throw new InvalidOperationException("The Sprite read did not issue an author result.");
        }

        public ImmutableArray<Diagnostic> Validate(
            in SpriteComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetInvalidField(in value, out var field, out var message))
            {
                return [CreateSpriteValidationDiagnostic(field, message)];
            }

            return binding.ValidateFields(in value);
        }

        public nint Write(
            in SpriteComponent value,
            scoped Span<byte> destination,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = CreateBytes(_schema, in value);

            if (destination.Length < bytes.Length)
            {
                throw new ArgumentException("The component destination is too small.", nameof(destination));
            }

            bytes.AsSpan().CopyTo(destination);
            return bytes.Length;
        }

        private struct ReadAction : IAction<EbinRecordReadView>
        {
            private readonly ComponentReadBinding<SpriteAuthor> _binding;
            private readonly ContentSchemaReference _schema;
            private ComponentReadResult<SpriteAuthor>? _result;

            internal ReadAction(ContentSchemaReference schema, ComponentReadBinding<SpriteAuthor> binding)
            {
                _schema = schema;
                _binding = binding;
                _result = null;
            }

            internal readonly ComponentReadResult<SpriteAuthor>? Result => _result;

            public void Invoke(scoped EbinRecordReadView record)
            {
                var header = record.Header;

                if ((header.SchemaId != _schema.SchemaId)
                    || (header.Major != _schema.Major)
                    || (header.Minor != _schema.Minor))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.SchemaMismatch,
                        16,
                        null,
                        "The Sprite record does not match the selected schema tuple.");
                    return;
                }

                if ((record.FieldCount is not 9 and not 10)
                    || !record.TryGetField(1, out var atlasAsset)
                    || !atlasAsset.IsRequired
                    || (atlasAsset.Kind != 22)
                    || (atlasAsset.Count != 1)
                    || !record.TryGetField(2, out var frameId)
                    || !IsField(frameId, 11, 16)
                    || !record.TryGetField(3, out var pivot)
                    || !IsField(pivot, 14, 8)
                    || !record.TryGetField(4, out var size)
                    || !IsField(size, 14, 8)
                    || !record.TryGetField(5, out var tint)
                    || !IsField(tint, 16, 16)
                    || !record.TryGetField(6, out var flipMask)
                    || !IsField(flipMask, 1, 1)
                    || !record.TryGetField(7, out var sortingLayer)
                    || !IsField(sortingLayer, 7, sizeof(int))
                    || !record.TryGetField(8, out var sortingOrder)
                    || !IsField(sortingOrder, 7, sizeof(int))
                    || !record.TryGetField(10, out var pixelSnap)
                    || !IsField(pixelSnap, 9, 1))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        null,
                        "The Sprite fields do not match the selected schema.");
                    return;
                }

                var hasMaterialField = record.TryGetField(9, out var materialAsset);

                if (record.FieldCount != (hasMaterialField ? 10 : 9))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        9,
                        null,
                        "The Sprite record contains an unknown field.");
                    return;
                }

                if (!RenderComponentDefinitions.TryReadAssetReference(
                    record.GetRecord(1),
                    s_atlasSchemaId,
                    out var atlasAssetId))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        1,
                        null,
                        "The Sprite Atlas reference does not match native/atlas 1.");
                    return;
                }

                var materialAssetId = default(OptionalAssetId);

                if (hasMaterialField)
                {
                    if (materialAsset.IsRequired
                        || (materialAsset.Kind != 22)
                        || (materialAsset.Count != 1))
                    {
                        _result = _binding.Reject(
                            ComponentReadFailureKind.InvalidField,
                            9,
                            null,
                            "The Sprite Material field must be an optional asset-reference record.");
                        return;
                    }

                    if (!RenderComponentDefinitions.TryReadAssetReference(
                        record.GetRecord(9),
                        s_materialSchemaId,
                        out var materialAssetIdValue))
                    {
                        _result = _binding.Reject(
                            ComponentReadFailureKind.InvalidField,
                            9,
                            null,
                            "The Sprite Material reference does not match native/material 1.");
                        return;
                    }

                    materialAssetId = new OptionalAssetId(materialAssetIdValue);
                }

                var author = new SpriteAuthor(
                    atlasAssetId,
                    ReadGuid(record, tag: 2),
                    ReadVector2(record, tag: 3),
                    ReadVector2(record, tag: 4),
                    ReadVector4(record, tag: 5),
                    flipMask.Payload[0],
                    BinaryPrimitives.ReadInt32LittleEndian(sortingLayer.Payload),
                    BinaryPrimitives.ReadInt32LittleEndian(sortingOrder.Payload),
                    materialAssetId,
                    pixelSnap.Payload[0] != 0);
                _result = _binding.Complete(in author, record.EncodedBytes.Length);
            }
        }
    }
}
