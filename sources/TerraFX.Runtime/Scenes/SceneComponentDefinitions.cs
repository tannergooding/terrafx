// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Generators;
using TerraFX.Numerics;

namespace TerraFX.Scenes;

internal static partial class SceneComponentDefinitions
{
    internal static LocalTransformComponent CreateAssignedLocalTransform(
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        if (!position.IsFinite().All())
        {
            throw new ArgumentException("LocalTransform position must be finite.", nameof(position));
        }

        var rotationLengthSquared = Quaternion.Dot(rotation, rotation);

        if (!rotation.IsFinite().All()
            || !float.IsFinite(rotationLengthSquared)
            || (rotationLengthSquared <= 1.0e-12f))
        {
            throw new ArgumentException("LocalTransform rotation must have a finite nonzero norm.", nameof(rotation));
        }

        if (!IsScaleValid(scale))
        {
            throw new ArgumentException("LocalTransform scale must be finite with component magnitudes in [0.0001, 1000].", nameof(scale));
        }

        return new LocalTransformComponent(position, Quaternion.Normalize(rotation), scale);
    }

    internal static WorldRegistrationBuilder RegisterLocalTransform(
        WorldRegistrationBuilder builder,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schemas);

        var assignment = new ContentSchemaIdentityAssignment("scene/local-transform");
        var schemaReference = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var componentLayout = new ComponentLayout(
            new ContentSchemaIdentityAssignment("scene/local-transform/layout/v1").SchemaId,
            Unsafe.SizeOf<LocalTransformComponent>());
        var schema = ComponentSchema<LocalTransformComponent>.FromRegistry(schemas, schemaReference, componentLayout);
        var positionPath = LocalTransformComponent.Field1StoragePath;
        var rotationPath = LocalTransformComponent.Field2StoragePath;
        var scalePath = LocalTransformComponent.Field3StoragePath;
        var position = ComponentFieldBinding<LocalTransformComponent, LocalTransformAuthor>
            .Scalar<Vector3, LocalTransformComponent.Field1Accessor, LocalTransformComponent.Field1Reader>(
                schema.GetField(LocalTransformComponent.Field1FieldPath),
                [positionPath],
                default,
                default);
        var rotation = ComponentFieldBinding<LocalTransformComponent, LocalTransformAuthor>
            .Scalar<Quaternion, LocalTransformComponent.Field2Accessor, LocalTransformComponent.Field2Reader>(
                schema.GetField(LocalTransformComponent.Field2FieldPath),
                [rotationPath],
                default,
                default);
        var scale = ComponentFieldBinding<LocalTransformComponent, LocalTransformAuthor>
            .Scalar<Vector3, LocalTransformComponent.Field3Accessor, LocalTransformComponent.Field3Reader>(
                schema.GetField(LocalTransformComponent.Field3FieldPath),
                [scalePath],
                default,
                default);
        var positionBytes = Unsafe.SizeOf<Vector3>();
        var rotationBytes = Unsafe.SizeOf<Quaternion>();
        var scaleBytes = Unsafe.SizeOf<Vector3>();
        var logicalBytes = checked(positionBytes + rotationBytes + scaleBytes);

        if (componentLayout.LogicalBytes != logicalBytes)
        {
            throw new InvalidOperationException("The LocalTransform component fields do not exactly cover their declared layout.");
        }

