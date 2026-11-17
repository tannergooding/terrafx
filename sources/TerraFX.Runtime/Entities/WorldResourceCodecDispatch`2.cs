// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldResourceCodecDispatch<T, TAuthor> : IWorldResourceCodecDispatch<T>
    where T : unmanaged
    where TAuthor : notnull
{
    private const long MaximumOperationBytes = 128L * 1024 * 1024;

    private readonly IWorldResourceStateCodec<T, TAuthor> _codec;
    private readonly ResourceFactoryFamily<T, TAuthor> _factory;
    private readonly long[] _operationByteReservations;
    private readonly IWorldResourceRecordCodec<T, TAuthor>? _recordCodec;

    internal WorldResourceCodecDispatch(
        ResourceFactoryFamily<T, TAuthor> factory,
        IWorldResourceStateCodec<T, TAuthor> codec)
    {
        _factory = factory;
        _codec = codec;
        _operationByteReservations = factory.IsAggregate ? new long[4] : [];
        _recordCodec = null;

        if (factory.IsAggregate)
        {
            _recordCodec = codec as IWorldResourceRecordCodec<T, TAuthor>
                ?? throw new ArgumentException("A partition-record resource requires its complete internal record codec.", nameof(codec));

            for (var index = 0; index < _operationByteReservations.Length; index++)
            {
                var bytes = _recordCodec.GetOperationByteReservation((WorldResourceOperationKind)(index + 1));

                if (bytes is <= 0 or > MaximumOperationBytes)
                {
                    throw new ArgumentException("Aggregate resource operations require positive finite owned byte reservations.", nameof(codec));
                }

                _operationByteReservations[index] = bytes;
            }
        }
    }

    public Type SourceAuthorType => typeof(TAuthor);

    public nint Count(
        WorldResourceOperationControl operation,
        T[] partitions,
        WorldResourceCaptureData<T> data,
        CancellationToken cancellationToken)
    {
        var view = new WorldResourceStateView<T>(operation, partitions);
        var binding = new WorldResourceCaptureBinding(operation, data);

        try
        {
            if (_factory.IsAggregate)
            {
                _factory.ValidateRuntimePartitions(partitions);
            }

            var count = _codec.Count(view, binding, cancellationToken);

            if (operation.Failure is { } failure)
            {
                throw failure;
            }

            if (count < 0)
            {
                throw operation.RecordFailure(new InvalidOperationException("The resource codec returned a negative encoded byte count."));
            }

            return count;
        }
        finally
        {
            binding.Revoke();
        }
    }

    public long GetOperationByteReservation(WorldResourceOperationKind operation)
    {
        if (operation is < WorldResourceOperationKind.Validation or > WorldResourceOperationKind.Write)
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        return _factory.IsAggregate ? _operationByteReservations[(int)operation - 1] : 0;
    }

    public WorldResourceStateCodecReadOutcome Read(
        scoped ReadOnlySpan<byte> source,
        Guid resourceId,
        ContentSchemaReference schema,
        int partitionCount,
        CancellationToken cancellationToken)
        => Read(source, resourceId, schema, partitionCount, null, cancellationToken);

    public WorldResourceStateCodecReadOutcome Read(
        scoped ReadOnlySpan<byte> source,
        Guid resourceId,
        ContentSchemaReference schema,
        int partitionCount,
        WorldResourceOperationControl? operation,
        CancellationToken cancellationToken)
    {
        if (_factory.IsAggregate
            && ((operation is null) || !operation.IsOwnedBy(_factory.Control)
                || (operation.ResourceId != resourceId) || (operation.Schema != schema)
                || (operation.PartitionCount != partitionCount) || (operation.PartitionType != typeof(T))
                || (operation.Operation != WorldResourceOperationKind.Validation)))
        {
            throw new InvalidOperationException("The aggregate resource Read requires its exact issued operation.");
        }

        var binding = new WorldResourceReadBinding<TAuthor>(resourceId, schema, partitionCount, source.Length, operation);

        try
        {
            var result = _codec.Read(source, binding, cancellationToken)
                ?? throw new InvalidOperationException("The resource codec returned a null read result.");

            if (binding.Failure is { } failure)
            {
                throw failure;
            }

            if (!result.IsIssuedBy(binding))
            {
                throw new InvalidOperationException("The resource codec returned a result from another read binding.");
            }

            if (result.Status == WorldResourceReadStatus.Read)
            {
                if ((result.ConsumedBytes != source.Length) || !result.Diagnostics.IsEmpty
                    || !result.TryGetAuthorValue(out var author))
                {
                    throw new InvalidOperationException("The resource codec returned an inconsistent successful read result.");
                }

                try
                {
                    _factory.ValidateAuthor(author, partitionCount);
                }
                catch (InvalidDataException exception) when (_factory.IsAggregate)
                {
                    throw new InvalidOperationException("The resource codec returned an author outside its captured record contract.", exception);
                }
            }
            else if ((result.Status != WorldResourceReadStatus.Rejected)
                || (result.ConsumedBytes != 0)
                || result.Diagnostics.IsDefaultOrEmpty
                || result.TryGetAuthorValue(out _))
            {
                throw new InvalidOperationException("The resource codec returned an inconsistent rejected read result.");
            }

            return WorldResourceStateCodecReadOutcome.Create(result);
        }
        finally
        {
            binding.Revoke();
        }
    }

    public void Restore(
        object author,
        WorldResourceOperationControl operation,
        T[] partitions,
        CancellationToken cancellationToken)
    {
        if (author is not TAuthor typedAuthor)
        {
            throw new InvalidOperationException("The resource restore author has the wrong type.");
        }

        var binding = new WorldResourceRestoreBinding<T>(operation, partitions);

        try
        {
            _codec.Restore(in typedAuthor, binding, cancellationToken);

            if (operation.Failure is { } failure)
            {
                throw failure;
            }

            binding.ValidateComplete();
        }
        finally
        {
            binding.Revoke();
        }
    }

    public void RestorePreparedSource(
        IWorldPreparedResourceSource source,
        WorldResourceOperationControl operation,
        T[] partitions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var registration = operation.PreparedSourceRegistration;
        var owner = operation.PreparedSourceOwner;

        if ((source is not WorldResourceSource<TAuthor> typedSource)
            || (registration is null) || (owner is null)
            || !typedSource.IsBoundTo(registration, owner)
            || !operation.IsOwnedBy(_factory.Control)
            || (source.ResourceId != operation.ResourceId) || (source.Schema != operation.Schema)
            || (source.PartitionCount != operation.PartitionCount)
            || (partitions.Length != operation.PartitionCount))
        {
            throw operation.RecordFailure(new InvalidOperationException("The prepared resource source does not match the candidate's exact owner, family, or partition domain."));
        }

        Restore(typedSource.Author, operation, partitions, cancellationToken);
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "The exact source operation preserves its first sticky callback failure, including when provider code catches it.")]
    public bool TryReadSource(
        IWorldResourceRegistration registration,
        IWorldRestoreSourceOwner owner,
        out IWorldPreparedResourceSource? source,
        out ImmutableArray<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        source = null;
        diagnostics = [];
        using var lease = owner.AcquireRead();
        var payload = lease.ReadResource(registration);
        var operation = CreateSourceOperation(registration, owner, lease);
        WorldResourceStateCodecReadOutcome outcome;
        lease.EnterProviderRead();

        try
        {
            outcome = Read(payload, registration.ResourceId, registration.Schema,
                registration.PartitionCount, operation, cancellationToken);
        }
        catch (Exception exception)
        {
            throw lease.RecordFailure(exception);
        }
        finally
        {
            operation.Revoke();
            lease.ExitProviderRead();
        }

        lease.ThrowIfFailed();
        cancellationToken.ThrowIfCancellationRequested();

        if (outcome.Status is WorldResourceReadStatus.Rejected)
        {
            diagnostics = outcome.Diagnostics;
            return false;
        }

        if (outcome.Author is not TAuthor author)
        {
            throw lease.RecordFailure(new InvalidOperationException("The selected source reader returned another author family."));
        }

        if (_recordCodec is not null)
        {
            operation = CreateSourceOperation(registration, owner, lease);
            lease.EnterProviderRead();

            try
            {
                diagnostics = _recordCodec.ValidateSourceReferences(in author, operation.References, cancellationToken);

                if (diagnostics.IsDefault)
                {
                    throw new InvalidOperationException("The source validator returned uninitialized diagnostics.");
                }

                foreach (var diagnostic in diagnostics)
                {
                    ArgumentNullException.ThrowIfNull(diagnostic);
                }

                if (operation.Failure is { } failure)
                {
                    throw failure;
                }
            }
            catch (Exception exception)
            {
                throw lease.RecordFailure(exception);
            }
            finally
            {
                operation.Revoke();
                lease.ExitProviderRead();
            }

            lease.ThrowIfFailed();
            cancellationToken.ThrowIfCancellationRequested();

            if (!diagnostics.IsEmpty)
            {
                diagnostics = ImmutableArrayCopy.Create(diagnostics);
                return false;
            }
        }

        var ownership = lease.RetainStorage();

        try
        {
            source = new WorldResourceSource<TAuthor>(registration, owner, author, ownership);
            ownership = null;
            return true;
        }
        finally
        {
            ownership?.Dispose();
        }
    }

    public ImmutableArray<Diagnostic> Validate(
        WorldResourceOperationControl operation,
        T[] partitions,
        CancellationToken cancellationToken)
    {
        var view = new WorldResourceStateView<T>(operation, partitions);
        var binding = new WorldResourceValidationBinding(operation);

        try
        {
            var diagnostics = _codec.Validate(view, binding, cancellationToken);

            if (operation.Failure is { } failure)
            {
                throw failure;
            }

            if (diagnostics.IsDefault)
            {
                throw new InvalidOperationException("The resource codec returned a default diagnostics array.");
            }

            foreach (var diagnostic in diagnostics)
            {
                ArgumentNullException.ThrowIfNull(diagnostic);
            }

            return ImmutableArrayCopy.Create(diagnostics);
        }
        finally
        {
            binding.Revoke();
        }
    }

    public nint Write(
        WorldResourceOperationControl operation,
        T[] partitions,
        WorldResourceCaptureData<T> data,
        scoped Span<byte> destination,
        CancellationToken cancellationToken)
    {
        var view = new WorldResourceStateView<T>(operation, partitions);
        var binding = new WorldResourceCaptureBinding(operation, data);

        try
        {
            if (_factory.IsAggregate)
            {
                _factory.ValidateRuntimePartitions(partitions);
            }

            var written = _codec.Write(view, destination, binding, cancellationToken);

            if (operation.Failure is { } failure)
            {
                throw failure;
            }

            if ((written < 0) || (written > destination.Length))
            {
                throw operation.RecordFailure(new InvalidOperationException("The resource codec reported an invalid encoded byte count."));
            }

            return written;
        }
        finally
        {
            binding.Revoke();
        }
    }

    private WorldResourceOperationControl CreateSourceOperation(
        IWorldResourceRegistration registration,
        IWorldRestoreSourceOwner owner,
        WorldRestoreSourceLease lease)
    {
        var operation = new WorldResourceOperationControl(
            _factory.Control, registration.ResourceId, registration.Schema,
            registration.PartitionCount, typeof(T), WorldResourceOperationKind.Validation);
        operation.BindReferences(owner.IdentityHistory, owner.CompletedTick, owner.Limits, lease.Control);
        return operation;
    }
}
