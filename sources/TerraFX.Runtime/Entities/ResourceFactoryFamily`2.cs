// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

internal sealed class ResourceFactoryFamily<T, TAuthor> : IWorldResourceFactoryDispatch<T>
    where T : unmanaged
    where TAuthor : notnull
{
    internal ResourceFactoryFamily(
        WorldResourceFactoryDescriptor descriptor,
        ImmutableArray<WorldResourceFieldBinding<T, TAuthor>> fields,
        IWorldResourceDefaultProvider<T> defaults)
    {
        Descriptor = descriptor;
        Fields = fields;
        Defaults = defaults;
        Control = new ResourceFactoryControl<T>(this);
        RegistrationBinding = new WorldResourceFactoryBinding<T>(Control);
    }

    public WorldResourceFactoryDescriptor Descriptor { get; }

    internal ResourceFactoryControl<T> Control { get; }

    internal IWorldResourceDefaultProvider<T> Defaults { get; }

    internal ImmutableArray<WorldResourceFieldBinding<T, TAuthor>> Fields { get; }

    internal WorldResourceFactoryBinding<T> RegistrationBinding { get; }

    public WorldResourceCaptureData<T> CreateCaptureData(T[] partitions)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        var fields = ImmutableArray.CreateBuilder<IWorldResourceFieldAccess<T>>(Fields.Length);

        foreach (var field in Fields)
        {
            fields.Add(field.FieldAccess);
        }

        return new WorldResourceCaptureData<T>(partitions, fields.MoveToImmutable());
    }

    public T CreateDefault(int partitionIndex, WorldResourceDefaultBinding binding)
        => Defaults.Create(partitionIndex, binding);

    public void ValidateAuthor(object author, int partitionCount)
    {
        if (author is not TAuthor typedAuthor)
        {
            throw new InvalidOperationException("The resource author root has the wrong type.");
        }

        foreach (var field in Fields)
        {
            var access = field.FieldAccess;

            if (access.GetAuthorCount(typedAuthor) != partitionCount)
            {
                throw new InvalidOperationException("The resource author FixedArray count does not match the registered partition count.");
            }

            for (var index = 0; index < partitionCount; index++)
            {
                _ = access.ReadAuthorElement(typedAuthor, index);
            }
        }
    }

    public void ValidateRuntimePartition(in T partition)
    {
        foreach (var field in Fields)
        {
            _ = field.FieldAccess.ReadPartition(in partition);
        }
    }
}
