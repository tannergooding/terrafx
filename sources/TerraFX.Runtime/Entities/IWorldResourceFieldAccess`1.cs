// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal interface IWorldResourceFieldAccess<T>
    where T : unmanaged
{
    WorldResourceFieldDescriptor Descriptor { get; }

    Type ElementType { get; }

    int GetAuthorCount(object author);

    object ReadAuthorElement(object author, int index);

    object ReadPartition(in T partition);

    void ValidateAuthorElement(object author, int index);

    void ValidatePartition(in T partition);
}
