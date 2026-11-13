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

namespace TerraFX.Rendering;

internal static partial class RenderComponentDefinitions
{
    private static readonly Guid s_meshAssetSchemaId = new ContentSchemaIdentityAssignment("native/mesh").SchemaId;
    private static readonly Guid s_meshComponentSchemaId = new ContentSchemaIdentityAssignment("render/mesh").SchemaId;
    private static readonly Guid s_meshAssetReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;

    internal static MeshAssetReferences GetMeshAssetReferences(scoped ReadOnlySpan<byte> source)
    {
        var action = new MeshAssetReferenceReadAction();
        var status = EbinStructuralReader.Read(
            source,
            EbinReadLimits.Foundation,
            EbinReadPolicy.Canonical,
            ref action,
            out var bytesConsumed,
            out var diagnostic);

        if ((status is not EbinReadStatus.Complete)
            || (bytesConsumed != source.Length)
            || (action.Value is not MeshAssetReferences references))
        {
            throw new InvalidDataException(
                $"The Mesh component asset reference could not be read: {diagnostic?.Category ?? status.ToString()}.");
        }

        return references;
    }

    internal static WorldRegistrationBuilder RegisterMesh(
        WorldRegistrationBuilder builder,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schemas);

        var schemaReference = new ContentSchemaReference(s_meshComponentSchemaId, 1, 0, ContentSchemaScope.Content);
        var componentLayout = new ComponentLayout(
            new ContentSchemaIdentityAssignment("render/mesh/layout/v1").SchemaId,
            Unsafe.SizeOf<MeshComponent>());
        var schema = ComponentSchema<MeshComponent>.FromRegistry(schemas, schemaReference, componentLayout);
        var meshAssetPath = MeshComponent.Field1StoragePath;
        var layerMaskPath = MeshComponent.Field2StoragePath;
        var poseModePath = MeshComponent.Field3StoragePath;
        var meshAsset = ComponentFieldBinding<MeshComponent, MeshAuthor>.AssetReference<
            MeshComponent.Field1Accessor,
            MeshComponent.Field1Reader>(
                schema.GetField(MeshComponent.Field1FieldPath),
                [meshAssetPath],
                default,
                default);
        var layerMask = ComponentFieldBinding<MeshComponent, MeshAuthor>.Scalar<
            ulong,
            MeshComponent.Field2Accessor,
            MeshComponent.Field2Reader>(
                schema.GetField(MeshComponent.Field2FieldPath),
                [layerMaskPath],
                default,
                default);
        var poseMode = ComponentFieldBinding<MeshComponent, MeshAuthor>.Scalar<
            byte,
            MeshComponent.Field3Accessor,
            MeshComponent.Field3Reader>(
                schema.GetField(MeshComponent.Field3FieldPath),
                [poseModePath],
                default,
                default);
        var meshAssetBytes = Unsafe.SizeOf<int>();
        var layerMaskBytes = Unsafe.SizeOf<ulong>();
        var poseModeBytes = Unsafe.SizeOf<byte>();
        var logicalBytes = checked(meshAssetBytes + layerMaskBytes + poseModeBytes);

        if (componentLayout.LogicalBytes != logicalBytes)
        {
            throw new InvalidOperationException("The Mesh component fields do not exactly cover their declared layout.");
        }

