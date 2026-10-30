// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
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

    public int PartitionCount => _description.PartitionCount;

    public Guid ResourceId => _description.ResourceId;

    public Type ResourceType => typeof(T);

    private static long ArrayBytes(int count, int stride)
        => checked(ArrayHeaderBytes + ((checked((long)count * stride) + 7) & ~7L));

    public object CreateStorage()
        => new T[PartitionCount];

    public long GetStorageByteReservation()
        => ArrayBytes(PartitionCount, Unsafe.SizeOf<T>());

    public void InitializeAndValidate(object storage)
    {
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

        var operation = new WorldResourceOperationControl(
            _factory,
            ResourceId,
            _factory.Descriptor.Schema,
            PartitionCount,
            typeof(T),
            WorldResourceOperationKind.Validation);
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

    public void ValidateForExecution(object storage)
    {
        ArgumentNullException.ThrowIfNull(storage);

        if (storage is not T[] partitions || (partitions.Length != PartitionCount))
        {
            throw new InvalidOperationException("The resource storage array does not match its frozen registration.");
        }

        for (var index = 0; index < partitions.Length; index++)
        {
            _factory.Dispatch.ValidateRuntimePartition(in partitions[index]);
        }

        var operation = new WorldResourceOperationControl(
            _factory,
            ResourceId,
            _factory.Descriptor.Schema,
            PartitionCount,
            typeof(T),
            WorldResourceOperationKind.Validation);
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
}
