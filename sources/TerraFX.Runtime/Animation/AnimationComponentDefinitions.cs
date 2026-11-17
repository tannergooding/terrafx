// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Generators;

namespace TerraFX.Animation;

internal static partial class AnimationComponentDefinitions
{
    private static readonly Guid s_playerSchemaId = new ContentSchemaIdentityAssignment("animation/player").SchemaId;
    private static readonly Guid s_playerAssetReferenceSchemaId = new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId;
    private static readonly Guid s_playerControllerSchemaId = new ContentSchemaIdentityAssignment("native/animation-controller").SchemaId;

    internal static WorldRegistrationBuilder RegisterPlayer(
        WorldRegistrationBuilder builder,
        CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(schemas);
        var schemaReference = new ContentSchemaReference(s_playerSchemaId, 1, 0, ContentSchemaScope.Content);
        var layout = new ComponentLayout(
            new ContentSchemaIdentityAssignment("animation/player/layout/v1").SchemaId,
            Unsafe.SizeOf<AnimationPlayerComponent>());
        var schema = ComponentSchema<AnimationPlayerComponent>.FromRegistry(schemas, schemaReference, layout);
        var assetPath = AnimationPlayerComponent.Field1StoragePath;
        var assetField = ComponentFieldBinding<AnimationPlayerComponent, AnimationPlayerAuthor>.AssetReference<
            AnimationPlayerComponent.Field1Accessor,
            AnimationPlayerComponent.Field1Reader>(
                schema.GetField(AnimationPlayerComponent.Field1FieldPath),
                [assetPath],
                default,
                default);
        var assetBytes = Unsafe.SizeOf<int>();
        var storage = new ComponentStorageDeclaration(
            layout.LogicalBytes,
            [
                new ComponentStorageLeaf(
                    assetPath,
                    ComponentStorageKind.Value,
                    0,
                    assetBytes,
                    0,
                    0,
                    ComponentStoragePurpose.SchemaValue),
            ],
            []);
        var binding = ComponentFactoryBinding<AnimationPlayerComponent>.Create(
            new ContentSchemaIdentityAssignment("animation/player/factory/v1").SchemaId,
            1,
            schema,
            storage,
            ComponentTransientRestore.None,
            [],
            [assetField],
            new AnimationPlayerDefaultProvider(),
            new AnimationPlayerRestoreProvider(),
            new AnimationPlayerStateCodec(schemaReference));
        return builder.RegisterComponent(schema, new AnimationPlayerFactory(binding));
    }

    internal readonly record struct AnimationPlayerAuthor(int ControllerAssetId);

    [GenerateComponentBindings(typeof(AnimationPlayerAuthor))]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal partial struct AnimationPlayerComponent
    {
        [GenerateComponentFieldAccessor(1, nameof(AnimationPlayerAuthor.ControllerAssetId), "ControllerAssetId")]
        private int _controllerAssetId;

        internal AnimationPlayerComponent(int controllerAssetId)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(controllerAssetId);
            _controllerAssetId = controllerAssetId;
        }

