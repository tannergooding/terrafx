// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldResourceRegistration<T> : IWorldResourceRegistration
    where T : unmanaged
{
    private const int ArrayHeaderBytes = 64;

    private readonly IWorldResourceCodecDispatch<T> _codec;
    private readonly SystemResourceDescription _description;
    private readonly ResourceFactoryControl<T> _factory;

    internal WorldResourceRegistration(
        SystemResourceDescription description,
        ResourceFactoryControl<T> factory,
        IWorldResourceCodecDispatch<T> codec)
    {
        _description = description;
        _factory = factory;
        _codec = codec;
        Descriptor = new WorldResourceRegistrationDescriptor(description, factory.Descriptor);
    }

    public bool AllowParallelAccess => _description.AllowParallelAccess;

    public SystemResourceDescription Description => _description;

    public WorldResourceRegistrationDescriptor Descriptor { get; }

    public bool IsAggregate => _factory.Dispatch.IsAggregate;

    public int PartitionCount => _description.PartitionCount;

    public Guid ResourceId => _description.ResourceId;

    public Type ResourceType => typeof(T);

    public ContentSchemaReference Schema => _factory.Descriptor.Schema;

    public Type SourceAuthorType => _codec.SourceAuthorType;

    private static long ArrayBytes(int count, int stride)
        => checked(ArrayHeaderBytes + ((checked((long)count * stride) + 7) & ~7L));

    public nint CountStateRecord(object storage, CancellationToken cancellationToken, World? world = null, long? completedTick = null)
    {
        using var reservation = ReserveOperation(world, WorldResourceOperationKind.Count);
        var partitions = GetPartitions(storage);
        var operation = CreateOperation(WorldResourceOperationKind.Count, world, completedTick);
        return _codec.Count(operation, partitions, _factory.Dispatch.CreateCaptureData(partitions), cancellationToken);
    }

    public object CreateStorage()
        => new T[PartitionCount];

    public long GetStorageByteReservation()
        => ArrayBytes(PartitionCount, Unsafe.SizeOf<T>());

    public void InitializeAndValidate(object storage, World? world = null)
    {
        using var reservation = ReserveOperation(world, WorldResourceOperationKind.Validation);
        ArgumentNullException.ThrowIfNull(storage);

        if (storage is not T[] partitions || (partitions.Length != PartitionCount))
        {
            throw new InvalidOperationException("The resource storage array does not match its frozen registration.");
        }

        for (var index = 0; index < partitions.Length; index++)
        {
            var binding = new WorldResourceDefaultBinding(ResourceId, _factory.Descriptor.Schema, PartitionCount, index);
            var value = _factory.Dispatch.CreateDefault(index, binding);
            _factory.Dispatch.ValidateRuntimePartition(in value);
            partitions[index] = value;
        }

        var operation = CreateOperation(WorldResourceOperationKind.Validation, world, null);
        var diagnostics = _codec.Validate(operation, partitions, CancellationToken.None);

        if (operation.Failure is { } failure)
        {
            throw failure;
        }

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity >= DiagnosticSeverity.Error)
            {
                throw new InvalidOperationException("A resource default partition failed codec validation.");
            }
        }
    }

    public void RestorePreparedSource(
        IWorldPreparedResourceSource source,
        object storage,
        World candidate,
        long completedTick,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();
        var owner = candidate.ValidatePreparedResourceSource(this, source, completedTick);
        using var reservation = ReserveOperation(candidate, WorldResourceOperationKind.Restore);
        var operation = CreateOperation(WorldResourceOperationKind.Restore, candidate, completedTick);
        operation.BindPreparedSource(this, owner);

        try
        {
            _codec.RestorePreparedSource(source, operation, GetPartitions(storage), cancellationToken);
        }
        finally
        {
            operation.Revoke();
        }

        ValidateForExecution(storage, candidate, completedTick);
    }

    public void RestoreStateRecord(
        scoped ReadOnlySpan<byte> source,
        object storage,
        CancellationToken cancellationToken,
        World? world = null,
        long? completedTick = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reservation = ReserveOperation(world, WorldResourceOperationKind.Restore);
        var readOperation = IsAggregate ? CreateOperation(WorldResourceOperationKind.Validation, world, completedTick) : null;
        var outcome = _codec.Read(source, ResourceId, Schema, PartitionCount, readOperation, cancellationToken);

        if (outcome.Status is WorldResourceReadStatus.Rejected)
        {
            var diagnostic = outcome.Diagnostics[0];
            throw new InvalidDataException($"{diagnostic.Code}: {diagnostic.Message}");
        }

        if ((outcome.Status is not WorldResourceReadStatus.Read)
            || (outcome.ConsumedBytes != source.Length)
            || (outcome.Author is null)
            || !outcome.Diagnostics.IsEmpty)
        {
            throw new InvalidOperationException("The successful resource state read did not return its complete author value.");
        }

        var partitions = GetPartitions(storage);
        _codec.Restore(
            outcome.Author,
            CreateOperation(WorldResourceOperationKind.Restore, world, completedTick),
            partitions,
            cancellationToken);
        ValidateForExecution(storage, world, completedTick);
    }

    public bool TryReadSource(
        IWorldRestoreSourceOwner owner,
        out IWorldPreparedResourceSource? source,
        out ImmutableArray<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
        => _codec.TryReadSource(this, owner, out source, out diagnostics, cancellationToken);

    public void ValidateForExecution(object storage, World? world = null, long? requiredRequestTick = null)
    {
        using var reservation = ReserveOperation(world, WorldResourceOperationKind.Validation);
        var partitions = GetPartitions(storage);

        for (var index = 0; index < partitions.Length; index++)
        {
            _factory.Dispatch.ValidateRuntimePartition(in partitions[index]);
        }

        var operation = CreateOperation(WorldResourceOperationKind.Validation, world, requiredRequestTick);
        var diagnostics = _codec.Validate(operation, partitions, CancellationToken.None);

        if (operation.Failure is { } failure)
        {
            throw failure;
        }

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity >= DiagnosticSeverity.Error)
            {
                throw new InvalidOperationException("A resource partition failed codec validation after a system scope.");
            }
        }
    }

    public nint WriteStateRecord(object storage, scoped Span<byte> destination, CancellationToken cancellationToken, World? world = null, long? completedTick = null)
    {
        using var reservation = ReserveOperation(world, WorldResourceOperationKind.Write);
        var partitions = GetPartitions(storage);
        var operation = CreateOperation(WorldResourceOperationKind.Write, world, completedTick);
        return _codec.Write(
            operation,
            partitions,
            _factory.Dispatch.CreateCaptureData(partitions),
            destination,
            cancellationToken);
    }

    private WorldResourceOperationControl CreateOperation(WorldResourceOperationKind operation, World? world, long? requiredRequestTick)
    {
        var control = new WorldResourceOperationControl(
            _factory,
            ResourceId,
            Schema,
            PartitionCount,
            typeof(T),
            operation);

        if (IsAggregate)
        {
            control.BindReferences(world ?? throw new InvalidOperationException("An aggregate resource operation requires its actual World issuer."), requiredRequestTick);
        }

        return control;
    }

    private T[] GetPartitions(object storage)
    {
        ArgumentNullException.ThrowIfNull(storage);

        if (storage is not T[] partitions || (partitions.Length != PartitionCount))
        {
            throw new InvalidOperationException("The resource storage array does not match its frozen registration.");
        }

        return partitions;
    }

    private WorldResourceOperationReservation? ReserveOperation(World? world, WorldResourceOperationKind operation)
    {
        var bytes = _codec.GetOperationByteReservation(operation);

        if (bytes == 0)
        {
            return null;
        }

        var owner = world ?? throw new InvalidOperationException("An aggregate resource operation requires its actual World admission owner.");
        var registered = false;

        foreach (var resource in owner.Registration.Resources)
        {
            registered |= ReferenceEquals(resource, this);
        }

        if (!registered)
        {
            throw new ArgumentException("The resource operation does not belong to the supplied World registration.", nameof(world));
        }

        return owner.ReserveResourceOperationBytes(bytes, GetStorageByteReservation());
    }
}
