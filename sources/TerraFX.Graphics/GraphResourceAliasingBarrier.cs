// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal readonly struct GraphResourceAliasingBarrier
{
    internal GraphResourceAliasingBarrier(
        RenderPassId passId,
        GraphBufferVersion beforeVersion,
        GraphBufferVersion afterVersion)
    {
        ArgumentNullException.ThrowIfNull(beforeVersion);
        ArgumentNullException.ThrowIfNull(afterVersion);

        if (!passId.IsValid)
        {
            throw new ArgumentException("The graph pass ID must be valid.", nameof(passId));
        }

        if (beforeVersion.DeviceEpoch != afterVersion.DeviceEpoch)
        {
            throw new ArgumentException("Aliased graph buffer versions must belong to one device epoch.", nameof(afterVersion));
        }

        if (beforeVersion.ResourceId == afterVersion.ResourceId)
        {
            throw new ArgumentException("An aliasing barrier requires distinct graph resource identities.", nameof(afterVersion));
        }

        if ((beforeVersion.Description.MemoryKind != GpuMemoryKind.DeviceLocal)
            || (beforeVersion.Description.ByteLength != afterVersion.Description.ByteLength)
            || (beforeVersion.Description.Usage != afterVersion.Description.Usage)
            || (beforeVersion.Description.StructuredStride != afterVersion.Description.StructuredStride)
            || (afterVersion.Description.MemoryKind != GpuMemoryKind.DeviceLocal))
        {
            throw new ArgumentException("Aliased transient buffers must have identical device-local allocation requirements.", nameof(afterVersion));
        }

        PassId = passId;
        BeforeResourceId = beforeVersion.ResourceId;
        AfterResourceId = afterVersion.ResourceId;
        DeviceEpoch = afterVersion.DeviceEpoch;
    }

    internal ulong AfterResourceId { get; }

    internal ulong BeforeResourceId { get; }

    internal uint DeviceEpoch { get; }

    internal RenderPassId PassId { get; }
}
