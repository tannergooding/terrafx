// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal interface IWorldResourceRegistration
{
    bool AllowParallelAccess { get; }

    SystemResourceDescription Description { get; }

    WorldResourceRegistrationDescriptor Descriptor { get; }

    int PartitionCount { get; }

    Guid ResourceId { get; }

    Type ResourceType { get; }

    object CreateStorage();

    long GetStorageByteReservation();

    void InitializeAndValidate(object storage);

    void ValidateForExecution(object storage);
}
