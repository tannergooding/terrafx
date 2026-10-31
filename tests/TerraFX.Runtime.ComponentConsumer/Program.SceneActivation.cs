// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.ComponentConsumer;

internal static partial class Program
{
    private readonly struct OptionalReferenceAccessor : IComponentOptionalAccessor<ReferenceComponent, Entity>
    {
        public void Clear(ref ReferenceComponent value) => value.SetTarget(default);

        public Entity GetPresent(in ReferenceComponent value) => value.Target;

        public bool IsPresent(in ReferenceComponent value) => value.Target.IsValid;

        public void SetPresent(ref ReferenceComponent value, in Entity fieldValue) => value.SetTarget(fieldValue);
    }

    private readonly record struct OptionalReferenceAuthor(Guid TargetId, bool IsPresent);

    private readonly struct OptionalReferenceAuthorReader : IComponentOptionalReader<OptionalReferenceAuthor, Guid>
    {
        public Guid GetPresent(in OptionalReferenceAuthor value) => value.TargetId;

        public bool IsPresent(in OptionalReferenceAuthor value) => value.IsPresent;
    }

    private sealed class OptionalReferenceFactory : IComponentFactory<ReferenceComponent>
    {
        private readonly ComponentFactoryBinding<ReferenceComponent> _binding;

        internal OptionalReferenceFactory(
            ComponentSchema<ReferenceComponent> schema,
            ComponentStorageDeclaration storage,
            ComponentFieldBinding<ReferenceComponent, OptionalReferenceAuthor> targetReference,
            ComponentFieldDescriptor targetField)
        {
            _binding = ComponentFactoryBinding<ReferenceComponent>.Create(
                new ContentSchemaIdentityAssignment("test/scene-expanded-entity-reference/factory/v1").SchemaId,
                1,
                schema,
                storage,
                ComponentTransientRestore.None,
                [],
                [targetReference],
                new ReferenceDefaultProvider(),
                new OptionalReferenceRestoreProvider(targetField),
                new OptionalReferenceStateCodec(schema.Schema, targetField));
        }

        public ComponentFactoryBinding<ReferenceComponent> Binding => _binding;
    }

    private struct OptionalReferenceReadAction : IAction<EbinRecordReadView>
    {
        private readonly ComponentReadBinding<OptionalReferenceAuthor> _binding;
        private readonly ContentSchemaReference _schema;
        private readonly ComponentFieldDescriptor _targetField;
        private ComponentReadResult<OptionalReferenceAuthor>? _result;

        internal OptionalReferenceReadAction(
            ContentSchemaReference schema,
            ComponentFieldDescriptor targetField,
            ComponentReadBinding<OptionalReferenceAuthor> binding)
        {
            _schema = schema;
            _targetField = targetField;
            _binding = binding;
            _result = null;
        }

