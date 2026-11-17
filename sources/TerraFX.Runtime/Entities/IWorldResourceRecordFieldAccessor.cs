// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal interface IWorldResourceRecordFieldAccessor<T, TAuthor>
    where T : unmanaged
    where TAuthor : notnull
{
    void ValidateAuthor(in TAuthor value);

    void ValidateRuntime(in T value);
}
