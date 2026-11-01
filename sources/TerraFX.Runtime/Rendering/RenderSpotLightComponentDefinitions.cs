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

namespace TerraFX.Rendering;

internal static partial class RenderComponentDefinitions
{
    internal static WorldRegistrationBuilder RegisterSpotLight(
        WorldRegistrationBuilder builder,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schemas);

        var assignment = new ContentSchemaIdentityAssignment("render/spot-light");
        var schemaReference = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var componentLayout = new ComponentLayout(
            new ContentSchemaIdentityAssignment("render/spot-light/layout/v1").SchemaId,
            Unsafe.SizeOf<SpotLightComponent>());
        var schema = ComponentSchema<SpotLightComponent>.FromRegistry(schemas, schemaReference, componentLayout);
        var colorPath = SpotLightComponent.Field1StoragePath;
        var fluxPath = SpotLightComponent.Field2StoragePath;
        var rangePath = SpotLightComponent.Field3StoragePath;
        var directionPath = SpotLightComponent.Field4StoragePath;
        var innerConeAnglePath = SpotLightComponent.Field5StoragePath;
        var outerConeAnglePath = SpotLightComponent.Field6StoragePath;
        var layerMaskPath = SpotLightComponent.Field7StoragePath;
        var enabledPath = SpotLightComponent.Field8StoragePath;
        var priorityPath = SpotLightComponent.Field9StoragePath;
        var color = ComponentFieldBinding<SpotLightComponent, SpotLightAuthor>
            .Scalar<Vector3, SpotLightComponent.Field1Accessor, SpotLightComponent.Field1Reader>(
                schema.GetField(SpotLightComponent.Field1FieldPath),
                [colorPath],
                default,
                default);
        var flux = ComponentFieldBinding<SpotLightComponent, SpotLightAuthor>
            .Scalar<float, SpotLightComponent.Field2Accessor, SpotLightComponent.Field2Reader>(
                schema.GetField(SpotLightComponent.Field2FieldPath),
                [fluxPath],
                default,
                default);
        var range = ComponentFieldBinding<SpotLightComponent, SpotLightAuthor>
            .Scalar<float, SpotLightComponent.Field3Accessor, SpotLightComponent.Field3Reader>(
                schema.GetField(SpotLightComponent.Field3FieldPath),
                [rangePath],
                default,
                default);
        var direction = ComponentFieldBinding<SpotLightComponent, SpotLightAuthor>
            .Scalar<Vector3, SpotLightComponent.Field4Accessor, SpotLightComponent.Field4Reader>(
                schema.GetField(SpotLightComponent.Field4FieldPath),
                [directionPath],
                default,
                default);
        var innerConeAngle = ComponentFieldBinding<SpotLightComponent, SpotLightAuthor>
            .Scalar<float, SpotLightComponent.Field5Accessor, SpotLightComponent.Field5Reader>(
                schema.GetField(SpotLightComponent.Field5FieldPath),
                [innerConeAnglePath],
                default,
                default);
        var outerConeAngle = ComponentFieldBinding<SpotLightComponent, SpotLightAuthor>
            .Scalar<float, SpotLightComponent.Field6Accessor, SpotLightComponent.Field6Reader>(
                schema.GetField(SpotLightComponent.Field6FieldPath),
                [outerConeAnglePath],
                default,
                default);
        var layerMask = ComponentFieldBinding<SpotLightComponent, SpotLightAuthor>
            .Scalar<ulong, SpotLightComponent.Field7Accessor, SpotLightComponent.Field7Reader>(
                schema.GetField(SpotLightComponent.Field7FieldPath),
                [layerMaskPath],
                default,
                default);
        var enabled = ComponentFieldBinding<SpotLightComponent, SpotLightAuthor>
            .Scalar<bool, SpotLightComponent.Field8Accessor, SpotLightComponent.Field8Reader>(
                schema.GetField(SpotLightComponent.Field8FieldPath),
                [enabledPath],
                default,
                default);
        var priority = ComponentFieldBinding<SpotLightComponent, SpotLightAuthor>
            .Scalar<int, SpotLightComponent.Field9Accessor, SpotLightComponent.Field9Reader>(
                schema.GetField(SpotLightComponent.Field9FieldPath),
                [priorityPath],
                default,
                default);
        var colorBytes = Unsafe.SizeOf<Vector3>();
        var directionBytes = Unsafe.SizeOf<Vector3>();
        var enabledBytes = Unsafe.SizeOf<bool>();
        var innerConeAngleBytes = Unsafe.SizeOf<float>();
        var layerMaskBytes = Unsafe.SizeOf<ulong>();
        var fluxBytes = Unsafe.SizeOf<float>();
        var outerConeAngleBytes = Unsafe.SizeOf<float>();
        var priorityBytes = Unsafe.SizeOf<int>();
        var rangeBytes = Unsafe.SizeOf<float>();
        var logicalBytes = checked(
            colorBytes
            + directionBytes
            + enabledBytes
            + innerConeAngleBytes
            + layerMaskBytes
            + fluxBytes
            + outerConeAngleBytes
            + priorityBytes
            + rangeBytes);

