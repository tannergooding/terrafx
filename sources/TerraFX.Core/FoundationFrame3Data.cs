// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX.Numerics;

namespace TerraFX;

internal sealed class FoundationFrame3Data
{
    internal FoundationFrame3Data(EbinRecord admitted, FoundationPersistenceBinding binding)
    {
        Record = admitted;
        WorldGuid = binding.WorldGuid;
        Profile = binding.Profile;
        var origin = admitted.GetField(3).Payload;
        Origin = new SpatialPosition3(new ChunkIndex3(BinaryPrimitives.ReadInt32LittleEndian(origin), BinaryPrimitives.ReadInt32LittleEndian(origin[4..]), BinaryPrimitives.ReadInt32LittleEndian(origin[8..])), EbinPrimitives.Vector3(origin[12..]));
        Revision = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(admitted.GetField(4).Payload));
        Minimum = EbinPrimitives.Vector3(admitted.GetField(5).Payload);
        Maximum = EbinPrimitives.Vector3(admitted.GetField(6).Payload);
    }

    internal Vector3 Maximum { get; }

    internal Vector3 Minimum { get; }

    internal SpatialPosition3 Origin { get; }

    internal FoundationProfileBinding Profile { get; }

    internal EbinRecord Record { get; }

    internal long Revision { get; }

    internal Guid WorldGuid { get; }

    internal SpatialFrame3 Project(FoundationPersistenceBinding target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.WorldGuid != WorldGuid || target.Profile.Id != Profile.Id || target.Profile.Settings != Profile.Settings)
        {
            ThrowHelper.ThrowInvalidOperationException("The staged frame does not belong to this persistence binding.");
        }

        if (!target.Admits(Minimum, Maximum))
        {
            ThrowHelper.ThrowArgumentException("The target owner's extent policy refuses the staged frame.", nameof(target));
        }

        return new SpatialFrame3(target.OwnerId, Profile.Id, Profile.Settings, Origin, Revision, Minimum, Maximum);
    }
}
