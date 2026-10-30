// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal interface IWorldResourcePartitionAccess<T>
    where T : unmanaged
{
    T Read(int partitionIndex);

    void Write(int partitionIndex, in T value);
}