        if (componentLayout.LogicalBytes != logicalBytes)
        {
            throw new InvalidOperationException("The SpotLight fields do not exactly cover their declared layout.");
        }

        var storage = new ComponentStorageDeclaration(
            logicalBytes,
            [
                new ComponentStorageLeaf(colorPath, ComponentStorageKind.Value, Marshal.OffsetOf<SpotLightComponent>("_color").ToInt32(), colorBytes, 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(directionPath, ComponentStorageKind.Value, Marshal.OffsetOf<SpotLightComponent>("_direction").ToInt32(), directionBytes, 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(enabledPath, ComponentStorageKind.Value, Marshal.OffsetOf<SpotLightComponent>("_enabled").ToInt32(), enabledBytes, 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(innerConeAnglePath, ComponentStorageKind.Value, Marshal.OffsetOf<SpotLightComponent>("_innerConeAngle").ToInt32(), innerConeAngleBytes, 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(layerMaskPath, ComponentStorageKind.Value, Marshal.OffsetOf<SpotLightComponent>("_layerMask").ToInt32(), layerMaskBytes, 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(fluxPath, ComponentStorageKind.Value, Marshal.OffsetOf<SpotLightComponent>("_luminousFlux").ToInt32(), fluxBytes, 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(outerConeAnglePath, ComponentStorageKind.Value, Marshal.OffsetOf<SpotLightComponent>("_outerConeAngle").ToInt32(), outerConeAngleBytes, 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(priorityPath, ComponentStorageKind.Value, Marshal.OffsetOf<SpotLightComponent>("_priority").ToInt32(), priorityBytes, 0, 0, ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(rangePath, ComponentStorageKind.Value, Marshal.OffsetOf<SpotLightComponent>("_range").ToInt32(), rangeBytes, 0, 0, ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var factory = new SpotLightFactory(
            ComponentFactoryBinding<SpotLightComponent>.Create(
                new ContentSchemaIdentityAssignment("render/spot-light/factory/v1").SchemaId,
                1,
                schema,
                storage,
                ComponentTransientRestore.None,
                [],
                [color, flux, range, direction, innerConeAngle, outerConeAngle, layerMask, enabled, priority],
                new SpotLightDefaultProvider(),
                new SpotLightRestoreProvider(),
                new SpotLightStateCodec(schemaReference)));

        return builder.RegisterComponent(schema, factory);
    }

    internal readonly record struct SpotLightAuthor(
        Vector3 Color,
        float LuminousFlux,
        float Range,
        Vector3 Direction,
        float InnerConeAngle,
        float OuterConeAngle,
        ulong LayerMask,
        bool Enabled,
        int Priority);

    [GenerateComponentBindings(typeof(SpotLightAuthor))]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal partial struct SpotLightComponent
    {
        [GenerateComponentFieldAccessor(1, nameof(SpotLightAuthor.Color), "Color")]
        private Vector3 _color;

        [GenerateComponentFieldAccessor(4, nameof(SpotLightAuthor.Direction), "Direction")]
        private Vector3 _direction;

        [GenerateComponentFieldAccessor(8, nameof(SpotLightAuthor.Enabled), "Enabled")]
        [MarshalAs(UnmanagedType.U1)]
        private bool _enabled;

        [GenerateComponentFieldAccessor(5, nameof(SpotLightAuthor.InnerConeAngle), "InnerConeAngle")]
        private float _innerConeAngle;

        [GenerateComponentFieldAccessor(7, nameof(SpotLightAuthor.LayerMask), "LayerMask")]
        private ulong _layerMask;

        [GenerateComponentFieldAccessor(2, nameof(SpotLightAuthor.LuminousFlux), "LuminousFlux")]
        private float _luminousFlux;

        [GenerateComponentFieldAccessor(6, nameof(SpotLightAuthor.OuterConeAngle), "OuterConeAngle")]
        private float _outerConeAngle;

        [GenerateComponentFieldAccessor(9, nameof(SpotLightAuthor.Priority), "Priority")]
        private int _priority;

        [GenerateComponentFieldAccessor(3, nameof(SpotLightAuthor.Range), "Range")]
        private float _range;

        internal SpotLightComponent(
            Vector3 color,
            float luminousFlux,
            float range,
            Vector3 direction,
            float innerConeAngle,
            float outerConeAngle,
            ulong layerMask,
            bool enabled,
            int priority)
        {
            _color = color;
            _direction = direction;
            _enabled = enabled;
            _innerConeAngle = innerConeAngle;
            _layerMask = layerMask;
            _luminousFlux = luminousFlux;
            _outerConeAngle = outerConeAngle;
            _priority = priority;
            _range = range;
        }

        internal readonly Vector3 Color => _color;

        internal readonly Vector3 Direction => _direction;

        internal readonly bool Enabled => _enabled;

        internal readonly float InnerConeAngle => _innerConeAngle;

        internal readonly ulong LayerMask => _layerMask;

        internal readonly float LuminousFlux => _luminousFlux;

        internal readonly float OuterConeAngle => _outerConeAngle;

        internal readonly int Priority => _priority;

        internal readonly float Range => _range;
    }

    private sealed class SpotLightDefaultProvider : IComponentDefaultProvider<SpotLightComponent>
    {
        public SpotLightComponent Create(ComponentDefaultBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            var range = SpatialMath.ConvertQuantity(
                10,
                1,
                0,
                binding.SpatialSettings.MetersPerUnitExponent,
                1);
            return new SpotLightComponent(
                Vector3.One,
                1000.0f,
                range,
                new Vector3(0.0f, -1.0f, 0.0f),
                BitConverter.Int32BitsToSingle(0x3F060A92),
                BitConverter.Int32BitsToSingle(0x3F490FDB),
                ulong.MaxValue,
                enabled: false,
                priority: 0);
        }
    }

    private sealed class SpotLightFactory : IComponentFactory<SpotLightComponent>
    {
        private readonly ComponentFactoryBinding<SpotLightComponent> _binding;

        internal SpotLightFactory(ComponentFactoryBinding<SpotLightComponent> binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            _binding = binding;
        }

        public ComponentFactoryBinding<SpotLightComponent> Binding => _binding;
    }

    private sealed class SpotLightRestoreProvider : IComponentRestoreProvider<SpotLightComponent, SpotLightAuthor>
    {
        public SpotLightComponent RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => default;

        public SpotLightComponent Restore(
            in SpotLightAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new SpotLightComponent(
                author.Color,
                author.LuminousFlux,
                author.Range,
                author.Direction,
                author.InnerConeAngle,
                author.OuterConeAngle,
                author.LayerMask,
                author.Enabled,
                author.Priority);
        }
    }

    private sealed class SpotLightStateCodec : IComponentStateCodec<SpotLightComponent, SpotLightAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal SpotLightStateCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        private static byte[] CreateBytes(ContentSchemaReference schema, in SpotLightComponent value)
        {
            var status = EbinCodec.Prepare(CreateRecord(schema, in value), out var bytes, out var diagnostic);

            if (status is not EbinStatus.Complete)
            {
                throw new InvalidOperationException($"The SpotLight record could not be encoded: {diagnostic.Category}.");
            }

            return bytes;
        }

        private static EbinRecord CreateRecord(ContentSchemaReference schema, in SpotLightComponent value)
            => new(
                schema.SchemaId,
                checked((uint)schema.Major),
                checked((uint)schema.Minor),
                [
                    EbinPrimitives.Geometry(1, value.Color),
                    EbinPrimitives.Binary32(2, value.LuminousFlux),
                    EbinPrimitives.Binary32(3, value.Range),
                    EbinPrimitives.Geometry(4, value.Direction),
                    EbinPrimitives.Binary32(5, value.InnerConeAngle),
                    EbinPrimitives.Binary32(6, value.OuterConeAngle),
                    EbinPrimitives.UInt64(7, value.LayerMask),
                    EbinPrimitives.Boolean(8, value.Enabled),
                    EbinPrimitives.Int32(9, value.Priority),
                ]);

        private static bool IsField(EbinFieldReadView field, ushort kind, int payloadBytes)
            => field.IsRequired
            && (field.Kind == kind)
            && (field.Count == 1)
            && (field.Payload.Length == payloadBytes);

        private static bool IsValidColor(Vector3 value)
            => value.IsFinite().All() && (value.X >= 0.0f) && (value.Y >= 0.0f) && (value.Z >= 0.0f);

        private static bool IsValidDirection(Vector3 value)
        {
            var lengthSquared = Vector3.Dot(value, value);
            return value.IsFinite().All() && float.IsFinite(lengthSquared) && (lengthSquared > 1.0e-12f);
        }

        private static bool TryGetInvalidField(in SpotLightComponent value, out string field, out string message)
        {
            if (!IsValidColor(value.Color))
            {
                field = "Color";
                message = "Spot-light linear color must be finite and nonnegative.";
                return true;
            }

            if (!float.IsFinite(value.LuminousFlux) || (value.LuminousFlux < 0.0f))
            {
                field = "LuminousFlux";
                message = "Spot-light luminous flux must be finite and nonnegative.";
                return true;
            }

            if (!float.IsFinite(value.Range) || (value.Range <= 0.0f))
            {
                field = "Range";
                message = "Spot-light range must be finite and positive.";
                return true;
            }

            if (!IsValidDirection(value.Direction))
            {
                field = "Direction";
                message = "Spot-light direction must be finite and nonzero.";
                return true;
            }

            if (!float.IsFinite(value.InnerConeAngle)
                || !float.IsFinite(value.OuterConeAngle)
                || (value.InnerConeAngle <= 0.0f)
                || (value.InnerConeAngle > value.OuterConeAngle)
                || (value.OuterConeAngle >= (float.Pi / 2.0f)))
            {
                field = "InnerConeAngle";
                message = "Spot-light cone half angles must satisfy 0 < inner <= outer < pi/2.";
                return true;
            }

            field = string.Empty;
            message = string.Empty;
            return false;
        }

        public nint Count(
            in SpotLightComponent value,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateBytes(_schema, in value).Length;
        }

        public ComponentReadResult<SpotLightAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<SpotLightAuthor> binding,
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
                ?? throw new InvalidOperationException("The SpotLight read did not issue an author result.");
        }

        public ImmutableArray<Diagnostic> Validate(
            in SpotLightComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetInvalidField(in value, out var field, out var message))
            {
                return [CreateRenderLightValidationDiagnostic("RenderSpotLightInvalid", field, message)];
            }

            return binding.ValidateFields(in value);
        }

        public nint Write(
            in SpotLightComponent value,
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
            private readonly ComponentReadBinding<SpotLightAuthor> _binding;
            private readonly ContentSchemaReference _schema;
            private ComponentReadResult<SpotLightAuthor>? _result;

            internal ReadAction(
                ContentSchemaReference schema,
                ComponentReadBinding<SpotLightAuthor> binding)
            {
                _schema = schema;
                _binding = binding;
                _result = null;
            }

            internal readonly ComponentReadResult<SpotLightAuthor>? Result => _result;

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
                        "The SpotLight record does not match the selected schema tuple.");
                    return;
                }

                if ((record.FieldCount != 9)
                    || !record.TryGetField(1, out var color)
                    || !record.TryGetField(2, out var flux)
                    || !record.TryGetField(3, out var range)
                    || !record.TryGetField(4, out var direction)
                    || !record.TryGetField(5, out var innerConeAngle)
                    || !record.TryGetField(6, out var outerConeAngle)
                    || !record.TryGetField(7, out var layerMask)
                    || !record.TryGetField(8, out var enabled)
                    || !record.TryGetField(9, out var priority)
                    || !IsField(color, 15, 12)
                    || !IsField(flux, 10, 4)
                    || !IsField(range, 10, 4)
                    || !IsField(direction, 15, 12)
                    || !IsField(innerConeAngle, 10, 4)
                    || !IsField(outerConeAngle, 10, 4)
                    || !IsField(layerMask, 4, 8)
                    || !IsField(enabled, 9, 1)
                    || (enabled.Payload[0] > 1)
                    || !IsField(priority, 7, 4))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        null,
                        "The SpotLight fields do not match the selected schema.");
                    return;
                }

                var author = new SpotLightAuthor(
                    EbinPrimitives.Vector3(color.Payload),
                    EbinPrimitives.Single(flux.Payload),
                    EbinPrimitives.Single(range.Payload),
                    EbinPrimitives.Vector3(direction.Payload),
                    EbinPrimitives.Single(innerConeAngle.Payload),
                    EbinPrimitives.Single(outerConeAngle.Payload),
                    BinaryPrimitives.ReadUInt64LittleEndian(layerMask.Payload),
                    enabled.Payload[0] != 0,
                    BinaryPrimitives.ReadInt32LittleEndian(priority.Payload));
                _result = _binding.Complete(in author, record.EncodedBytes.Length);
            }
        }
    }
}
