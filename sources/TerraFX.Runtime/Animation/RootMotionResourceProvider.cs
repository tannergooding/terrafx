// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using TerraFX.Entities;

namespace TerraFX.Animation;

internal sealed partial class RootMotionResourceProvider :
    IWorldResourceFactory<RootMotionRequest>,
    IWorldResourceDefaultProvider<RootMotionRequest>,
    IWorldResourceRecordCodec<RootMotionRequest, RootMotionResourceAuthor>
{
    private readonly WorldResourceCodecBinding<RootMotionRequest> _codec;
    private readonly WorldResourceFactoryBinding<RootMotionRequest, RootMotionResourceAuthor> _factory;
    private readonly WorldResourceFieldDescriptor _values;

    internal RootMotionResourceProvider(CompiledContentSchemaRegistry schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        _factory = CreateBinding(schemas, this);
        _values = _factory.Descriptor.Fields[0];
        _codec = WorldResourceCodecBinding<RootMotionRequest>.Create(_factory, this);
    }

    internal WorldResourceFactoryBinding<RootMotionRequest, RootMotionResourceAuthor> Factory => _factory;

    WorldResourceFactoryBinding<RootMotionRequest> IWorldResourceFactory<RootMotionRequest>.Binding => _factory.RegistrationBinding;

    WorldResourceCodecBinding<RootMotionRequest> IWorldResourceStateCodec<RootMotionRequest>.Binding => _codec;

    internal static long MeasureOperationByteReservation(WorldResourceOperationKind operation)
    {
        // Use the existing 64-byte managed-owner/array envelope and aligned field bounds.
        // Each callback's complete maximum is admitted before creating any of these owners.
        const long ControlAndStackBytes = 1024 + (128 * 1024);
        var defaults = checked(4096L * (
            64 + Align8(Unsafe.SizeOf<Guid>()) + Align8(Unsafe.SizeOf<ContentSchemaReference>()) + (2 * Align8(sizeof(int)))));
        var boxes = checked(4096L * (64 + Align8(Unsafe.SizeOf<RootMotionRequest>())));
        var slotAuthorBytes = checked(
            64 + (6 * Align8(Unsafe.SizeOf<Guid?>())) + Align8(Unsafe.SizeOf<long?>())
            + Align8(Unsafe.SizeOf<TerraFX.Numerics.Vector3?>()) + Align8(Unsafe.SizeOf<TerraFX.Numerics.Quaternion?>())
            + Align8(Unsafe.SizeOf<ImmutableArray<RootMotionSourceAuthor>?>())
            + Align8(Unsafe.SizeOf<byte?>()) + Align8(Unsafe.SizeOf<WorldAssetReference?>()) + 8);
        var authors = checked(
            64 + (2 * ArrayBytes(4096, IntPtr.Size))
            + (4096 * (slotAuthorBytes + (2 * ArrayBytes(4, Unsafe.SizeOf<RootMotionSourceAuthor>())))));

        return operation switch {
            WorldResourceOperationKind.Validation => checked(ControlAndStackBytes + defaults),
            WorldResourceOperationKind.Count => checked(ControlAndStackBytes + boxes),
            WorldResourceOperationKind.Restore => checked(ControlAndStackBytes + authors + ArrayBytes(4096, sizeof(bool))),
            WorldResourceOperationKind.Write => checked(
                ControlAndStackBytes + (2 * boxes) + ArrayBytes(4096, Unsafe.SizeOf<EbinPayloadSlice>())
                + ArrayBytes(4096, RootMotionResourceCodec.MaximumSlotBytes)
                + ArrayBytes(RootMotionResourceCodec.MaximumRecordBytes - 96, 1)
                + ArrayBytes(RootMotionResourceCodec.MaximumRecordBytes, 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    private static long Align8(long bytes) => checked((bytes + 7) & ~7L);

    private static long ArrayBytes(int count, int stride) => checked(64 + Align8(checked((long)count * stride)));

    private static void ValidateContext(
        scoped ReadOnlySpan<RootMotionRequest> values, WorldResourceReferenceBinding references)
    {
        if (!RootMotionResourceCodec.ValidateRuntimeContext(values, references))
        {
            throw new InvalidDataException("The captured root-motion requests do not match their issued World/tick/history context.");
        }
    }

    public nint Count(
        scoped WorldResourceStateView<RootMotionRequest> value,
        WorldResourceCaptureBinding binding,
        CancellationToken cancellationToken)
    {
        ValidateContext(value.Partitions, binding.References);
        return RootMotionResourceCodec.Measure(binding, _values, cancellationToken).RecordBytes;
    }

    public RootMotionRequest Create(int partitionIndex, WorldResourceDefaultBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if ((binding.ResourceId != AnimationResources.RootMotionRequestsId)
            || (binding.Schema != _factory.Descriptor.Schema) || (binding.PartitionCount != 4096)
            || (binding.PartitionIndex != partitionIndex) || ((uint)partitionIndex >= 4096))
        {
            throw new InvalidOperationException("The root-motion default binding does not match its exact resource domain.");
        }

        return default;
    }

    public long GetOperationByteReservation(WorldResourceOperationKind operation)
        => MeasureOperationByteReservation(operation);

    public WorldResourceReadResult<RootMotionResourceAuthor> Read(
        scoped ReadOnlySpan<byte> source,
        WorldResourceReadBinding<RootMotionResourceAuthor> binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var references = binding.References;
        var status = RootMotionResourceCodec.Validate(
            source, in references, cancellationToken, out var valid, out var consumed, out var diagnostic);

        if (status != EbinReadStatus.Complete)
        {
            return binding.Reject(status, diagnostic);
        }

        if (consumed != source.Length)
        {
            return binding.Reject(WorldResourceReadFailureKind.ConsumptionMismatch, consumed, _values.Path,
                "The root-motion source must contain exactly one complete canonical record.");
        }

        if (!valid)
        {
            return binding.Reject(WorldResourceReadFailureKind.InvalidValue, 0, _values.Path,
                "The root-motion source violates its exact slot/source/presence/reference contract.");
        }

        var author = RootMotionResourceCodec.ReadAuthors(source, in references, cancellationToken);
        return binding.Complete(in author, consumed);
    }

    public void Restore(
        in RootMotionResourceAuthor author,
        WorldResourceRestoreBinding<RootMotionRequest> binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(binding);
        var references = binding.References;

        if (!references.RequiredRequestTick.HasValue || (binding.PartitionCount != 4096)
            || !default(RootMotionResourceValidation.ResourceValidator).IsValidAuthor(in author))
        {
            throw new InvalidDataException("The restored root-motion author requires its complete issued tick/domain.");
        }

        for (var index = 0; index < 4096; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = RootMotionResourceCodec.RestoreSlot(author.Values[index], references);
            binding.SetPartition(index, in value);
        }
    }

    public ImmutableArray<Diagnostic> Validate(
        scoped WorldResourceStateView<RootMotionRequest> value,
        WorldResourceValidationBinding binding,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = binding.ValidateFields(value);

        if (!RootMotionResourceCodec.ValidateRuntimeContext(value.Partitions, binding.References))
        {
            return [
                new Diagnostic(
                    "Animation.RootMotionResource.InvalidValue", DiagnosticSeverity.Error, 0,
                    null, null, null, null, null, null, null, null, null, null, null, null,
                    "RootMotionResource.Validate", null,
                    "A root-motion request has a stale tick, foreign World, or mismatched historical creation facts.", []),
            ];
        }

        return [];
    }

    public ImmutableArray<Diagnostic> ValidateSourceReferences(
        in RootMotionResourceAuthor author,
        WorldResourceReferenceBinding binding,
        CancellationToken cancellationToken)
    {
        foreach (var slot in author.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (slot.Present
                && ((slot.TickIndex != binding.RequiredRequestTick)
                    || !binding.TryResolveHistoricalEntity(
                        slot.PlayerId.GetValueOrDefault(), slot.PlayerCreationLineageId.GetValueOrDefault(),
                        slot.PlayerCreationBranchId.GetValueOrDefault(), out _)
                    || !binding.TryResolveHistoricalEntity(
                        slot.ActorId.GetValueOrDefault(), slot.ActorCreationLineageId.GetValueOrDefault(),
                        slot.ActorCreationBranchId.GetValueOrDefault(), out _)))
            {
                return [
                    new Diagnostic(
                        "Animation.RootMotionResource.InvalidValue", DiagnosticSeverity.Error, 0,
                        null, null, null, null, null, null, null, null, null, null, null, null,
                        "RootMotionResource.Validate", null,
                        "A root-motion source has a stale tick or mismatched historical creation facts.", []),
                ];
            }
        }

        return [];
    }

    public nint Write(
        scoped WorldResourceStateView<RootMotionRequest> value,
        scoped Span<byte> destination,
        WorldResourceCaptureBinding binding,
        CancellationToken cancellationToken)
    {
        ValidateContext(value.Partitions, binding.References);
        return RootMotionResourceCodec.Write(binding, _values, destination, cancellationToken);
    }
}
