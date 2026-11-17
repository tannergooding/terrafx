// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal interface IWorldPreparedResourceSource : IDisposable
{
    int PartitionCount { get; }

    Guid ResourceId { get; }

    ContentSchemaReference Schema { get; }
}
