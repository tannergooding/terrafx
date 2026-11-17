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

namespace TerraFX.Rendering;

internal static partial class RenderComponentDefinitions
{
    private static readonly Guid s_particleEmitterAssetSchemaId = new ContentSchemaIdentityAssignment("native/cosmetic-particle-emitter").SchemaId;
    private static readonly Guid s_particleEmitterComponentSchemaId = new ContentSchemaIdentityAssignment("render/particle-emitter").SchemaId;
    private static readonly Guid s_particleEmitterReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;

    internal static WorldRegistrationBuilder RegisterParticleEmitter(
        WorldRegistrationBuilder builder,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schemas);

        var schemaReference = new ContentSchemaReference(s_particleEmitterComponentSchemaId, 1, 0, ContentSchemaScope.Content);
        var layout = new ComponentLayout(
            new ContentSchemaIdentityAssignment("render/particle-emitter/layout/v1").SchemaId,
            Unsafe.SizeOf<ParticleEmitterComponent>());
        var schema = ComponentSchema<ParticleEmitterComponent>.FromRegistry(schemas, schemaReference, layout);
        var definitionPath = ParticleEmitterComponent.Field1StoragePath;
        var enabledPath = ParticleEmitterComponent.Field2StoragePath;
        var definition = ComponentFieldBinding<ParticleEmitterComponent, ParticleEmitterAuthor>.AssetReference<
            ParticleEmitterComponent.Field1Accessor,
            ParticleEmitterComponent.Field1Reader>(
                schema.GetField(ParticleEmitterComponent.Field1FieldPath),
                [definitionPath],
                default,
                default);
        var enabled = ComponentFieldBinding<ParticleEmitterComponent, ParticleEmitterAuthor>.Scalar<
            bool,
            ParticleEmitterComponent.Field2Accessor,
            ParticleEmitterComponent.Field2Reader>(
                schema.GetField(ParticleEmitterComponent.Field2FieldPath),
                [enabledPath],
                default,
                default);
        var definitionBytes = Unsafe.SizeOf<int>();
        var enabledBytes = Unsafe.SizeOf<bool>();
        var logicalBytes = checked(definitionBytes + enabledBytes);

        if (layout.LogicalBytes != logicalBytes)
        {
            throw new InvalidOperationException("The particle emitter fields do not exactly cover their declared layout.");
        }

