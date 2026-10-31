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
    internal static WorldRegistrationBuilder RegisterCamera(
        WorldRegistrationBuilder builder,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schemas);

        var assignment = new ContentSchemaIdentityAssignment("scene/camera");
        var schemaReference = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var componentLayout = new ComponentLayout(
            new ContentSchemaIdentityAssignment("scene/camera/layout/v1").SchemaId,
            Unsafe.SizeOf<CameraComponent>());
        var schema = ComponentSchema<CameraComponent>.FromRegistry(schemas, schemaReference, componentLayout);
        var field1Path = CameraComponent.Field1StoragePath;
        var field2Path = CameraComponent.Field2StoragePath;
        var field3Path = CameraComponent.Field3StoragePath;
        var field4Path = CameraComponent.Field4StoragePath;
        var field5Path = CameraComponent.Field5StoragePath;
        var field6Path = CameraComponent.Field6StoragePath;
        var field7Path = CameraComponent.Field7StoragePath;
        var field8Path = CameraComponent.Field8StoragePath;
        var field9Path = CameraComponent.Field9StoragePath;
        var field10Path = CameraComponent.Field10StoragePath;
        var field11Path = CameraComponent.Field11StoragePath;
        var field12Path = CameraComponent.Field12StoragePath;
        var field1 = ComponentFieldBinding<CameraComponent, CameraAuthor>
            .Scalar<byte, CameraComponent.Field1Accessor, CameraComponent.Field1Reader>(
                schema.GetField(CameraComponent.Field1FieldPath),
                [field1Path],
                default,
                default);
        var field2 = ComponentFieldBinding<CameraComponent, CameraAuthor>
            .Scalar<float, CameraComponent.Field2Accessor, CameraComponent.Field2Reader>(
                schema.GetField(CameraComponent.Field2FieldPath),
                [field2Path],
                default,
                default);
        var field3 = ComponentFieldBinding<CameraComponent, CameraAuthor>
            .Scalar<float, CameraComponent.Field3Accessor, CameraComponent.Field3Reader>(
                schema.GetField(CameraComponent.Field3FieldPath),
                [field3Path],
                default,
                default);
        var field4 = ComponentFieldBinding<CameraComponent, CameraAuthor>
            .Scalar<float, CameraComponent.Field4Accessor, CameraComponent.Field4Reader>(
                schema.GetField(CameraComponent.Field4FieldPath),
                [field4Path],
                default,
                default);
        var field5 = ComponentFieldBinding<CameraComponent, CameraAuthor>
            .Scalar<byte, CameraComponent.Field5Accessor, CameraComponent.Field5Reader>(
                schema.GetField(CameraComponent.Field5FieldPath),
                [field5Path],
                default,
                default);
        var field6 = ComponentFieldBinding<CameraComponent, CameraAuthor>
            .Optional<float, CameraFarAccessor, CameraFarAuthorReader>(
                schema.GetField(CameraComponent.Field6FieldPath),
                [field6Path],
                default,
                default);
        var field7 = ComponentFieldBinding<CameraComponent, CameraAuthor>
            .Optional<float, CameraAspectRatioAccessor, CameraAspectRatioAuthorReader>(
                schema.GetField(CameraComponent.Field7FieldPath),
                [field7Path],
                default,
                default);
        var field8 = ComponentFieldBinding<CameraComponent, CameraAuthor>
            .Scalar<Vector4, CameraComponent.Field8Accessor, CameraComponent.Field8Reader>(
                schema.GetField(CameraComponent.Field8FieldPath),
                [field8Path],
                default,
                default);
        var field9 = ComponentFieldBinding<CameraComponent, CameraAuthor>
            .Scalar<byte, CameraComponent.Field9Accessor, CameraComponent.Field9Reader>(
                schema.GetField(CameraComponent.Field9FieldPath),
                [field9Path],
                default,
                default);
        var field10 = ComponentFieldBinding<CameraComponent, CameraAuthor>
            .Scalar<Vector4, CameraComponent.Field10Accessor, CameraComponent.Field10Reader>(
                schema.GetField(CameraComponent.Field10FieldPath),
                [field10Path],
                default,
                default);
        var field11 = ComponentFieldBinding<CameraComponent, CameraAuthor>
            .Scalar<ulong, CameraComponent.Field11Accessor, CameraComponent.Field11Reader>(
                schema.GetField(CameraComponent.Field11FieldPath),
                [field11Path],
                default,
                default);
        var field12 = ComponentFieldBinding<CameraComponent, CameraAuthor>
            .Scalar<int, CameraComponent.Field12Accessor, CameraComponent.Field12Reader>(
                schema.GetField(CameraComponent.Field12FieldPath),
                [field12Path],
                default,
                default);
        var fieldSizes = new[] {
            Unsafe.SizeOf<byte>(),
            Unsafe.SizeOf<float>(),
            Unsafe.SizeOf<float>(),
            Unsafe.SizeOf<float>(),
            Unsafe.SizeOf<byte>(),
            Unsafe.SizeOf<OptionalSingle>(),
            Unsafe.SizeOf<OptionalSingle>(),
            Unsafe.SizeOf<Vector4>(),
            Unsafe.SizeOf<byte>(),
            Unsafe.SizeOf<Vector4>(),
            Unsafe.SizeOf<ulong>(),
            Unsafe.SizeOf<int>(),
        };
        var fieldOffsets = new[] {
            Marshal.OffsetOf<CameraComponent>("_projectionKind").ToInt32(),
            Marshal.OffsetOf<CameraComponent>("_verticalFieldOfView").ToInt32(),
            Marshal.OffsetOf<CameraComponent>("_orthographicHeight").ToInt32(),
            Marshal.OffsetOf<CameraComponent>("_near").ToInt32(),
            Marshal.OffsetOf<CameraComponent>("_farMode").ToInt32(),
            Marshal.OffsetOf<CameraComponent>("_far").ToInt32(),
            Marshal.OffsetOf<CameraComponent>("_aspectRatioOverride").ToInt32(),
            Marshal.OffsetOf<CameraComponent>("_viewport").ToInt32(),
            Marshal.OffsetOf<CameraComponent>("_clearFlags").ToInt32(),
            Marshal.OffsetOf<CameraComponent>("_clearColor").ToInt32(),
            Marshal.OffsetOf<CameraComponent>("_layerMask").ToInt32(),
            Marshal.OffsetOf<CameraComponent>("_order").ToInt32(),
        };
        var logicalBytes = 0;

        foreach (var fieldSize in fieldSizes)
        {
            logicalBytes = checked(logicalBytes + fieldSize);
        }

        if (componentLayout.LogicalBytes != logicalBytes)
        {
            throw new InvalidOperationException("The Camera component fields do not exactly cover their declared layout.");
        }

        var storage = new ComponentStorageDeclaration(
            logicalBytes,
            [
                new ComponentStorageLeaf(field7Path, ComponentStorageKind.OptionalValue, fieldOffsets[6], fieldSizes[6], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(field10Path, ComponentStorageKind.Value, fieldOffsets[9], fieldSizes[9], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(field9Path, ComponentStorageKind.Value, fieldOffsets[8], fieldSizes[8], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(field6Path, ComponentStorageKind.OptionalValue, fieldOffsets[5], fieldSizes[5], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(field5Path, ComponentStorageKind.Value, fieldOffsets[4], fieldSizes[4], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(field11Path, ComponentStorageKind.Value, fieldOffsets[10], fieldSizes[10], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(field4Path, ComponentStorageKind.Value, fieldOffsets[3], fieldSizes[3], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(field12Path, ComponentStorageKind.Value, fieldOffsets[11], fieldSizes[11], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(field3Path, ComponentStorageKind.Value, fieldOffsets[2], fieldSizes[2], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(field1Path, ComponentStorageKind.Value, fieldOffsets[0], fieldSizes[0], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(field2Path, ComponentStorageKind.Value, fieldOffsets[1], fieldSizes[1], 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(field8Path, ComponentStorageKind.Value, fieldOffsets[7], fieldSizes[7], 0, 0, ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var factory = new CameraFactory(
            ComponentFactoryBinding<CameraComponent>.Create(
                new ContentSchemaIdentityAssignment("scene/camera/factory/v1").SchemaId,
                1,
                schema,
                storage,
                ComponentTransientRestore.None,
                [],
                [field1, field2, field3, field4, field5, field6, field7, field8, field9, field10, field11, field12],
                new CameraDefaultProvider(),
                new CameraRestoreProvider(),
                new CameraStateCodec(schemaReference)));

        return builder.RegisterComponent(schema, factory);
    }

    private static float ConvertCameraMeters(long numerator, long denominator, SpatialSettings settings)
        => SpatialMath.ConvertQuantity(numerator, denominator, 0, settings.MetersPerUnitExponent, 1);

    private static Diagnostic CreateCameraValidationDiagnostic(string field, string message)
        => new(
            "SceneCameraInvalid",
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

    internal readonly record struct CameraAuthor(
        byte ProjectionKind,
        float VerticalFieldOfView,
        float OrthographicHeight,
        float Near,
        byte FarMode,
        OptionalSingle Far,
        OptionalSingle AspectRatioOverride,
        Vector4 Viewport,
        byte ClearFlags,
        Vector4 ClearColor,
        ulong LayerMask,
        int Order);

    [GenerateComponentBindings(typeof(CameraAuthor))]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal partial struct CameraComponent
    {
        [GenerateComponentFieldAccessor(7, nameof(CameraAuthor.AspectRatioOverride), "AspectRatioOverride")]
        private OptionalSingle _aspectRatioOverride;

        [GenerateComponentFieldAccessor(10, nameof(CameraAuthor.ClearColor), "ClearColor")]
        private Vector4 _clearColor;

        [GenerateComponentFieldAccessor(9, nameof(CameraAuthor.ClearFlags), "ClearFlags")]
        private byte _clearFlags;

        [GenerateComponentFieldAccessor(6, nameof(CameraAuthor.Far), "Far")]
        private OptionalSingle _far;

        [GenerateComponentFieldAccessor(5, nameof(CameraAuthor.FarMode), "FarMode")]
        private byte _farMode;

        [GenerateComponentFieldAccessor(11, nameof(CameraAuthor.LayerMask), "LayerMask")]
        private ulong _layerMask;

        [GenerateComponentFieldAccessor(4, nameof(CameraAuthor.Near), "Near")]
        private float _near;

        [GenerateComponentFieldAccessor(12, nameof(CameraAuthor.Order), "Order")]
        private int _order;

        [GenerateComponentFieldAccessor(3, nameof(CameraAuthor.OrthographicHeight), "OrthographicHeight")]
        private float _orthographicHeight;

        [GenerateComponentFieldAccessor(1, nameof(CameraAuthor.ProjectionKind), "ProjectionKind")]
        private byte _projectionKind;

        [GenerateComponentFieldAccessor(2, nameof(CameraAuthor.VerticalFieldOfView), "VerticalFieldOfView")]
        private float _verticalFieldOfView;

        [GenerateComponentFieldAccessor(8, nameof(CameraAuthor.Viewport), "Viewport")]
        private Vector4 _viewport;

        internal CameraComponent(
            byte projectionKind,
            float verticalFieldOfView,
            float orthographicHeight,
            float near,
            byte farMode,
            OptionalSingle far,
            OptionalSingle aspectRatioOverride,
            Vector4 viewport,
            byte clearFlags,
            Vector4 clearColor,
            ulong layerMask,
            int order)
        {
            _projectionKind = projectionKind;
            _verticalFieldOfView = verticalFieldOfView;
            _orthographicHeight = orthographicHeight;
            _near = near;
            _farMode = farMode;
            _far = far;
            _aspectRatioOverride = aspectRatioOverride;
            _viewport = viewport;
            _clearFlags = clearFlags;
            _clearColor = clearColor;
            _layerMask = layerMask;
            _order = order;
        }

        internal readonly OptionalSingle AspectRatioOverride => _aspectRatioOverride;

        internal OptionalSingle AspectRatioStorage
        {
            readonly get
            {
                return _aspectRatioOverride;
            }

            set
            {
                _aspectRatioOverride = value;
            }
        }

        internal readonly Vector4 ClearColor => _clearColor;

        internal readonly byte ClearFlags => _clearFlags;

        internal readonly OptionalSingle Far => _far;

        internal readonly byte FarMode => _farMode;

        internal OptionalSingle FarStorage
        {
            readonly get
            {
                return _far;
            }

            set
            {
                _far = value;
            }
        }

        internal readonly ulong LayerMask => _layerMask;

        internal readonly float Near => _near;

        internal readonly int Order => _order;

        internal readonly float OrthographicHeight => _orthographicHeight;

        internal readonly byte ProjectionKind => _projectionKind;

        internal readonly float VerticalFieldOfView => _verticalFieldOfView;

        internal readonly Vector4 Viewport => _viewport;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct OptionalSingle
    {
        private readonly byte _hasValue;
        private readonly float _value;

        internal OptionalSingle(float value)
        {
            _hasValue = 1;
            _value = value;
        }

        internal bool HasValue => _hasValue != 0;

        internal float Value => _value;
    }

    private readonly struct CameraAspectRatioAccessor : IComponentOptionalAccessor<CameraComponent, float>
    {
        public void Clear(ref CameraComponent value) => value.AspectRatioStorage = default;

        public float GetPresent(in CameraComponent value) => value.AspectRatioOverride.Value;

        public bool IsPresent(in CameraComponent value) => value.AspectRatioOverride.HasValue;

        public void SetPresent(ref CameraComponent value, in float fieldValue)
            => value.AspectRatioStorage = new OptionalSingle(fieldValue);
    }

    private readonly struct CameraAspectRatioAuthorReader : IComponentOptionalReader<CameraAuthor, float>
    {
        public float GetPresent(in CameraAuthor value) => value.AspectRatioOverride.Value;

        public bool IsPresent(in CameraAuthor value) => value.AspectRatioOverride.HasValue;
    }

    private sealed class CameraDefaultProvider : IComponentDefaultProvider<CameraComponent>
    {
        public CameraComponent Create(ComponentDefaultBinding binding)
        {
            var settings = binding.SpatialSettings;
            return new CameraComponent(
                (byte)CameraProjectionKind.Perspective,
                float.Pi / 3.0f,
                ConvertCameraMeters(10, 1, settings),
                ConvertCameraMeters(1, 10, settings),
                (byte)CameraFarMode.Finite,
                new OptionalSingle(ConvertCameraMeters(1000, 1, settings)),
                default,
                new Vector4(0, 0, 1, 1),
                (byte)(CameraClearFlags.Color | CameraClearFlags.Depth),
                new Vector4(0, 0, 0, 1),
                ulong.MaxValue,
                0);
        }
    }

    private sealed class CameraFactory : IComponentFactory<CameraComponent>
    {
        private readonly ComponentFactoryBinding<CameraComponent> _binding;

        internal CameraFactory(ComponentFactoryBinding<CameraComponent> binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            _binding = binding;
        }

        public ComponentFactoryBinding<CameraComponent> Binding => _binding;
    }

    private readonly struct CameraFarAccessor : IComponentOptionalAccessor<CameraComponent, float>
    {
        public void Clear(ref CameraComponent value) => value.FarStorage = default;

        public float GetPresent(in CameraComponent value) => value.Far.Value;

        public bool IsPresent(in CameraComponent value) => value.Far.HasValue;

        public void SetPresent(ref CameraComponent value, in float fieldValue)
            => value.FarStorage = new OptionalSingle(fieldValue);
    }

    private readonly struct CameraFarAuthorReader : IComponentOptionalReader<CameraAuthor, float>
    {
        public float GetPresent(in CameraAuthor value) => value.Far.Value;

        public bool IsPresent(in CameraAuthor value) => value.Far.HasValue;
    }

    private sealed class CameraRestoreProvider : IComponentRestoreProvider<CameraComponent, CameraAuthor>
    {
        public CameraComponent RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => default;

        public CameraComponent Restore(
            in CameraAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new CameraComponent(
                author.ProjectionKind,
                author.VerticalFieldOfView,
                author.OrthographicHeight,
                author.Near,
                author.FarMode,
                author.Far,
                author.AspectRatioOverride,
                author.Viewport,
                author.ClearFlags,
                author.ClearColor,
                author.LayerMask,
                author.Order);
        }
    }

    private sealed class CameraStateCodec : IComponentStateCodec<CameraComponent, CameraAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal CameraStateCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        private static byte[] CreateBytes(ContentSchemaReference schema, in CameraComponent value)
        {
            var status = EbinCodec.Prepare(CreateRecord(schema, in value), out var bytes, out var diagnostic);

            if (status != EbinStatus.Complete)
            {
                throw new InvalidOperationException($"The Camera record could not be encoded: {diagnostic.Category}.");
            }

            return bytes;
        }

        private static EbinRecord CreateRecord(ContentSchemaReference schema, in CameraComponent value)
        {
            var fields = new EbinField[10 + (value.Far.HasValue ? 1 : 0) + (value.AspectRatioOverride.HasValue ? 1 : 0)];
            var index = 0;
            fields[index++] = EbinPrimitives.UInt8(1, value.ProjectionKind);
            fields[index++] = EbinPrimitives.Binary32(2, value.VerticalFieldOfView);
            fields[index++] = EbinPrimitives.Binary32(3, value.OrthographicHeight);
            fields[index++] = EbinPrimitives.Binary32(4, value.Near);
            fields[index++] = EbinPrimitives.UInt8(5, value.FarMode);

            if (value.Far.HasValue)
            {
                fields[index++] = EbinPrimitives.Binary32(6, value.Far.Value);
            }

            if (value.AspectRatioOverride.HasValue)
            {
                fields[index++] = EbinPrimitives.Binary32(7, value.AspectRatioOverride.Value);
            }

            fields[index++] = EbinPrimitives.Geometry(8, value.Viewport);
            fields[index++] = EbinPrimitives.UInt8(9, value.ClearFlags);
            fields[index++] = EbinPrimitives.Geometry(10, value.ClearColor);
            fields[index++] = EbinPrimitives.UInt64(11, value.LayerMask);
            fields[index++] = EbinPrimitives.Int32(12, value.Order);

            if (index != fields.Length)
            {
                throw new InvalidOperationException("The Camera field count did not match its optional values.");
            }

            return new EbinRecord(
                schema.SchemaId,
                checked((uint)schema.Major),
                checked((uint)schema.Minor),
                fields);
        }

        private static bool IsField(EbinFieldReadView field, ushort kind, int payloadBytes)
            => (field.Kind == kind) && (field.Count == 1) && (field.Payload.Length == payloadBytes);

        private static bool TryGetInvalidField(in CameraComponent value, out string field, out string message)
        {
            if (!Enum.IsDefined((CameraProjectionKind)value.ProjectionKind))
            {
                field = "ProjectionKind";
                message = "Camera projection kind is not defined.";
                return true;
            }

            if (!float.IsFinite(value.VerticalFieldOfView)
                || (value.VerticalFieldOfView <= 0.0f)
                || (value.VerticalFieldOfView >= float.Pi))
            {
                field = "VerticalFieldOfView";
                message = "Camera field of view must be finite and in (0, pi).";
                return true;
            }

            if (!float.IsFinite(value.OrthographicHeight) || (value.OrthographicHeight <= 0.0f))
            {
                field = "OrthographicHeight";
                message = "Camera orthographic height must be finite and positive.";
                return true;
            }

            if (!float.IsFinite(value.Near) || (value.Near <= 0.0f))
            {
                field = "Near";
                message = "Camera near distance must be finite and positive.";
                return true;
            }

            if (!Enum.IsDefined((CameraFarMode)value.FarMode))
            {
                field = "FarMode";
                message = "Camera far mode is not defined.";
                return true;
            }

            if ((CameraFarMode)value.FarMode == CameraFarMode.Finite)
            {
                if (!value.Far.HasValue || !float.IsFinite(value.Far.Value) || (value.Far.Value <= value.Near))
                {
                    field = "Far";
                    message = "A finite camera far distance must exceed its near distance.";
                    return true;
                }
            }
            else if (((CameraProjectionKind)value.ProjectionKind != CameraProjectionKind.Perspective)
                || value.Far.HasValue)
            {
                field = "FarMode";
                message = "Only perspective cameras admit an absent far distance with an infinite far mode.";
                return true;
            }

            if (value.AspectRatioOverride.HasValue
                && (!float.IsFinite(value.AspectRatioOverride.Value) || (value.AspectRatioOverride.Value <= 0.0f)))
            {
                field = "AspectRatioOverride";
                message = "Camera aspect-ratio override must be finite and positive.";
                return true;
            }

            var viewport = value.Viewport;

            if (!viewport.IsFinite().All()
                || (viewport.X < 0.0f)
                || (viewport.Y < 0.0f)
                || (viewport.Z <= 0.0f)
                || (viewport.W <= 0.0f)
                || ((double)viewport.X + viewport.Z > 1.0)
                || ((double)viewport.Y + viewport.W > 1.0))
            {
                field = "Viewport";
                message = "Camera viewport must be finite, positive in extent, and contained in the normalized target.";
                return true;
            }

            if ((value.ClearFlags & ~(byte)(CameraClearFlags.Color | CameraClearFlags.Depth)) != 0)
            {
                field = "ClearFlags";
                message = "Camera clear flags contain an undefined bit.";
                return true;
            }

            if (!value.ClearColor.IsFinite().All())
            {
                field = "ClearColor";
                message = "Camera clear color must be finite.";
                return true;
            }

            field = string.Empty;
            message = string.Empty;
            return false;
        }

        public nint Count(
            in CameraComponent value,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateBytes(_schema, in value).Length;
        }

        public ComponentReadResult<CameraAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<CameraAuthor> binding,
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
                ?? throw new InvalidOperationException("The Camera read did not issue an author result.");
        }

        public ImmutableArray<Diagnostic> Validate(
            in CameraComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetInvalidField(in value, out var field, out var message))
            {
                return [CreateCameraValidationDiagnostic(field, message)];
            }

            return binding.ValidateFields(in value);
        }

        public nint Write(
            in CameraComponent value,
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
            private readonly ComponentReadBinding<CameraAuthor> _binding;
            private readonly ContentSchemaReference _schema;
            private ComponentReadResult<CameraAuthor>? _result;

            internal ReadAction(ContentSchemaReference schema, ComponentReadBinding<CameraAuthor> binding)
            {
                _schema = schema;
                _binding = binding;
                _result = null;
            }

            internal readonly ComponentReadResult<CameraAuthor>? Result => _result;

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
                        "The Camera record does not match the selected schema tuple.");
                    return;
                }

                var hasFar = record.TryGetField(6, out var far);
                var hasAspectRatio = record.TryGetField(7, out var aspectRatio);

                if ((record.FieldCount != 10 + (hasFar ? 1 : 0) + (hasAspectRatio ? 1 : 0))
                    || !record.TryGetField(1, out var projectionKind)
                    || !record.TryGetField(2, out var verticalFieldOfView)
                    || !record.TryGetField(3, out var orthographicHeight)
                    || !record.TryGetField(4, out var near)
                    || !record.TryGetField(5, out var farMode)
                    || !record.TryGetField(8, out var viewport)
                    || !record.TryGetField(9, out var clearFlags)
                    || !record.TryGetField(10, out var clearColor)
                    || !record.TryGetField(11, out var layerMask)
                    || !record.TryGetField(12, out var order)
                    || !IsField(projectionKind, 1, 1)
                    || !IsField(verticalFieldOfView, 10, 4)
                    || !IsField(orthographicHeight, 10, 4)
                    || !IsField(near, 10, 4)
                    || !IsField(farMode, 1, 1)
                    || (hasFar && !IsField(far, 10, 4))
                    || (hasAspectRatio && !IsField(aspectRatio, 10, 4))
                    || !IsField(viewport, 16, 16)
                    || !IsField(clearFlags, 1, 1)
                    || !IsField(clearColor, 16, 16)
                    || !IsField(layerMask, 4, 8)
                    || !IsField(order, 7, 4))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        null,
                        "The Camera fields do not match the selected schema.");
                    return;
                }

                var author = new CameraAuthor(
                    projectionKind.Payload[0],
                    EbinPrimitives.Single(verticalFieldOfView.Payload),
                    EbinPrimitives.Single(orthographicHeight.Payload),
                    EbinPrimitives.Single(near.Payload),
                    farMode.Payload[0],
                    hasFar ? new OptionalSingle(EbinPrimitives.Single(far.Payload)) : default,
                    hasAspectRatio ? new OptionalSingle(EbinPrimitives.Single(aspectRatio.Payload)) : default,
                    EbinPrimitives.Vector4(viewport.Payload),
                    clearFlags.Payload[0],
                    EbinPrimitives.Vector4(clearColor.Payload),
                    BinaryPrimitives.ReadUInt64LittleEndian(layerMask.Payload),
                    BinaryPrimitives.ReadInt32LittleEndian(order.Payload));
                _result = _binding.Complete(in author, record.EncodedBytes.Length);
            }
        }
    }
}
