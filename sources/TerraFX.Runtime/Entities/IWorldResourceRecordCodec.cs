// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

internal interface IWorldResourceRecordCodec<T, TAuthor> : IWorldResourceStateCodec<T, TAuthor>
    where T : unmanaged
    where TAuthor : notnull
{
    long GetOperationByteReservation(WorldResourceOperationKind operation);

    ImmutableArray<Diagnostic> ValidateSourceReferences(
        in TAuthor author,
        WorldResourceReferenceBinding binding,
        CancellationToken cancellationToken);
}