        var storage = new ComponentStorageDeclaration(
            logicalBytes,
            [
                new ComponentStorageLeaf(
                    definitionPath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<ParticleEmitterComponent>("_definitionAssetId").ToInt32(),
                    definitionBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
                new ComponentStorageLeaf(
                    enabledPath,
                    ComponentStorageKind.Value,
                    Marshal.OffsetOf<ParticleEmitterComponent>("_enabled").ToInt32(),
                    enabledBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var binding = ComponentFactoryBinding<ParticleEmitterComponent>.Create(
            new ContentSchemaIdentityAssignment("render/particle-emitter/factory/v1").SchemaId,
            1,
            schema,
            storage,
            ComponentTransientRestore.None,
            [],
            [definition, enabled],
            new ParticleEmitterDefaultProvider(),
            new ParticleEmitterRestoreProvider(),
            new ParticleEmitterStateCodec(schemaReference));
        return builder.RegisterComponent(schema, new ParticleEmitterFactory(binding));
    }

    internal readonly record struct ParticleEmitterAuthor(int DefinitionAssetId, bool Enabled);

    [GenerateComponentBindings(typeof(ParticleEmitterAuthor))]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal partial struct ParticleEmitterComponent
    {
        [GenerateComponentFieldAccessor(1, nameof(ParticleEmitterAuthor.DefinitionAssetId), "DefinitionAssetId")]
        private int _definitionAssetId;

        [GenerateComponentFieldAccessor(2, nameof(ParticleEmitterAuthor.Enabled), "Enabled")]
        private bool _enabled;

        internal ParticleEmitterComponent(int definitionAssetId, bool enabled)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(definitionAssetId);
            _definitionAssetId = definitionAssetId;
            _enabled = enabled;
        }

        internal readonly int DefinitionAssetId => _definitionAssetId;

        internal readonly bool Enabled => _enabled;
    }

    private sealed class ParticleEmitterDefaultProvider : IComponentDefaultProvider<ParticleEmitterComponent>
    {
        public ParticleEmitterComponent Create(ComponentDefaultBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            throw new InvalidOperationException("A particle emitter requires an explicitly selected Definition asset.");
        }
    }

    private sealed class ParticleEmitterFactory : IComponentFactory<ParticleEmitterComponent>
    {
        private readonly ComponentFactoryBinding<ParticleEmitterComponent> _binding;

        internal ParticleEmitterFactory(ComponentFactoryBinding<ParticleEmitterComponent> binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            _binding = binding;
        }

        public ComponentFactoryBinding<ParticleEmitterComponent> Binding => _binding;
    }

    private sealed class ParticleEmitterRestoreProvider : IComponentRestoreProvider<ParticleEmitterComponent, ParticleEmitterAuthor>
    {
        public ParticleEmitterComponent RebuildDerived(
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The persistent particle emitter has no Derived restore path.");
        }

        public ParticleEmitterComponent Restore(
            in ParticleEmitterAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();
            return new ParticleEmitterComponent(author.DefinitionAssetId, author.Enabled);
        }
    }

    private sealed class ParticleEmitterStateCodec : IComponentStateCodec<ParticleEmitterComponent, ParticleEmitterAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal ParticleEmitterStateCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        private static bool IsField(EbinFieldReadView field, ushort kind, int payloadBytes)
            => field.IsRequired
            && (field.Kind == kind)
            && (field.Count == 1)
            && (field.Payload.Length == payloadBytes);

        private static void ValidateDefinition(in ParticleEmitterComponent value)
        {
            if (value.DefinitionAssetId <= 0)
            {
                throw new InvalidOperationException("The particle emitter Definition asset identity must be positive.");
            }
        }

        public nint Count(
            in ParticleEmitterComponent value,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();
            return Encode(in value).Length;
        }

        public ComponentReadResult<ParticleEmitterAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<ParticleEmitterAuthor> binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
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
                ?? throw new InvalidOperationException("The particle emitter read did not issue an author result.");
        }

        public ImmutableArray<Diagnostic> Validate(
            in ParticleEmitterComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDefinition(in value);
            return binding.ValidateFields(in value);
        }

        public nint Write(
            in ParticleEmitterComponent value,
            scoped Span<byte> destination,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = Encode(in value);

            if (destination.Length < bytes.Length)
            {
                throw new ArgumentException("The component destination is too small.", nameof(destination));
            }

            bytes.AsSpan().CopyTo(destination);
            return bytes.Length;
        }

        private byte[] Encode(in ParticleEmitterComponent value)
        {
            ValidateDefinition(in value);
            var definition = new EbinRecord(s_particleEmitterReferenceSchemaId, 1, 0, [
                EbinPrimitives.UInt32(1, checked((uint)value.DefinitionAssetId)),
                EbinPrimitives.Guid(2, s_particleEmitterAssetSchemaId),
                EbinPrimitives.UInt32(3, 1),
            ]);
            var record = new EbinRecord(
                _schema.SchemaId,
                checked((uint)_schema.Major),
                checked((uint)_schema.Minor),
                [
                    EbinPrimitives.Record(1, definition),
                    EbinPrimitives.Boolean(2, value.Enabled),
                ]);
            var status = EbinCodec.Prepare(record, out var bytes, out var diagnostic);

            if (status is not EbinStatus.Complete)
            {
                throw new InvalidOperationException($"The particle emitter record could not be encoded: {diagnostic.Category}.");
            }

            return bytes;
        }

        private struct ReadAction : IAction<EbinRecordReadView>
        {
            private readonly ComponentReadBinding<ParticleEmitterAuthor> _binding;
            private ComponentReadResult<ParticleEmitterAuthor>? _result;
            private readonly ContentSchemaReference _schema;

            internal ReadAction(
                ContentSchemaReference schema,
                ComponentReadBinding<ParticleEmitterAuthor> binding)
            {
                _schema = schema;
                _binding = binding;
                _result = null;
            }

            internal readonly ComponentReadResult<ParticleEmitterAuthor>? Result => _result;

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
                        "The particle emitter record does not match the selected schema tuple.");
                    return;
                }

                if (record.EncodedBytes.Length != _binding.SourceByteLength)
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.ConsumptionMismatch,
                        record.EncodedBytes.Length,
                        null,
                        "The particle emitter record does not consume its complete source extent.");
                    return;
                }

                if ((record.FieldCount != 2)
                    || !record.TryGetField(1, out var definition)
                    || !definition.IsRequired
                    || (definition.Kind != 22)
                    || (definition.Count != 1)
                    || !record.TryGetField(2, out var enabled)
                    || !IsField(enabled, 9, sizeof(bool)))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        null,
                        "The particle emitter fields do not match the selected schema.");
                    return;
                }

                var reference = record.GetRecord(1);
                var referenceHeader = reference.Header;

                if ((referenceHeader.SchemaId != s_particleEmitterReferenceSchemaId)
                    || (referenceHeader.Major != 1)
                    || (referenceHeader.Minor != 0)
                    || (reference.FieldCount != 3)
                    || !reference.TryGetField(1, out var assetId)
                    || !IsField(assetId, 3, sizeof(uint))
                    || !reference.TryGetField(2, out var schemaId)
                    || !IsField(schemaId, 11, 16)
                    || !reference.TryGetField(3, out var major)
                    || !IsField(major, 3, sizeof(uint)))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidField,
                        64,
                        ParticleEmitterComponent.Field1FieldPath,
                        "The particle emitter Definition reference is malformed.");
                    return;
                }

                var definitionId = BinaryPrimitives.ReadUInt32LittleEndian(assetId.Payload);

                if ((definitionId is 0 or > int.MaxValue)
                    || (new Guid(schemaId.Payload, bigEndian: true) != s_particleEmitterAssetSchemaId)
                    || (BinaryPrimitives.ReadUInt32LittleEndian(major.Payload) != 1))
                {
                    _result = _binding.Reject(
                        ComponentReadFailureKind.InvalidValue,
                        64,
                        ParticleEmitterComponent.Field1FieldPath,
                        "The particle emitter Definition must select a positive native/cosmetic-particle-emitter 1 AssetId.");
                    return;
                }

                var author = new ParticleEmitterAuthor(checked((int)definitionId), enabled.Payload[0] != 0);
                _result = _binding.Complete(in author, record.EncodedBytes.Length);
            }
        }
    }
}
