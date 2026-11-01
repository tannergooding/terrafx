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
    internal static WorldRegistrationBuilder RegisterDirectionalLight(
        WorldRegistrationBuilder builder,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schemas);

        var assignment = new ContentSchemaIdentityAssignment("render/directional-light");
        var schemaReference = new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var componentLayout = new ComponentLayout(
            new ContentSchemaIdentityAssignment("render/directional-light/layout/v1").SchemaId,
            Unsafe.SizeOf<DirectionalLightComponent>());
        var schema = ComponentSchema<DirectionalLightComponent>.FromRegistry(
            schemas,
            schemaReference,
            componentLayout);
        var colorPath = DirectionalLightComponent.Field1StoragePath;
        var illuminancePath = DirectionalLightComponent.Field2StoragePath;
        var directionPath = DirectionalLightComponent.Field3StoragePath;
        var layerMaskPath = DirectionalLightComponent.Field4StoragePath;
        var enabledPath = DirectionalLightComponent.Field5StoragePath;
        var priorityPath = DirectionalLightComponent.Field6StoragePath;
        var color = ComponentFieldBinding<DirectionalLightComponent, DirectionalLightAuthor>
            .Scalar<Vector3, DirectionalLightComponent.Field1Accessor, DirectionalLightComponent.Field1Reader>(
                schema.GetField(DirectionalLightComponent.Field1FieldPath),
                [colorPath],
                default,
                default);
        var illuminance = ComponentFieldBinding<DirectionalLightComponent, DirectionalLightAuthor>
            .Scalar<float, DirectionalLightComponent.Field2Accessor, DirectionalLightComponent.Field2Reader>(
                schema.GetField(DirectionalLightComponent.Field2FieldPath),
                [illuminancePath],
                default,
                default);
        var direction = ComponentFieldBinding<DirectionalLightComponent, DirectionalLightAuthor>
            .Scalar<Vector3, DirectionalLightComponent.Field3Accessor, DirectionalLightComponent.Field3Reader>(
                schema.GetField(DirectionalLightComponent.Field3FieldPath),
                [directionPath],
                default,
                default);
        var layerMask = ComponentFieldBinding<DirectionalLightComponent, DirectionalLightAuthor>
            .Scalar<ulong, DirectionalLightComponent.Field4Accessor, DirectionalLightComponent.Field4Reader>(
                schema.GetField(DirectionalLightComponent.Field4FieldPath),
                [layerMaskPath],
                default,
                default);
        var enabled = ComponentFieldBinding<DirectionalLightComponent, DirectionalLightAuthor>
            .Scalar<bool, DirectionalLightComponent.Field5Accessor, DirectionalLightComponent.Field5Reader>(
                schema.GetField(DirectionalLightComponent.Field5FieldPath),
                [enabledPath],
                default,
                default);
        var priority = ComponentFieldBinding<DirectionalLightComponent, DirectionalLightAuthor>
            .Scalar<int, DirectionalLightComponent.Field6Accessor, DirectionalLightComponent.Field6Reader>(
                schema.GetField(DirectionalLightComponent.Field6FieldPath),
                [priorityPath],
                default,
                default);
        var colorBytes = Unsafe.SizeOf<Vector3>();
        var directionBytes = Unsafe.SizeOf<Vector3>();
        var enabledBytes = Unsafe.SizeOf<bool>();
        var illuminanceBytes = Unsafe.SizeOf<float>();
        var layerMaskBytes = Unsafe.SizeOf<ulong>();
        var priorityBytes = Unsafe.SizeOf<int>();
        var logicalBytes = checked(
            colorBytes
            + directionBytes
            + enabledBytes
            + illuminanceBytes
            + layerMaskBytes
            + priorityBytes);

        if (componentLayout.LogicalBytes != logicalBytes)
        {
            throw new InvalidOperationException("The DirectionalLight fields do not exactly cover their declared layout.");
        }

        var storage = new ComponentStorageDeclaration(
            logicalBytes,
            [
                new ComponentStorageLeaf(
                    colorPath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<DirectionalLightComponent>("_color").ToInt32(),
                    colorBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(
                    directionPath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<DirectionalLightComponent>("_direction").ToInt32(),
                    directionBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(
                    enabledPath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<DirectionalLightComponent>("_enabled").ToInt32(),
                    enabledBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(
                    illuminancePath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<DirectionalLightComponent>("_illuminance").ToInt32(),
                    illuminanceBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(
                    layerMaskPath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<DirectionalLightComponent>("_layerMask").ToInt32(),
                    layerMaskBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(
                    priorityPath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<DirectionalLightComponent>("_priority").ToInt32(),
                    priorityBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var factory = new DirectionalLightFactory(
            ComponentFactoryBinding<DirectionalLightComponent>.Create(
                new ContentSchemaIdentityAssignment("render/directional-light/factory/v1").SchemaId,
                1,
                schema,
                storage,
                ComponentTransientRestore.None,
                [],
                [color, illuminance, direction, layerMask, enabled, priority],
                new DirectionalLightDefaultProvider(),
                new DirectionalLightRestoreProvider(),
                new DirectionalLightStateCodec(schemaReference)));

        return builder.RegisterComponent(schema, factory);
    }

    private static Diagnostic CreateRenderLightValidationDiagnostic(string code, string field, string message)
        => new(
            code,
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

    internal readonly record struct DirectionalLightAuthor(
        Vector3 Color,
        float Illuminance,
        Vector3 Direction,
        ulong LayerMask,
        bool Enabled,
        int Priority);

    [GenerateComponentBindings(typeof(DirectionalLightAuthor))]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal partial struct DirectionalLightComponent
    {
        [GenerateComponentFieldAccessor(1, nameof(DirectionalLightAuthor.Color), "Color")]
        private Vector3 _color;

        [GenerateComponentFieldAccessor(3, nameof(DirectionalLightAuthor.Direction), "Direction")]
        private Vector3 _direction;

        [GenerateComponentFieldAccessor(5, nameof(DirectionalLightAuthor.Enabled), "Enabled")]
        [MarshalAs(UnmanagedType.U1)]
        private bool _enabled;

        [GenerateComponentFieldAccessor(2, nameof(DirectionalLightAuthor.Illuminance), "Illuminance")]
        private float _illuminance;

        [GenerateComponentFieldAccessor(4, nameof(DirectionalLightAuthor.LayerMask), "LayerMask")]
        private ulong _layerMask;

        [GenerateComponentFieldAccessor(6, nameof(DirectionalLightAuthor.Priority), "Priority")]
        private int _priority;

        internal DirectionalLightComponent(
            Vector3 color,
            float illuminance,
            Vector3 direction,
            ulong layerMask,
            bool enabled,
            int priority)
        {
            _color = color;
            _direction = direction;
            _enabled = enabled;
            _illuminance = illuminance;
            _layerMask = layerMask;
            _priority = priority;
        }

        internal readonly Vector3 Color => _color;

        internal readonly Vector3 Direction => _direction;

        internal readonly bool Enabled => _enabled;

        internal readonly float Illuminance => _illuminance;

        internal readonly ulong LayerMask => _layerMask;

        internal readonly int Priority => _priority;
    }

    private sealed class DirectionalLightDefaultProvider : IComponentDefaultProvider<DirectionalLightComponent>
    {
        public DirectionalLightComponent Create(ComponentDefaultBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            return new DirectionalLightComponent(
                Vector3.One,
                10000.0f,
                new Vector3(0.0f, -1.0f, 0.0f),
                ulong.MaxValue,
                enabled: false,
                priority: 0);
        }
    }

    private sealed class DirectionalLightFactory : IComponentFactory<DirectionalLightComponent>
    {
        private readonly ComponentFactoryBinding<DirectionalLightComponent> _binding;

        internal DirectionalLightFactory(ComponentFactoryBinding<DirectionalLightComponent> binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            _binding = binding;
        }

        public ComponentFactoryBinding<DirectionalLightComponent> Binding => _binding;
    }

    private sealed class DirectionalLightRestoreProvider : IComponentRestoreProvider<DirectionalLightComponent, DirectionalLightAuthor>
    {
        public DirectionalLightComponent RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => default;

        public DirectionalLightComponent Restore(
            in DirectionalLightAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new DirectionalLightComponent(
                author.Color,
                author.Illuminance,
                author.Direction,
                author.LayerMask,
                author.Enabled,
                author.Priority);
        }
    }

    private sealed class DirectionalLightStateCodec : IComponentStateCodec<DirectionalLightComponent, DirectionalLightAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal DirectionalLightStateCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        private static byte[] CreateBytes(ContentSchemaReference schema, in DirectionalLightComponent value)
        {
            var status = EbinCodec.Prepare(CreateRecord(schema, in value), out var bytes, out var diagnostic);

            if (status is not EbinStatus.Complete)
            {
                throw new InvalidOperationException($"The DirectionalLight record could not be encoded: {diagnostic.Category}.");
            }

            return bytes;
        }

        private static EbinRecord CreateRecord(ContentSchemaReference schema, in DirectionalLightComponent value)
            => new(
                schema.SchemaId,
                checked((uint)schema.Major),
                checked((uint)schema.Minor),
                [
                    EbinPrimitives.Geometry(1, value.Color),
                    EbinPrimitives.Binary32(2, value.Illuminance),
                    EbinPrimitives.Geometry(3, value.Direction),
                    EbinPrimitives.UInt64(4, value.LayerMask),
                    EbinPrimitives.Boolean(5, value.Enabled),
                    EbinPrimitives.Int32(6, value.Priority),
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

        private static bool TryGetInvalidField(in DirectionalLightComponent value, out string field, out string message)
        {
            if (!IsValidColor(value.Color))
            {
                field = "Color";
                message = "Directional-light linear color must be finite and nonnegative.";
                return true;
            }

            if (!float.IsFinite(value.Illuminance) || (value.Illuminance < 0.0f))
            {
                field = "Illuminance";
                message = "Directional-light illuminance must be finite and nonnegative.";
                return true;
            }

            if (!IsValidDirection(value.Direction))
            {
                field = "Direction";
                message = "Directional-light direction must be finite and nonzero.";
                return true;
            }

            field = string.Empty;
            message = string.Empty;
            return false;
        }

        public nint Count(
            in DirectionalLightComponent value,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateBytes(_schema, in value).Length;
        }

        public ComponentReadResult<DirectionalLightAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<DirectionalLightAuthor> binding,
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
                ?? throw new InvalidOperationException("The DirectionalLight read did not issue an author result.");
        }

        public ImmutableArray<Diagnostic> Validate(
            in DirectionalLightComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryGetInvalidField(in value, out var field, out var message))
            {
                return [CreateRenderLightValidationDiagnostic("RenderDirectionalLightInvalid", field, message)];
            }

            return binding.ValidateFields(in value);
        }

        public nint Write(
            in DirectionalLightComponent value,
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
            private readonly ComponentReadBinding<DirectionalLightAuthor> _binding;
            private readonly ContentSchemaReference _schema;
            private ComponentReadResult<DirectionalLightAuthor>? _result;

            internal ReadAction(
                ContentSchemaReference schema,
                ComponentReadBinding<DirectionalLightAuthor> binding)
            {
                _schema = schema;
                _binding = binding;
                _result = null;
            }

            internal readonly ComponentReadResult<DirectionalLightAuthor>? Result => _result;

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
                        "The DirectionalLight record does not match the selected schema tuple.");
                    return;
                }

                if ((record.FieldCount != 6)
                    || !record.TryGetField(1, out var color)
                    || !record.TryGetField(2, out var illuminance)
                    || !record.TryGetField(3, out var direction)
                    || !record.TryGetField(4, out var layerMask)
                    || !record.TryGetField(5, out var enabled)
                    || !record.TryGetField(6, out var priority)
                    || !IsField(color, 15, 12)
                    || !IsField(illuminance, 10, 4)
                    || !IsField(direction, 15, 12)
                    || !IsField(layerMask, 4, 8)
                    || !IsField(enabled, 9, 1)
                    || !IsField(priority, 7, 4))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        null,
                        "The DirectionalLight fields do not match the selected schema.");
                    return;
                }

                var author = new DirectionalLightAuthor(
                    EbinPrimitives.Vector3(color.Payload),
                    EbinPrimitives.Single(illuminance.Payload),
                    EbinPrimitives.Vector3(direction.Payload),
                    BinaryPrimitives.ReadUInt64LittleEndian(layerMask.Payload),
                    enabled.Payload[0] != 0,
                    BinaryPrimitives.ReadInt32LittleEndian(priority.Payload));
                _result = _binding.Complete(in author, record.EncodedBytes.Length);
            }
        }
    }
}
