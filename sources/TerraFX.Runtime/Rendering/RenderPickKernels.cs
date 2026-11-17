// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal static class RenderPickKernels
{
    private const int MaximumDrawCount = 131_072;

    internal static int BuildMembership(
        Guid worldGuid,
        ReadOnlySpan<DrawIdentity> meshes,
        ReadOnlySpan<DrawIdentity> sprites,
        Span<DrawIdentity> table,
        Span<uint> meshIds,
        Span<uint> spriteIds)
    {
        if (worldGuid == Guid.Empty)
        {
            throw new ArgumentException("Pick membership requires a nonempty source World identity.", nameof(worldGuid));
        }

        var (drawCount, _) = GetOutputRequirements(meshes.Length, sprites.Length);

        if (table.Length < drawCount)
        {
            throw new ArgumentException("The pick table cannot hold the complete membership.", nameof(table));
        }

        if (meshIds.Length < meshes.Length)
        {
            throw new ArgumentException("The Mesh ID map cannot hold every source draw.", nameof(meshIds));
        }

        if (spriteIds.Length < sprites.Length)
        {
            throw new ArgumentException("The Sprite ID map cannot hold every source draw.", nameof(spriteIds));
        }

        ValidateMembers(worldGuid, meshes, nameof(meshes));
        ValidateMembers(worldGuid, sprites, nameof(sprites));
        ValidateDistinctDrawIds(meshes, sprites);
        ValidateStorage(
            meshes,
            sprites,
            table[..drawCount],
            meshIds[..meshes.Length],
            spriteIds[..sprites.Length]);

        var meshIndex = 0;
        var spriteIndex = 0;

        for (var tableIndex = 0; tableIndex < drawCount; tableIndex++)
        {
            var denseId = checked((uint)(tableIndex + 1));
            var takeMesh = (spriteIndex == sprites.Length)
                || ((meshIndex < meshes.Length)
                    && (FoundationCatalogue.CompareRfc(meshes[meshIndex].DrawId, sprites[spriteIndex].DrawId) < 0));

            if (takeMesh)
            {
                table[tableIndex] = meshes[meshIndex];
                meshIds[meshIndex++] = denseId;
            }
            else
            {
                table[tableIndex] = sprites[spriteIndex];
                spriteIds[spriteIndex++] = denseId;
            }
        }

        return drawCount;
    }

    internal static (int DrawCount, long ElementBytes) GetOutputRequirements(int meshCount, int spriteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(meshCount);
        ArgumentOutOfRangeException.ThrowIfNegative(spriteCount);
        var drawCount = checked(meshCount + spriteCount);

        if (drawCount > MaximumDrawCount)
        {
            throw new InvalidOperationException($"Pick membership requires {drawCount} draws, exceeding the {MaximumDrawCount} limit.");
        }

        // Span elements only: this is not an array-header or complete operation reservation.
        var elementBytes = checked(
            ((long)drawCount * Unsafe.SizeOf<DrawIdentity>())
            + ((long)drawCount * sizeof(uint)));
        return (drawCount, elementBytes);
    }

    internal static bool TryLookup(ReadOnlySpan<DrawIdentity> table, uint denseId, out DrawIdentity draw)
    {
        if (denseId == 0)
        {
            draw = default;
            return false;
        }

        if (denseId > table.Length)
        {
            throw new InvalidDataException("The nonzero pick ID is outside the retained membership table.");
        }

        // The caller supplies the validated written table prefix, not its spare capacity.
        draw = table[checked((int)denseId) - 1];
        return true;
    }

    internal static RenderArea ValidatePixel(Vector4 viewport, PixelSize targetExtent, int pixelX, int pixelY)
    {
        var area = RenderCameraViewport.CreateArea(viewport, targetExtent);

        if ((pixelX < area.X) || (pixelX >= (long)area.X + area.Width))
        {
            throw new ArgumentOutOfRangeException(nameof(pixelX));
        }

        if ((pixelY < area.Y) || (pixelY >= (long)area.Y + area.Height))
        {
            throw new ArgumentOutOfRangeException(nameof(pixelY));
        }

        return area;
    }

    private static void ValidateDistinctDrawIds(ReadOnlySpan<DrawIdentity> meshes, ReadOnlySpan<DrawIdentity> sprites)
    {
        var meshIndex = 0;
        var spriteIndex = 0;

        while ((meshIndex < meshes.Length) && (spriteIndex < sprites.Length))
        {
            var comparison = FoundationCatalogue.CompareRfc(meshes[meshIndex].DrawId, sprites[spriteIndex].DrawId);

            if (comparison == 0)
            {
                throw new ArgumentException("A DrawId occurs in both pick membership inputs.", nameof(sprites));
            }

            if (comparison < 0)
            {
                meshIndex++;
            }
            else
            {
                spriteIndex++;
            }
        }
    }

    private static void ValidateMembers(Guid worldGuid, ReadOnlySpan<DrawIdentity> draws, string parameterName)
    {
        for (var index = 0; index < draws.Length; index++)
        {
            var draw = draws[index];

            if (!draw.Identity.IsValid || (draw.Identity.WorldGuid != worldGuid) || (draw.DrawId == Guid.Empty))
            {
                throw new ArgumentException("Every pick draw requires a qualified source-World identity and a nonempty DrawId.", parameterName);
            }

            if ((index > 0) && (FoundationCatalogue.CompareRfc(draws[index - 1].DrawId, draw.DrawId) >= 0))
            {
                throw new ArgumentException("Pick membership inputs must be strictly RFC-DrawId sorted.", parameterName);
            }
        }
    }

    private static void ValidateStorage(
        ReadOnlySpan<DrawIdentity> meshes,
        ReadOnlySpan<DrawIdentity> sprites,
        Span<DrawIdentity> table,
        Span<uint> meshIds,
        Span<uint> spriteIds)
    {
        var meshBytes = MemoryMarshal.AsBytes(meshes);
        var spriteBytes = MemoryMarshal.AsBytes(sprites);
        ReadOnlySpan<byte> tableBytes = MemoryMarshal.AsBytes(table);
        ReadOnlySpan<byte> meshMapBytes = MemoryMarshal.AsBytes(meshIds);
        ReadOnlySpan<byte> spriteMapBytes = MemoryMarshal.AsBytes(spriteIds);

        if (tableBytes.Overlaps(meshBytes)
            || tableBytes.Overlaps(spriteBytes)
            || tableBytes.Overlaps(meshMapBytes)
            || tableBytes.Overlaps(spriteMapBytes)
            || meshMapBytes.Overlaps(meshBytes)
            || meshMapBytes.Overlaps(spriteBytes)
            || meshMapBytes.Overlaps(spriteMapBytes)
            || spriteMapBytes.Overlaps(meshBytes)
            || spriteMapBytes.Overlaps(spriteBytes))
        {
            throw new ArgumentException("Pick table and ID-map output ranges require independent storage.");
        }
    }

    internal readonly record struct DrawIdentity(WorldEntityIdentity Identity, Guid DrawId);
}
