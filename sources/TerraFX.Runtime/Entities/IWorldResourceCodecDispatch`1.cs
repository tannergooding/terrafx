// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

internal interface IWorldResourceCodecDispatch<T>
    where T : unmanaged
{
    Type SourceAuthorType { get; }

    nint Count(
        WorldResourceOperationControl operation,
        T[] partitions,
        WorldResourceCaptureData<T> data,
        CancellationToken cancellationToken);

    long GetOperationByteReservation(WorldResourceOperationKind operation);

    WorldResourceStateCodecReadOutcome Read(
        scoped ReadOnlySpan<byte> source,
        Guid resourceId,
        ContentSchemaReference schema,
        int partitionCount,
        CancellationToken cancellationToken);

    WorldResourceStateCodecReadOutcome Read(
        scoped ReadOnlySpan<byte> source,
        Guid resourceId,
        ContentSchemaReference schema,
        int partitionCount,
        WorldResourceOperationControl? operation,
        CancellationToken cancellationToken);

    void Restore(
        object author,
        WorldResourceOperationControl operation,
        T[] partitions,
        CancellationToken cancellationToken);

    void RestorePreparedSource(
        IWorldPreparedResourceSource source,
        WorldResourceOperationControl operation,
        T[] partitions,
        CancellationToken cancellationToken);

    bool TryReadSource(
        IWorldResourceRegistration registration,
        IWorldRestoreSourceOwner owner,
        out IWorldPreparedResourceSource? source,
        out ImmutableArray<Diagnostic> diagnostics,
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
