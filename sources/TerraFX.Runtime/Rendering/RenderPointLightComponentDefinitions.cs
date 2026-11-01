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
    internal static WorldRegistrationBuilder RegisterPointLight(
        WorldRegistrationBuilder builder,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schemas);

        var assignment = new ContentSchemaIdentityAssignment("render/point-light");
        var schemaReference = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var componentLayout = new ComponentLayout(
            new ContentSchemaIdentityAssignment("render/point-light/layout/v1").SchemaId,
            Unsafe.SizeOf<PointLightComponent>());
        var schema = ComponentSchema<PointLightComponent>.FromRegistry(schemas, schemaReference, componentLayout);
        var colorPath = PointLightComponent.Field1StoragePath;
        var fluxPath = PointLightComponent.Field2StoragePath;
        var rangePath = PointLightComponent.Field3StoragePath;
        var layerMaskPath = PointLightComponent.Field4StoragePath;
        var color = ComponentFieldBinding<PointLightComponent, PointLightAuthor>
            .Scalar<Vector3, PointLightComponent.Field1Accessor, PointLightComponent.Field1Reader>(
                schema.GetField(PointLightComponent.Field1FieldPath),
                [colorPath],
                default,
                default);
        var flux = ComponentFieldBinding<PointLightComponent, PointLightAuthor>
            .Scalar<float, PointLightComponent.Field2Accessor, PointLightComponent.Field2Reader>(
                schema.GetField(PointLightComponent.Field2FieldPath),
                [fluxPath],
                default,
                default);
        var range = ComponentFieldBinding<PointLightComponent, PointLightAuthor>
            .Scalar<float, PointLightComponent.Field3Accessor, PointLightComponent.Field3Reader>(
                schema.GetField(PointLightComponent.Field3FieldPath),
                [rangePath],
                default,
                default);
        var layerMask = ComponentFieldBinding<PointLightComponent, PointLightAuthor>
            .Scalar<ulong, PointLightComponent.Field4Accessor, PointLightComponent.Field4Reader>(
                schema.GetField(PointLightComponent.Field4FieldPath),
                [layerMaskPath],
                default,
                default);
        var colorBytes = Unsafe.SizeOf<Vector3>();
        var fluxBytes = Unsafe.SizeOf<float>();
        var rangeBytes = Unsafe.SizeOf<float>();
        var layerMaskBytes = Unsafe.SizeOf<ulong>();
        var logicalBytes = checked(colorBytes + fluxBytes + rangeBytes + layerMaskBytes);

        if (componentLayout.LogicalBytes != logicalBytes)
        {
            throw new InvalidOperationException("The PointLight component fields do not exactly cover their declared layout.");
        }

        var storage = new ComponentStorageDeclaration(
            logicalBytes,
            [
                new ComponentStorageLeaf(
                    colorPath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<PointLightComponent>("_color").ToInt32(),
                    colorBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(
                    layerMaskPath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<PointLightComponent>("_layerMask").ToInt32(),
                    layerMaskBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(
                    fluxPath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<PointLightComponent>("_luminousFlux").ToInt32(),
                    fluxBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(
                    rangePath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<PointLightComponent>("_range").ToInt32(),
                    rangeBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var factory = new PointLightFactory(
            ComponentFactoryBinding<PointLightComponent>.Create(
                new ContentSchemaIdentityAssignment("render/point-light/factory/v1").SchemaId,
                1,
                schema,
                storage,
                ComponentTransientRestore.None,
                [],
                [color, flux, range, layerMask],
                new PointLightDefaultProvider(),
                new PointLightRestoreProvider(),
                new PointLightStateCodec(schemaReference)));

        return builder.RegisterComponent(schema, factory);
    }

    private static Diagnostic CreatePointLightValidationDiagnostic(string field, string message)
        => new(
            "RenderPointLightInvalid",
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

    internal readonly record struct PointLightAuthor(Vector3 Color, float LuminousFlux, float Range, ulong LayerMask);

    [GenerateComponentBindings(typeof(PointLightAuthor))]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal partial struct PointLightComponent
    {
        [GenerateComponentFieldAccessor(1, nameof(PointLightAuthor.Color), "Color")]
        private Vector3 _color;

        [GenerateComponentFieldAccessor(4, nameof(PointLightAuthor.LayerMask), "LayerMask")]
        private ulong _layerMask;

        [GenerateComponentFieldAccessor(2, nameof(PointLightAuthor.LuminousFlux), "LuminousFlux")]
        private float _luminousFlux;

        [GenerateComponentFieldAccessor(3, nameof(PointLightAuthor.Range), "Range")]
        private float _range;

        internal PointLightComponent(Vector3 color, float luminousFlux, float range, ulong layerMask)
        {
            _color = color;
            _luminousFlux = luminousFlux;
            _range = range;
            _layerMask = layerMask;
        }

        internal readonly Vector3 Color => _color;

        internal readonly ulong LayerMask => _layerMask;

        internal readonly float LuminousFlux => _luminousFlux;

        internal readonly float Range => _range;
    }

    private sealed class PointLightDefaultProvider : IComponentDefaultProvider<PointLightComponent>
    {
        public PointLightComponent Create(ComponentDefaultBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            var range = SpatialMath.ConvertQuantity(
                10,
                1,
                0,
                binding.SpatialSettings.MetersPerUnitExponent,
                1);
            return new PointLightComponent(Vector3.One, 1000.0f, range, ulong.MaxValue);
        }
    }

    private sealed class PointLightFactory : IComponentFactory<PointLightComponent>
    {
        private readonly ComponentFactoryBinding<PointLightComponent> _binding;

        internal PointLightFactory(ComponentFactoryBinding<PointLightComponent> binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            _binding = binding;
        }

        public ComponentFactoryBinding<PointLightComponent> Binding => _binding;
    }

    private sealed class PointLightRestoreProvider : IComponentRestoreProvider<PointLightComponent, PointLightAuthor>
    {
        public PointLightComponent RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => default;

        public PointLightComponent Restore(
            in PointLightAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PointLightComponent(author.Color, author.LuminousFlux, author.Range, author.LayerMask);
        }
    }

    private sealed class PointLightStateCodec : IComponentStateCodec<PointLightComponent, PointLightAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal PointLightStateCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        private static byte[] CreateBytes(ContentSchemaReference schema, in PointLightComponent value)
        {
            var status = EbinCodec.Prepare(CreateRecord(schema, in value), out var bytes, out var diagnostic);

            if (status is not EbinStatus.Complete)
            {
                throw new InvalidOperationException($"The PointLight record could not be encoded: {diagnostic.Category}.");
            }

            return bytes;
        }

        private static EbinRecord CreateRecord(ContentSchemaReference schema, in PointLightComponent value)
            => new(
                schema.SchemaId,
                checked((uint)schema.Major),
                checked((uint)schema.Minor),
                [
                    EbinPrimitives.Geometry(1, value.Color),
                    EbinPrimitives.Binary32(2, value.LuminousFlux),
                    EbinPrimitives.Binary32(3, value.Range),
                    EbinPrimitives.UInt64(4, value.LayerMask),
                ]);

        private static bool IsField(EbinFieldReadView field, ushort kind, int payloadBytes)
            => field.IsRequired
            && (field.Kind == kind)
            && (field.Count == 1)
            && (field.Payload.Length == payloadBytes);

        private static bool TryGetInvalidField(in PointLightComponent value, out string field, out string message)
        {
            var color = value.Color;

            if (!color.IsFinite().All() || (color.X < 0.0f) || (color.Y < 0.0f) || (color.Z < 0.0f))
            {
                field = "Color";
                message = "Point-light linear color must be finite and nonnegative.";
                return true;
            }

            if (!float.IsFinite(value.LuminousFlux) || (value.LuminousFlux < 0.0f))
            {
                field = "LuminousFlux";
                message = "Point-light luminous flux must be finite and nonnegative.";
                return true;
            }

            if (!float.IsFinite(value.Range) || (value.Range <= 0.0f))
            {
                field = "Range";
                message = "Point-light range must be finite and positive.";
                return true;
            }

            field = string.Empty;
            message = string.Empty;
            return false;
        }

        public nint Count(
            in PointLightComponent value,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateBytes(_schema, in value).Length;
        }

        public ComponentReadResult<PointLightAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<PointLightAuthor> binding,
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
                ?? throw new InvalidOperationException("The PointLight read did not issue an author result.");
        }

        public ImmutableArray<Diagnostic> Validate(
            in PointLightComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetInvalidField(in value, out var field, out var message))
            {
                return [CreatePointLightValidationDiagnostic(field, message)];
            }

            return binding.ValidateFields(in value);
        }

        public nint Write(
            in PointLightComponent value,
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
            private readonly ComponentReadBinding<PointLightAuthor> _binding;
            private readonly ContentSchemaReference _schema;
            private ComponentReadResult<PointLightAuthor>? _result;

            internal ReadAction(
                ContentSchemaReference schema,
                ComponentReadBinding<PointLightAuthor> binding)
            {
                _schema = schema;
                _binding = binding;
                _result = null;
            }

            internal readonly ComponentReadResult<PointLightAuthor>? Result => _result;

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
                        "The PointLight record does not match the selected schema tuple.");
                    return;
                }

                if ((record.FieldCount != 4)
                    || !record.TryGetField(1, out var color)
                    || !record.TryGetField(2, out var flux)
                    || !record.TryGetField(3, out var range)
                    || !record.TryGetField(4, out var layerMask)
                    || !IsField(color, 15, 12)
                    || !IsField(flux, 10, 4)
                    || !IsField(range, 10, 4)
                    || !IsField(layerMask, 4, 8))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        null,
                        "The PointLight fields do not match the selected schema.");
                    return;
                }

                var author = new PointLightAuthor(
                    EbinPrimitives.Vector3(color.Payload),
                    EbinPrimitives.Single(flux.Payload),
                    EbinPrimitives.Single(range.Payload),
                    BinaryPrimitives.ReadUInt64LittleEndian(layerMask.Payload));
                _result = _binding.Complete(in author, record.EncodedBytes.Length);
            }
        }
    }
}