        var storage = new ComponentStorageDeclaration(
            logicalBytes,
            [
                new ComponentStorageLeaf(
                    meshAssetPath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<MeshComponent>("_meshAssetId").ToInt32(),
                    meshAssetBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(
                    layerMaskPath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<MeshComponent>("_layerMask").ToInt32(),
                    layerMaskBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(
                    poseModePath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<MeshComponent>("_poseMode").ToInt32(),
                    poseModeBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var factory = new MeshFactory(
            ComponentFactoryBinding<MeshComponent>.Create(
                new ContentSchemaIdentityAssignment("render/mesh/factory/v1").SchemaId,
                1,
                schema,
                storage,
                ComponentTransientRestore.None,
                [],
                [meshAsset, layerMask, poseMode],
                new MeshDefaultProvider(),
                new MeshRestoreProvider(),
                new MeshStateCodec(schemaReference)));

        return builder.RegisterComponent(schema, factory);
    }

    private static Diagnostic CreateMeshValidationDiagnostic(string field, string message)
        => new(
            "RenderMeshInvalid",
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

    internal readonly record struct MeshAssetReferences(int MeshAssetId, ulong LayerMask, byte PoseMode);

    internal readonly record struct MeshAuthor(int MeshAssetId, ulong LayerMask, byte PoseMode);

    [GenerateComponentBindings(typeof(MeshAuthor))]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal partial struct MeshComponent
    {
        [GenerateComponentFieldAccessor(1, nameof(MeshAuthor.MeshAssetId), "MeshAssetId")]
        private int _meshAssetId;

        [GenerateComponentFieldAccessor(2, nameof(MeshAuthor.LayerMask), "LayerMask")]
        private ulong _layerMask;

        [GenerateComponentFieldAccessor(3, nameof(MeshAuthor.PoseMode), "PoseMode")]
        private byte _poseMode;

        internal MeshComponent(int meshAssetId, ulong layerMask, byte poseMode)
        {
            _meshAssetId = meshAssetId;
            _layerMask = layerMask;
            _poseMode = poseMode;
        }

        internal readonly ulong LayerMask => _layerMask;

        internal readonly int MeshAssetId => _meshAssetId;

        internal readonly byte PoseMode => _poseMode;
    }

    private struct MeshAssetReferenceReadAction : IAction<EbinRecordReadView>
    {
        internal MeshAssetReferences? Value { get; private set; }

        private static bool IsCanonicalField(EbinFieldReadView field, ushort kind, int payloadBytes)
            => field.IsRequired
            && (field.Kind == kind)
            && (field.Count == 1)
            && (field.Payload.Length == payloadBytes);

        public void Invoke(scoped EbinRecordReadView record)
        {
            var header = record.Header;

            if ((header.SchemaId != s_meshComponentSchemaId)
                || (header.Major != 1)
                || (header.Minor != 0)
                || (header.FieldCount != 3)
                || !record.TryGetField(1, out var mesh)
                || !mesh.IsRequired
                || (mesh.Kind != 22)
                || (mesh.Count != 1)
                || !record.TryGetField(2, out var layerMask)
                || !IsCanonicalField(layerMask, 4, sizeof(ulong))
                || !record.TryGetField(3, out var poseMode)
                || !IsCanonicalField(poseMode, 1, sizeof(byte)))
            {
                throw new InvalidDataException("A Mesh component does not match its exact native/mesh 1 wire shape.");
            }

            var meshReference = record.GetRecord(1);
            var referenceHeader = meshReference.Header;

            if ((referenceHeader.SchemaId != s_meshAssetReferenceSchemaId)
                || (referenceHeader.Major != 1)
                || (referenceHeader.Minor != 0)
                || (referenceHeader.FieldCount != 3)
                || !meshReference.TryGetField(1, out var assetIdField)
                || !IsCanonicalField(assetIdField, 3, sizeof(uint))
                || !meshReference.TryGetField(2, out var schemaIdField)
                || !IsCanonicalField(schemaIdField, 11, 16)
                || !meshReference.TryGetField(3, out var schemaMajorField)
                || !IsCanonicalField(schemaMajorField, 3, sizeof(uint)))
            {
                throw new InvalidDataException("A Mesh component asset reference is malformed.");
            }

            var assetId = BinaryPrimitives.ReadUInt32LittleEndian(assetIdField.Payload);
            var schemaId = new Guid(schemaIdField.Payload, bigEndian: true);
            var schemaMajor = BinaryPrimitives.ReadUInt32LittleEndian(schemaMajorField.Payload);

            if ((assetId is 0 or > int.MaxValue)
                || (schemaId != s_meshAssetSchemaId)
                || (schemaMajor != 1))
            {
                throw new InvalidDataException("A Mesh component must reference a native/mesh 1 asset.");
            }

            Value = new MeshAssetReferences(
                checked((int)assetId),
                BinaryPrimitives.ReadUInt64LittleEndian(layerMask.Payload),
                poseMode.Payload[0]);
        }
    }

    private sealed class MeshDefaultProvider : IComponentDefaultProvider<MeshComponent>
    {
        public MeshComponent Create(ComponentDefaultBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            throw new InvalidOperationException("A Mesh component requires an explicit Mesh asset.");
        }
    }

    private sealed class MeshFactory : IComponentFactory<MeshComponent>
    {
        private readonly ComponentFactoryBinding<MeshComponent> _binding;

        internal MeshFactory(ComponentFactoryBinding<MeshComponent> binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            _binding = binding;
        }

        public ComponentFactoryBinding<MeshComponent> Binding => _binding;
    }

    private sealed class MeshRestoreProvider : IComponentRestoreProvider<MeshComponent, MeshAuthor>
    {
        public MeshComponent RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => default;

        public MeshComponent Restore(
            in MeshAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new MeshComponent(author.MeshAssetId, author.LayerMask, author.PoseMode);
        }
    }

    private sealed class MeshStateCodec : IComponentStateCodec<MeshComponent, MeshAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal MeshStateCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        private static EbinRecord CreateAssetReferenceRecord(int assetId)
            => new(
                s_meshAssetReferenceSchemaId,
                1,
                0,
                [
                    EbinPrimitives.UInt32(1, checked((uint)assetId)),
                    EbinPrimitives.Guid(2, s_meshAssetSchemaId),
                    EbinPrimitives.UInt32(3, 1),
                ]);

        private static byte[] CreateBytes(ContentSchemaReference schema, in MeshComponent value)
        {
            var status = EbinCodec.Prepare(CreateRecord(schema, in value), out var bytes, out var diagnostic);

            if (status is not EbinStatus.Complete)
            {
                throw new InvalidOperationException($"The Mesh component record could not be encoded: {diagnostic.Category}.");
            }

            return bytes;
        }

        private static EbinRecord CreateRecord(ContentSchemaReference schema, in MeshComponent value)
            => new(
                schema.SchemaId,
                checked((uint)schema.Major),
                checked((uint)schema.Minor),
                [
                    EbinPrimitives.Record(1, CreateAssetReferenceRecord(value.MeshAssetId)),
                    EbinPrimitives.UInt64(2, value.LayerMask),
                    EbinPrimitives.UInt8(3, value.PoseMode),
                ]);

        private static bool IsField(EbinFieldReadView field, ushort kind, int payloadBytes)
            => field.IsRequired
            && (field.Kind == kind)
            && (field.Count == 1)
            && (field.Payload.Length == payloadBytes);

        private static bool TryGetInvalidField(in MeshComponent value, out string field, out string message)
        {
            if (value.MeshAssetId <= 0)
            {
                field = "Mesh";
                message = "Mesh asset identity must be positive.";
                return true;
            }

            if (value.PoseMode != 0)
            {
                field = "PoseMode";
                message = "Only static BindPose is admitted by the selected Mesh component schema.";
                return true;
            }

            field = string.Empty;
            message = string.Empty;
            return false;
        }

        public nint Count(
            in MeshComponent value,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateBytes(_schema, in value).Length;
        }

        public ComponentReadResult<MeshAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<MeshAuthor> binding,
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
                ?? throw new InvalidOperationException("The Mesh component read did not issue an author result.");
        }

        public ImmutableArray<Diagnostic> Validate(
            in MeshComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetInvalidField(in value, out var field, out var message))
            {
                return [CreateMeshValidationDiagnostic(field, message)];
            }

            return binding.ValidateFields(in value);
        }

        public nint Write(
            in MeshComponent value,
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
            private readonly ComponentReadBinding<MeshAuthor> _binding;
            private readonly ContentSchemaReference _schema;
            private ComponentReadResult<MeshAuthor>? _result;

            internal ReadAction(
                ContentSchemaReference schema,
                ComponentReadBinding<MeshAuthor> binding)
            {
                _schema = schema;
                _binding = binding;
                _result = null;
            }

            internal readonly ComponentReadResult<MeshAuthor>? Result => _result;

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
                        "The Mesh component record does not match the selected schema tuple.");
                    return;
                }

                if ((record.FieldCount != 3)
                    || !record.TryGetField(1, out var mesh)
                    || !record.TryGetField(2, out var layerMask)
                    || !record.TryGetField(3, out var poseMode)
                    || !mesh.IsRequired
                    || (mesh.Kind != 22)
                    || (mesh.Count != 1)
                    || !IsField(layerMask, 4, sizeof(ulong))
                    || !IsField(poseMode, 1, sizeof(byte)))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        null,
                        "The Mesh component fields do not match the selected schema.");
                    return;
                }

