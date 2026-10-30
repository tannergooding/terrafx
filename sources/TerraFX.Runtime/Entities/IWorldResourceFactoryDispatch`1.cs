// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal interface IWorldResourceFactoryDispatch<T>
    where T : unmanaged
{
    WorldResourceFactoryDescriptor Descriptor { get; }

    WorldResourceCaptureData<T> CreateCaptureData(T[] partitions);

    T CreateDefault(int partitionIndex, WorldResourceDefaultBinding binding);

    void ValidateAuthor(object author, int partitionCount);

    void ValidateRuntimePartition(in T partition);
}
