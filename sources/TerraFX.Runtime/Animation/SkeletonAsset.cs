// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Assets;

namespace TerraFX.Animation;

/// <summary>Represents one immutable, package-backed native Skeleton asset.</summary>
public sealed class SkeletonAsset
{
    internal SkeletonAsset(
        AssetId assetId,
        Guid? rootMotionJointId,
        ImmutableArray<SkeletonJoint> joints,
        ImmutableArray<byte> encodedRecord)
    {
        if (!assetId.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(assetId));
        }

        if (joints.IsDefaultOrEmpty || (joints.Length > 256))
        {
            throw new ArgumentOutOfRangeException(nameof(joints));
        }

        if (encodedRecord.IsDefaultOrEmpty)
        {
            throw new ArgumentException("The Skeleton record must be initialized and nonempty.", nameof(encodedRecord));
        }

        AssetId = assetId;
        RootMotionJointId = rootMotionJointId;
        Joints = joints;
        EncodedRecord = encodedRecord;
    }

    internal AssetId AssetId { get; }

    internal ImmutableArray<byte> EncodedRecord { get; }

    internal ImmutableArray<SkeletonJoint> Joints { get; }

    internal Guid? RootMotionJointId { get; }
}
