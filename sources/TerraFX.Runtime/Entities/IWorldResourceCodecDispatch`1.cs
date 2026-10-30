// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

internal interface IWorldResourceCodecDispatch<T>
    where T : unmanaged
{
    nint Count(
        WorldResourceOperationControl operation,
        T[] partitions,
        WorldResourceCaptureData<T> data,
        CancellationToken cancellationToken);

    WorldResourceStateCodecReadOutcome Read(
        scoped ReadOnlySpan<byte> source,
        Guid resourceId,
        ContentSchemaReference schema,
        int partitionCount,
        CancellationToken cancellationToken);

    void Restore(
        object author,
        WorldResourceOperationControl operation,
        T[] partitions,
        CancellationToken cancellationToken);

    ImmutableArray<Diagnostic> Validate(
        WorldResourceOperationControl operation,
        T[] partitions,
        CancellationToken cancellationToken);

    nint Write(
        WorldResourceOperationControl operation,
        T[] partitions,
        WorldResourceCaptureData<T> data,
        scoped Span<byte> destination,
        CancellationToken cancellationToken);
}