        internal readonly ComponentReadResult<OptionalReferenceAuthor>? Result => _result;

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
                    _targetField.Path,
                    "The optional Entity-reference record does not match the selected schema tuple.");
                return;
            }

            if (record.FieldCount == 0)
            {
                var absentAuthor = new OptionalReferenceAuthor(Guid.Empty, IsPresent: false);
                _result = _binding.Complete(in absentAuthor, record.EncodedBytes.Length);
                return;
            }

            if ((record.FieldCount != 1)
                || !record.TryGetField(1, out var target)
                || (target.Kind != 11)
                || target.IsRequired
                || (target.Count != 1)
                || (target.Payload.Length != Unsafe.SizeOf<Guid>()))
            {
                _result = _binding.Reject(
                    ComponentReadFailureKind.InvalidField,
                    64,
                    _targetField.Path,
                    "The optional Entity-reference field does not match the selected schema.");
                return;
            }

            var targetId = new Guid(target.Payload, bigEndian: true);

            if (targetId == Guid.Empty)
            {
                _result = _binding.Reject(
                    ComponentReadFailureKind.InvalidField,
                    64,
                    _targetField.Path,
                    "The optional Entity-reference identity must be nonempty.");
                return;
            }

            var author = new OptionalReferenceAuthor(targetId, IsPresent: true);
            _result = _binding.Complete(in author, record.EncodedBytes.Length);
        }
    }

    private sealed class OptionalReferenceRestoreProvider :
        IComponentRestoreProvider<ReferenceComponent, OptionalReferenceAuthor>
    {
        private readonly ComponentFieldDescriptor _targetField;

        internal OptionalReferenceRestoreProvider(ComponentFieldDescriptor targetField)
        {
            _targetField = targetField;
        }

        public ReferenceComponent RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken)
            => default;

        public ReferenceComponent Restore(
            in OptionalReferenceAuthor author,
            WorldStateRestoreBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!author.IsPresent)
            {
                return new ReferenceComponent(default);
            }

            return new ReferenceComponent(
                binding.ResolveOptionalEntity(_targetField, author.TargetId) ?? default);
        }
    }

    private sealed class OptionalReferenceStateCodec :
        IComponentStateCodec<ReferenceComponent, OptionalReferenceAuthor>
    {
        private readonly ContentSchemaReference _schema;
        private readonly ComponentFieldDescriptor _targetField;

        internal OptionalReferenceStateCodec(ContentSchemaReference schema, ComponentFieldDescriptor targetField)
        {
            _schema = schema;
            _targetField = targetField;
        }

        private static EbinFieldSource[] CreateFields()
            => [new EbinFieldSource(1, 11, 0, 1, new EbinPayloadSlice(0, Unsafe.SizeOf<Guid>()))];

        private static nint MeasureRecord(
            ContentSchemaReference schema,
            ReadOnlySpan<EbinFieldSource> fields,
            ReadOnlySpan<byte> payload)
        {
            var record = new EbinRecordWriteView(
                schema.SchemaId,
                schema.Major,
                schema.Minor,
                fields,
                payload);

            if (EbinStructuralWriter.Measure(record, EbinReadLimits.Foundation, out var recordBytes, out _) != EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException("The optional Entity-reference record could not be measured.");
            }

            return recordBytes;
        }

        private static nint WriteRecord(
            ContentSchemaReference schema,
            ReadOnlySpan<EbinFieldSource> fields,
            ReadOnlySpan<byte> payload,
            scoped Span<byte> candidate,
            scoped Span<byte> destination)
        {
            var record = new EbinRecordWriteView(
                schema.SchemaId,
                schema.Major,
                schema.Minor,
                fields,
                payload);
            var status = EbinStructuralWriter.Write(
                record,
                EbinReadLimits.Foundation,
                candidate,
                destination,
                out var bytesWritten,
                out var diagnostic);

            if (status == EbinWriteStatus.DestinationTooSmall)
            {
                throw new ArgumentException(
                    $"The optional Entity-reference destination is too small: {diagnostic?.Category}.",
                    nameof(destination));
            }

            if (status != EbinWriteStatus.Complete)
            {
                throw new InvalidOperationException(
                    $"The optional Entity-reference record could not be written: {diagnostic?.Category}.");
            }

            return bytesWritten;
        }

        private static void WriteTargetId(Guid targetId, scoped Span<byte> payload)
        {
            if ((EbinPayloadWriter.Write(targetId, payload, out var bytesWritten, out _) != EbinWriteStatus.Complete)
                || (bytesWritten != Unsafe.SizeOf<Guid>()))
            {
                throw new InvalidOperationException("The optional Entity reference could not be encoded.");
            }
        }

        public nint Count(
            in ReferenceComponent value,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!value.Target.IsValid)
            {
                return MeasureRecord(_schema, [], []);
            }

            Span<byte> payload = stackalloc byte[Unsafe.SizeOf<Guid>()];
            WriteTargetId(binding.GetEntityId(_targetField, value.Target), payload);
            return MeasureRecord(_schema, CreateFields(), payload);
        }

        public ComponentReadResult<OptionalReferenceAuthor> Read(
            scoped ReadOnlySpan<byte> source,
            ComponentReadBinding<OptionalReferenceAuthor> binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var action = new OptionalReferenceReadAction(_schema, _targetField, binding);
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
                ?? throw new InvalidOperationException("The optional Entity-reference read did not issue an author result.");
        }

        public ImmutableArray<Diagnostic> Validate(
            in ReferenceComponent value,
            ComponentValidationBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return binding.ValidateFields(in value);
        }

        public nint Write(
            in ReferenceComponent value,
            scoped Span<byte> destination,
            ComponentCaptureBinding binding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> candidate = stackalloc byte[256];

            if (!value.Target.IsValid)
            {
                return WriteRecord(_schema, [], [], candidate, destination);
            }

            Span<byte> payload = stackalloc byte[Unsafe.SizeOf<Guid>()];
            WriteTargetId(binding.GetEntityId(_targetField, value.Target), payload);
            return WriteRecord(_schema, CreateFields(), payload, candidate, destination);
        }
    }
}
