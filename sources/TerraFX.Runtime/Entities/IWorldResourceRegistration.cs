// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

internal interface IWorldResourceRegistration
{
    bool AllowParallelAccess { get; }

    SystemResourceDescription Description { get; }

    WorldResourceRegistrationDescriptor Descriptor { get; }

    bool IsAggregate { get; }

    int PartitionCount { get; }

    Guid ResourceId { get; }

    Type ResourceType { get; }

    ContentSchemaReference Schema { get; }

    Type SourceAuthorType { get; }

    nint CountStateRecord(object storage, CancellationToken cancellationToken, World? world = null, long? completedTick = null);

    object CreateStorage();

    long GetStorageByteReservation();

    void InitializeAndValidate(object storage, World? world = null);

    void RestorePreparedSource(
        IWorldPreparedResourceSource source,
        object storage,
        World candidate,
        long completedTick,
        CancellationToken cancellationToken);

    void RestoreStateRecord(
        scoped ReadOnlySpan<byte> source,
        object storage,
        CancellationToken cancellationToken,
        World? world = null,
        long? completedTick = null);

    bool TryReadSource(
        IWorldRestoreSourceOwner owner,
        out IWorldPreparedResourceSource? source,
        out ImmutableArray<Diagnostic> diagnostics,
        CancellationToken cancellationToken);

    void ValidateForExecution(object storage, World? world = null, long? requiredRequestTick = null);

    nint WriteStateRecord(object storage, scoped Span<byte> destination, CancellationToken cancellationToken, World? world = null, long? completedTick = null);
}
