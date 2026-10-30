// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldResourceCodecDispatch<T, TAuthor> : IWorldResourceCodecDispatch<T>
    where T : unmanaged
    where TAuthor : notnull
{
    private readonly IWorldResourceStateCodec<T, TAuthor> _codec;
    private readonly ResourceFactoryFamily<T, TAuthor> _factory;

    internal WorldResourceCodecDispatch(
        ResourceFactoryFamily<T, TAuthor> factory,
        IWorldResourceStateCodec<T, TAuthor> codec)
    {
        _factory = factory;
        _codec = codec;
    }

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

    public WorldResourceStateCodecReadOutcome Read(
        scoped ReadOnlySpan<byte> source,
        Guid resourceId,
        ContentSchemaReference schema,
        int partitionCount,
        CancellationToken cancellationToken)
    {
        var binding = new WorldResourceReadBinding<TAuthor>(resourceId, schema, partitionCount, source.Length);

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

                _factory.ValidateAuthor(author, partitionCount);
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
}
