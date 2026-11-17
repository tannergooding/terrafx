// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal interface IWorldResourcePartitionRecordsValidator<T, TAuthor>
    where T : unmanaged
    where TAuthor : notnull
{
    bool IsValidAuthor(in TAuthor value);

    bool IsValidRuntime(scoped ReadOnlySpan<T> partitions);
}