                var meshReference = record.GetRecord(1);
                var referenceHeader = meshReference.Header;

                if ((referenceHeader.SchemaId != s_meshAssetReferenceSchemaId)
                    || (referenceHeader.Major != 1)
                    || (referenceHeader.Minor != 0)
                    || (referenceHeader.FieldCount != 3)
                    || !meshReference.TryGetField(1, out var assetIdField)
                    || !IsField(assetIdField, 3, sizeof(uint))
                    || !meshReference.TryGetField(2, out var schemaIdField)
                    || !IsField(schemaIdField, 11, 16)
                    || !meshReference.TryGetField(3, out var schemaMajorField)
                    || !IsField(schemaMajorField, 3, sizeof(uint)))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        null,
                        "The Mesh component asset reference is malformed.");
                    return;
                }

                var assetId = BinaryPrimitives.ReadUInt32LittleEndian(assetIdField.Payload);
                var schemaId = new Guid(schemaIdField.Payload, bigEndian: true);
                var schemaMajor = BinaryPrimitives.ReadUInt32LittleEndian(schemaMajorField.Payload);

                if ((assetId is 0 or > int.MaxValue)
                    || (schemaId != s_meshAssetSchemaId)
                    || (schemaMajor != 1))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        null,
                        "The Mesh component must reference a native/mesh 1 asset.");
                    return;
                }

                var author = new MeshAuthor(
                    checked((int)assetId),
                    BinaryPrimitives.ReadUInt64LittleEndian(layerMask.Payload),
                    poseMode.Payload[0]);
                _result = _binding.Complete(in author, record.EncodedBytes.Length);
            }
        }
    }
}
