// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal interface IWorldResourceCaptureData
{
    int GetCount(WorldResourceFieldDescriptor field);

    object GetElement(WorldResourceFieldDescriptor field, int partitionIndex, Type elementType);
}
