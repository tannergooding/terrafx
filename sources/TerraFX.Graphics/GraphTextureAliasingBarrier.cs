// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal readonly struct GraphTextureAliasingBarrier
{
    internal GraphTextureAliasingBarrier(
        RenderPassId passId,
        GraphTextureVersion beforeVersion,
        GraphTextureVersion afterVersion)
    {
        ArgumentNullException.ThrowIfNull(beforeVersion);
        ArgumentNullException.ThrowIfNull(afterVersion);

        if (!passId.IsValid)
        {
            throw new ArgumentException("The graph pass ID must be valid.", nameof(passId));
        }

        if (beforeVersion.DeviceEpoch != afterVersion.DeviceEpoch)
        {
            throw new ArgumentException("Aliased graph texture versions must belong to one device epoch.", nameof(afterVersion));
        }

        if (beforeVersion.ResourceId == afterVersion.ResourceId)
        {
            throw new ArgumentException("An aliasing barrier requires distinct graph resource identities.", nameof(afterVersion));
        }

        if (!AreCompatible(beforeVersion.Description, afterVersion.Description))
        {
            throw new ArgumentException("Aliased transient textures must have identical allocation requirements.", nameof(afterVersion));
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

    internal static bool AreCompatible(TextureDescription left, TextureDescription right)
        => (left.Dimension == right.Dimension)
            && (left.Width == right.Width)
            && (left.Height == right.Height)
            && (left.Depth == right.Depth)
            && (left.ArrayLayers == right.ArrayLayers)
            && (left.MipLevels == right.MipLevels)
            && (left.Format == right.Format)
            && (left.Usage == right.Usage);
}