        internal readonly int ControllerAssetId => _controllerAssetId;
    }

    private sealed class AnimationPlayerDefaultProvider : IComponentDefaultProvider<AnimationPlayerComponent>
    {
        public AnimationPlayerComponent Create(ComponentDefaultBinding binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            throw new InvalidOperationException("An Animation Player requires an explicitly selected Controller asset.");
        }
    }

    private sealed class AnimationPlayerFactory : IComponentFactory<AnimationPlayerComponent>
    {
        private readonly ComponentFactoryBinding<AnimationPlayerComponent> _binding;

        internal AnimationPlayerFactory(ComponentFactoryBinding<AnimationPlayerComponent> binding)
        {
            ArgumentNullException.ThrowIfNull(binding);
            _binding = binding;
        }

        public ComponentFactoryBinding<AnimationPlayerComponent> Binding => _binding;
    }

    private sealed class AnimationPlayerRestoreProvider
        : IComponentRestoreProvider<AnimationPlayerComponent, AnimationPlayerAuthor>
    {
        public AnimationPlayerComponent RebuildDerived(
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The Animation Player component has no Derived restore path.");
        }

        public AnimationPlayerComponent Restore(
            in AnimationPlayerAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();
            return new AnimationPlayerComponent(author.ControllerAssetId);
        }
    }

    private sealed class AnimationPlayerStateCodec : IComponentStateCodec<AnimationPlayerComponent, AnimationPlayerAuthor>
    {
        private readonly ContentSchemaReference _schema;

        internal AnimationPlayerStateCodec(ContentSchemaReference schema)
        {
            _schema = schema;
        }

        private static Diagnostic CreateDiagnostic(string field, string message)
            => new(
                "animation.player.component.invalid",
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
            in AnimationPlayerComponent value,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();
            return Encode(in value).Length;
        }

        public ComponentReadResult<AnimationPlayerAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<AnimationPlayerAuthor> binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();
            var action = new ReadAction();
            EbinReadStatus status;
            int bytesConsumed;
            EbinReadDiagnostic? diagnostic;

            try
            {
                status = EbinStructuralReader.Read(
                    source,
                    EbinReadLimits.Foundation,
                    EbinReadPolicy.Canonical,
                    ref action,
                    out bytesConsumed,
                    out diagnostic);
            }
            catch (NotSupportedException)
            {
                return binding.Reject(
                    ComponentReadFailureKind.SchemaMismatch,
                    0,
                    path: null,
                    "The Animation Player record does not match schema 1.0.");
            }
            catch (InvalidDataException)
            {
                return binding.Reject(
                    ComponentReadFailureKind.InvalidField,
                    0,
                    path: null,
                    "The Animation Player Controller reference is malformed or unsupported.");
            }

            if (status is not EbinReadStatus.Complete)
            {
                return binding.Reject(status, diagnostic);
            }

            if (bytesConsumed != source.Length)
            {
                return binding.Reject(
                    ComponentReadFailureKind.ConsumptionMismatch,
                    bytesConsumed,
                    path: null,
                    "The Animation Player record does not consume its complete source extent.");
            }

            var author = action.Author;
            return binding.Complete(in author, bytesConsumed);
        }

        public ImmutableArray<Diagnostic> Validate(
            in AnimationPlayerComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();

            if (value.ControllerAssetId <= 0)
            {
                return [CreateDiagnostic("Controller", "An Animation Player requires a positive Controller AssetId.")];
            }

            return binding.ValidateFields(in value);
        }

        public nint Write(
            in AnimationPlayerComponent value,
            scoped Span<byte> destination,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(binding);
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = Encode(in value);

            if (destination.Length < bytes.Length)
            {
                throw new InvalidOperationException("The Animation Player destination is smaller than its canonical record.");
            }

            bytes.AsSpan().CopyTo(destination);
            cancellationToken.ThrowIfCancellationRequested();
            return bytes.Length;
        }

        private byte[] Encode(in AnimationPlayerComponent value)
        {
            if (value.ControllerAssetId <= 0)
            {
                throw new InvalidOperationException("An Animation Player requires a positive Controller AssetId.");
            }

            var assetReference = new EbinRecord(s_playerAssetReferenceSchemaId, 1, 0, [
                EbinPrimitives.UInt32(1, checked((uint)value.ControllerAssetId)),
                EbinPrimitives.Guid(2, s_playerControllerSchemaId),
                EbinPrimitives.UInt32(3, 1),
            ]);
            var record = new EbinRecord(_schema.SchemaId, checked((uint)_schema.Major), checked((uint)_schema.Minor), [
                EbinPrimitives.Record(1, assetReference),
            ]);
            var status = EbinCodec.Prepare(record, out var bytes, out var diagnostic);

            if (status is not EbinStatus.Complete)
            {
                throw new InvalidOperationException($"The Animation Player record cannot be encoded: {diagnostic.Category}.");
            }

            return bytes;
        }

        private struct ReadAction : IAction<EbinRecordReadView>
        {
            internal AnimationPlayerAuthor Author { get; private set; }

            private static bool IsField(EbinFieldReadView field, ushort kind, bool required, ulong count)
                => field.IsRequired == required
                && (field.Kind == kind)
                && (field.Count == count)
                && ((kind == 22) || (field.Payload.Length == (kind switch {
                    3 => sizeof(uint),
                    11 => 16,
                    _ => 0,
                })));

            public void Invoke(scoped EbinRecordReadView record)
            {
                var header = record.Header;

                if ((header.SchemaId != s_playerSchemaId)
                    || (header.Major != 1)
                    || (header.Minor != 0)
                    || (header.FieldCount != 1)
                    || !record.TryGetField(1, out var playerField)
                    || !IsField(playerField, 22, required: true, count: 1))
                {
                    throw new InvalidDataException("The Animation Player record does not match schema 1.0.");
                }

                var reference = record.GetRecord(1);
                var referenceHeader = reference.Header;

                if ((referenceHeader.SchemaId != s_playerAssetReferenceSchemaId)
                    || (referenceHeader.Major != 1)
                    || (referenceHeader.Minor != 0)
                    || (referenceHeader.FieldCount != 3)
                    || !reference.TryGetField(1, out var assetIdField)
                    || !IsField(assetIdField, 3, required: true, count: 1)
                    || !reference.TryGetField(2, out var schemaIdField)
                    || !IsField(schemaIdField, 11, required: true, count: 1)
                    || !reference.TryGetField(3, out var schemaMajorField)
                    || !IsField(schemaMajorField, 3, required: true, count: 1))
                {
                    throw new InvalidDataException("The Animation Player Controller reference is malformed.");
                }

                var assetId = BinaryPrimitives.ReadUInt32LittleEndian(assetIdField.Payload);
                var schemaId = new Guid(schemaIdField.Payload, bigEndian: true);
                var schemaMajor = BinaryPrimitives.ReadUInt32LittleEndian(schemaMajorField.Payload);

                if ((assetId == 0)
                    || (assetId > int.MaxValue)
                    || (schemaId != s_playerControllerSchemaId)
                    || (schemaMajor != 1))
                {
                    throw new InvalidDataException("The Animation Player must select one native/animation-controller major-1 asset.");
                }

                Author = new AnimationPlayerAuthor(checked((int)assetId));
            }
        }
    }
}
