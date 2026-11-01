// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal static class RenderExtractionValidation
{
    private const int MaximumCameraCount = 16;

    internal static ImmutableArray<Guid> CopyCameraIds(ImmutableArray<Guid> cameraIds, string parameterName)
    {
        if (cameraIds.IsDefault)
        {
            throw new ArgumentException("The camera ID array must be initialized.", parameterName);
        }

        ArgumentOutOfRangeException.ThrowIfZero(cameraIds.Length, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cameraIds.Length, MaximumCameraCount, parameterName);

        var uniqueIds = new HashSet<Guid>();

        foreach (var cameraId in cameraIds)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(cameraId, Guid.Empty, parameterName);

            if (!uniqueIds.Add(cameraId))
            {
                throw new ArgumentException("The camera ID array cannot contain duplicates.", parameterName);
            }
        }

        return [.. cameraIds];
    }

    internal static bool MatchesFrame(SpatialFrame2 left, SpatialFrame2 right)
        => (left.OwnerId == right.OwnerId)
        && (left.ProfileId == right.ProfileId)
        && left.Settings.Equals(right.Settings)
        && (left.Revision == right.Revision)
        && (left.Origin.Chunk.X == right.Origin.Chunk.X)
        && (left.Origin.Chunk.Y == right.Origin.Chunk.Y)
        && left.Origin.Offset.Equals(right.Origin.Offset)
        && left.Minimum.Equals(right.Minimum)
        && left.Maximum.Equals(right.Maximum);

    internal static bool MatchesFrame(SpatialFrame3 left, SpatialFrame3 right)
        => (left.OwnerId == right.OwnerId)
        && (left.ProfileId == right.ProfileId)
        && left.Settings.Equals(right.Settings)
        && (left.Revision == right.Revision)
        && (left.Origin.Chunk.X == right.Origin.Chunk.X)
        && (left.Origin.Chunk.Y == right.Origin.Chunk.Y)
        && (left.Origin.Chunk.Z == right.Origin.Chunk.Z)
        && left.Origin.Offset.Equals(right.Origin.Offset)
        && left.Minimum.Equals(right.Minimum)
        && left.Maximum.Equals(right.Maximum);

    internal static void ValidateFrame(SpatialFrame2 frame, string parameterName)
    {
        if ((frame.OwnerId == 0) || (frame.Revision <= 0))
        {
            throw new ArgumentException("The selected 2D frame must be a registered World frame.", parameterName);
        }
    }

    internal static void ValidateFrame(SpatialFrame3 frame, string parameterName)
    {
        if ((frame.OwnerId == 0) || (frame.Revision <= 0))
        {
            throw new ArgumentException("The selected 3D frame must be a registered World frame.", parameterName);
        }
    }

    internal static void ValidateTargetExtent(PixelSize targetExtent, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfZero(targetExtent.Width, parameterName);
        ArgumentOutOfRangeException.ThrowIfZero(targetExtent.Height, parameterName);
    }
}