        var storage = new ComponentStorageDeclaration(
            componentLayout.LogicalBytes,
            [
                new ComponentStorageLeaf(
                    positionPath,
                    ComponentStorageKind.Value,
                    0,
                    positionBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(
                    rotationPath,
                    ComponentStorageKind.Value,
                    positionBytes,
                    rotationBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(
                    scalePath,
                    ComponentStorageKind.Value,
                    checked(positionBytes + rotationBytes),
                    scaleBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var factory = new LocalTransformFactory(
            ComponentFactoryBinding<LocalTransformComponent>.Create(
                new ContentSchemaIdentityAssignment("scene/local-transform/factory/v1").SchemaId,
                1,
                schema,
                storage,
                ComponentTransientRestore.None,
                [],
                [position, rotation, scale],
                new LocalTransformDefaultProvider(),
                new LocalTransformRestoreProvider(),
                new LocalTransformStateCodec(schemaReference)));

        return builder.RegisterComponent(schema, factory);
    }

    internal static WorldRegistrationBuilder RegisterRootPosition(
        WorldRegistrationBuilder builder,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schemas);

        var assignment = new ContentSchemaIdentityAssignment("scene/root-position");
        var schemaReference = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var componentLayout = new ComponentLayout(
            new ContentSchemaIdentityAssignment("scene/root-position/layout/v1").SchemaId,
            Unsafe.SizeOf<RootPositionComponent>());
        var schema = ComponentSchema<RootPositionComponent>.FromRegistry(schemas, schemaReference, componentLayout);
        var positionPath = RootPositionComponent.Field1StoragePath;
        var position = ComponentFieldBinding<RootPositionComponent, RootPositionAuthor>
            .Scalar<SpatialPosition3, RootPositionComponent.Field1Accessor, RootPositionComponent.Field1Reader>(
                schema.GetField(RootPositionComponent.Field1FieldPath),
                [positionPath],
                default,
                default);
        var storage = new ComponentStorageDeclaration(
            componentLayout.LogicalBytes,
            [
                new ComponentStorageLeaf(
                    positionPath,
                    ComponentStorageKind.Value,
                    0,
                    Unsafe.SizeOf<SpatialPosition3>(),
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var factory = new RootPositionFactory(
            ComponentFactoryBinding<RootPositionComponent>.Create<RootPositionAuthor>(
                new ContentSchemaIdentityAssignment("scene/root-position/factory/v1").SchemaId,
                1,
                schema,
                storage,
                ComponentTransientRestore.None,
                [],
                [position],
                new RootPositionDefaultProvider(),
                new RootPositionRestoreProvider(),
                new RootPositionStateCodec(schemaReference)));

        return builder.RegisterComponent(schema, factory);
    }

    private static bool IsScaleValid(Vector3 scale)
        => scale.IsFinite().All()
            && (float.Abs(scale.X) >= 0.0001f)
            && (float.Abs(scale.X) <= 1000.0f)
            && (float.Abs(scale.Y) >= 0.0001f)
            && (float.Abs(scale.Y) <= 1000.0f)
            && (float.Abs(scale.Z) >= 0.0001f)
            && (float.Abs(scale.Z) <= 1000.0f);

    internal readonly record struct LocalTransformAuthor(Vector3 Position, Quaternion Rotation, Vector3 Scale);

    [GenerateComponentBindings(typeof(LocalTransformAuthor))]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal partial struct LocalTransformComponent
    {
        [GenerateComponentFieldAccessor(1, nameof(LocalTransformAuthor.Position), "Position")]
        private Vector3 _position;

        [GenerateComponentFieldAccessor(2, nameof(LocalTransformAuthor.Rotation), "Rotation")]
        private Quaternion _rotation;

        [GenerateComponentFieldAccessor(3, nameof(LocalTransformAuthor.Scale), "Scale")]
        private Vector3 _scale;

        internal LocalTransformComponent(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            _position = position;
            _rotation = rotation;
            _scale = scale;
        }

        internal readonly Vector3 Position => _position;

        internal readonly Quaternion Rotation => _rotation;

        internal readonly Vector3 Scale => _scale;
    }

    internal readonly record struct RootPositionAuthor(SpatialPosition3 Position);

    [GenerateComponentBindings(typeof(RootPositionAuthor))]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal partial struct RootPositionComponent
    {
        [GenerateComponentFieldAccessor(1, nameof(RootPositionAuthor.Position), "Position")]
        private SpatialPosition3 _position;

        internal RootPositionComponent(SpatialPosition3 position)
        {
            _position = position;
        }

        internal readonly SpatialPosition3 Position => _position;
    }

    private sealed class LocalTransformDefaultProvider : IComponentDefaultProvider<LocalTransformComponent>
    {
        public LocalTransformComponent Create(ComponentDefaultBinding binding)
            => new(Vector3.Zero, Quaternion.Identity, Vector3.One);
    }

    private sealed class LocalTransformFactory : IComponentFactory<LocalTransformComponent>
    {
        private readonly ComponentFactoryBinding<LocalTransformComponent> _binding;

        internal LocalTransformFactory(ComponentFactoryBinding<LocalTransformComponent> binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            _binding = binding;
        }

        public ComponentFactoryBinding<LocalTransformComponent> Binding => _binding;
    }

    private sealed class LocalTransformRestoreProvider : IComponentRestoreProvider<LocalTransformComponent, LocalTransformAuthor>
    {
        public LocalTransformComponent RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => default;

        public LocalTransformComponent Restore(
            in LocalTransformAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new LocalTransformComponent(author.Position, author.Rotation, author.Scale);
        }
    }

    private sealed class LocalTransformStateCodec : IComponentStateCodec<LocalTransformComponent, LocalTransformAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal LocalTransformStateCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        private static byte[] CreateBytes(ContentSchemaReference schema, in LocalTransformComponent value)
        {
            var status = EbinCodec.Prepare(CreateRecord(schema, in value), out var bytes, out var diagnostic);

            if (status != EbinStatus.Complete)
            {
                throw new InvalidOperationException($"The LocalTransform record could not be encoded: {diagnostic.Category}.");
            }

            return bytes;
        }

        private static EbinRecord CreateRecord(ContentSchemaReference schema, in LocalTransformComponent value)
            => new EbinRecord(schema.SchemaId, checked((uint)schema.Major), checked((uint)schema.Minor), [
                EbinPrimitives.Geometry(1, value.Position),
                EbinPrimitives.Geometry(2, value.Rotation),
                EbinPrimitives.Geometry(3, value.Scale),
            ]);

        private static Diagnostic CreateValidationDiagnostic(string field, string message)
            => new(
                "SceneLocalTransformInvalid",
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

        public nint Count(
            in LocalTransformComponent value,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateBytes(_schema, in value).Length;
        }

        public ComponentReadResult<LocalTransformAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<LocalTransformAuthor> binding,
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

            if (status != EbinReadStatus.Complete)
            {
                return binding.Reject(status, diagnostic);
            }

            return action.Result
                ?? throw new InvalidOperationException("The LocalTransform read did not issue an author result.");
        }

        public ImmutableArray<Diagnostic> Validate(
            in LocalTransformComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!value.Position.IsFinite().All())
            {
                return [CreateValidationDiagnostic("Position", "LocalTransform position must be finite.")];
            }

            var rotationLengthSquared = Quaternion.Dot(value.Rotation, value.Rotation);

            if (!value.Rotation.IsFinite().All()
                || !float.IsFinite(rotationLengthSquared)
                || (rotationLengthSquared <= 1.0e-12f))
            {
                return [CreateValidationDiagnostic("Rotation", "LocalTransform rotation must have a finite nonzero norm.")];
            }

            if (!IsScaleValid(value.Scale))
            {
                return [CreateValidationDiagnostic("Scale", "LocalTransform scale must be finite with component magnitudes in [0.0001, 1000].")];
            }

            return binding.ValidateFields(in value);
        }

        public nint Write(
            in LocalTransformComponent value,
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
            private readonly ComponentReadBinding<LocalTransformAuthor> _binding;
            private readonly ContentSchemaReference _schema;
            private ComponentReadResult<LocalTransformAuthor>? _result;

            internal ReadAction(ContentSchemaReference schema, ComponentReadBinding<LocalTransformAuthor> binding)
            {
                _schema = schema;
                _binding = binding;
                _result = null;
            }

            internal readonly ComponentReadResult<LocalTransformAuthor>? Result => _result;

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
                        "The LocalTransform record does not match the selected schema tuple.");
                    return;
                }

                if ((record.FieldCount != 3)
                    || !record.TryGetField(1, out var position)
                    || !record.TryGetField(2, out var rotation)
                    || !record.TryGetField(3, out var scale)
                    || (position.Kind != 15)
                    || (rotation.Kind != 17)
                    || (scale.Kind != 15)
                    || (position.Count != 1)
                    || (rotation.Count != 1)
                    || (scale.Count != 1)
                    || (position.Payload.Length != 12)
                    || (rotation.Payload.Length != 16)
                    || (scale.Payload.Length != 12))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        null,
                        "The LocalTransform fields do not match the selected schema.");
                    return;
                }

                var author = new LocalTransformAuthor(
                    EbinPrimitives.Vector3(position.Payload),
                    new Quaternion(
                        EbinPrimitives.Single(rotation.Payload),
                        EbinPrimitives.Single(rotation.Payload[4..]),
                        EbinPrimitives.Single(rotation.Payload[8..]),
                        EbinPrimitives.Single(rotation.Payload[12..])),
                    EbinPrimitives.Vector3(scale.Payload));
                _result = _binding.Complete(in author, record.EncodedBytes.Length);
            }
        }
    }

    private sealed class RootPositionDefaultProvider : IComponentDefaultProvider<RootPositionComponent>
    {
        public RootPositionComponent Create(ComponentDefaultBinding binding) => new(default);
    }

    private sealed class RootPositionFactory : IComponentFactory<RootPositionComponent>
    {
        private readonly ComponentFactoryBinding<RootPositionComponent> _binding;

        internal RootPositionFactory(ComponentFactoryBinding<RootPositionComponent> binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            _binding = binding;
        }

        public ComponentFactoryBinding<RootPositionComponent> Binding => _binding;
    }

    private sealed class RootPositionRestoreProvider : IComponentRestoreProvider<RootPositionComponent, RootPositionAuthor>
    {
        public RootPositionComponent RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => default;

        public RootPositionComponent Restore(
            in RootPositionAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new RootPositionComponent(author.Position);
        }
    }

    private sealed class RootPositionStateCodec : IComponentStateCodec<RootPositionComponent, RootPositionAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal RootPositionStateCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        private static byte[] CreateBytes(ContentSchemaReference schema, in RootPositionComponent value)
        {
            var status = EbinCodec.Prepare(CreateRecord(schema, in value), out var bytes, out var diagnostic);

            if (status != EbinStatus.Complete)
            {
                throw new InvalidOperationException($"The RootPosition record could not be encoded: {diagnostic.Category}.");
            }

            return bytes;
        }

        private static EbinRecord CreateRecord(ContentSchemaReference schema, in RootPositionComponent value)
            => new EbinRecord(schema.SchemaId, checked((uint)schema.Major), checked((uint)schema.Minor), [
                EbinPrimitives.Position(1, value.Position),
            ]);

        public nint Count(
            in RootPositionComponent value,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateBytes(_schema, in value).Length;
        }

        public ComponentReadResult<RootPositionAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<RootPositionAuthor> binding,
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

            if (status != EbinReadStatus.Complete)
            {
                return binding.Reject(status, diagnostic);
            }

            return action.Result
                ?? throw new InvalidOperationException("The RootPosition read did not issue an author result.");
        }

        public ImmutableArray<Diagnostic> Validate(
            in RootPositionComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return binding.ValidateFields(in value);
        }

        public nint Write(
            in RootPositionComponent value,
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
            private readonly ComponentReadBinding<RootPositionAuthor> _binding;
            private readonly ContentSchemaReference _schema;
            private ComponentReadResult<RootPositionAuthor>? _result;

            internal ReadAction(ContentSchemaReference schema, ComponentReadBinding<RootPositionAuthor> binding)
            {
                _schema = schema;
                _binding = binding;
                _result = null;
            }

            internal readonly ComponentReadResult<RootPositionAuthor>? Result => _result;

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
                        "The RootPosition record does not match the selected schema tuple.");
                    return;
                }

                if ((record.FieldCount != 1)
                    || !record.TryGetField(1, out var position)
                    || (position.Kind != 21)
                    || (position.Count != 1)
                    || (position.Payload.Length != 24))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        null,
                        "The RootPosition field does not match the selected schema.");
                    return;
                }

                var offset = new Vector3(
                    EbinPrimitives.Single(position.Payload[12..]),
                    EbinPrimitives.Single(position.Payload[16..]),
                    EbinPrimitives.Single(position.Payload[20..]));

                if (!offset.IsFinite().All())
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        RootPositionComponent.Field1FieldPath,
                        "The RootPosition offset must be finite.");
                    return;
                }

                var positionValue = new SpatialPosition3(
                    new ChunkIndex3(
                        BinaryPrimitives.ReadInt32LittleEndian(position.Payload),
                        BinaryPrimitives.ReadInt32LittleEndian(position.Payload[4..]),
                        BinaryPrimitives.ReadInt32LittleEndian(position.Payload[8..])),
                    offset);
                var author = new RootPositionAuthor(positionValue);
                _result = _binding.Complete(in author, record.EncodedBytes.Length);
            }
        }
    }
}
