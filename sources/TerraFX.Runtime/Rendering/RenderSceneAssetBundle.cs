// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Rendering;

internal sealed class RenderSceneAssetBundle : IDisposable
{
    private static readonly Guid s_spriteSchemaId = new ContentSchemaIdentityAssignment("render/sprite").SchemaId;

    private readonly AtlasEntry[] _atlasEntries;
    private int _isDisposed;
    private readonly MaterialEntry[] _materialEntries;
    private readonly SpriteBinding[] _spriteBindings;
    private WorldSceneStorageReservation? _storageReservation;

    private RenderSceneAssetBundle(
        SpriteBinding[] spriteBindings,
        AtlasEntry[] atlasEntries,
        MaterialEntry[] materialEntries,
        WorldSceneStorageReservation storageReservation)
    {
        ArgumentNullException.ThrowIfNull(spriteBindings);
        ArgumentNullException.ThrowIfNull(atlasEntries);
        ArgumentNullException.ThrowIfNull(materialEntries);
        ArgumentNullException.ThrowIfNull(storageReservation);

        if (spriteBindings.Length == 0)
        {
            throw new ArgumentException("A Sprite asset bundle requires at least one prepared Sprite.", nameof(spriteBindings));
        }

        _spriteBindings = spriteBindings;
        _atlasEntries = atlasEntries;
        _materialEntries = materialEntries;
        _storageReservation = storageReservation;
    }

    internal static async Task<RenderSceneAssetBundle?> PrepareAsync(
        SceneExpansionPlan plan,
        Func<long, WorldSceneStorageReservation?> reserveStorage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(reserveStorage);
        cancellationToken.ThrowIfCancellationRequested();
        var spriteCount = CountSprites(plan);

        if (spriteCount == 0)
        {
            return null;
        }

        var scratchReservation = reserveStorage(GetScratchByteReservation(spriteCount))
            ?? throw new InvalidOperationException("The World cannot reserve Sprite asset-preparation scratch storage.");
        WorldSceneStorageReservation? finalReservation = null;
        AtlasEntry[]? atlasEntries = null;
        var loadedAtlasCount = 0;
        MaterialEntry[]? materialEntries = null;
        var loadedMaterialCount = 0;

        try
        {
            var atlasIds = new int[spriteCount];
            var materialIds = new int[spriteCount];
            var materialIdCount = 0;
            FillAssetIds(plan, atlasIds, materialIds, ref materialIdCount, cancellationToken);
            Array.Sort(atlasIds);
            Array.Sort(materialIds, 0, materialIdCount);
            var atlasCount = CompactDistinct(atlasIds, atlasIds.Length);
            var materialCount = CompactDistinct(materialIds, materialIdCount);
            finalReservation = reserveStorage(GetRequiredByteReservation(
                spriteCount,
                atlasCount,
                materialCount))
                ?? throw new InvalidOperationException("The World cannot reserve retained Sprite asset-bundle storage.");
            var spriteBindings = new SpriteBinding[spriteCount];
            atlasEntries = new AtlasEntry[atlasCount];
            materialEntries = new MaterialEntry[materialCount];
            FillSpriteBindings(plan, spriteBindings, cancellationToken);

            for (var index = 0; index < atlasCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assetId = new AssetId(atlasIds[index]);
                var atlasLease = await plan.LoadFromPackageAsync<RenderAtlasAsset>(
                    assetId,
                    cancellationToken).ConfigureAwait(false);

                try
                {
                    var pageSet = (RenderAtlasPageSet?)await RenderAtlasPageLoader.RetainAsync(
                            atlasLease,
                            cancellationToken)
                        .ConfigureAwait(false);

                    try
                    {
                        var ownedPageSet = pageSet
                            ?? throw new InvalidOperationException("The Atlas page loader returned no retained page set.");
                        atlasEntries[index] = new AtlasEntry(assetId.Value, ownedPageSet);
                        loadedAtlasCount++;
                        pageSet = null;
                    }
                    finally
                    {
                        pageSet?.Dispose();
                    }
                }
                finally
                {
                    atlasLease.Dispose();
                }
            }

            for (var index = 0; index < materialCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assetId = new AssetId(materialIds[index]);
                var materialLease = await plan.LoadFromPackageAsync<RenderMaterialAsset>(
                    assetId,
                    cancellationToken).ConfigureAwait(false);

                try
                {
                    var material = materialLease.Value;

                    if (!material.IsSprite)
                    {
                        throw new InvalidDataException("A render/sprite Material must select the exact Sprite profile.");
                    }

                    if (material.Parameters.SpriteTexture is { } texture
                        && (texture.Sampler.Mip != 0))
                    {
                        throw new InvalidDataException("An Atlas-page Sprite Material cannot sample unavailable mip levels.");
                    }

                    var retainedMaterialLease = (AssetLease<RenderMaterialAsset>?)materialLease.Retain();

                    try
                    {
                        var ownedMaterialLease = retainedMaterialLease
                            ?? throw new InvalidOperationException("The Material lease could not be retained.");
                        materialEntries[index] = new MaterialEntry(assetId.Value, ownedMaterialLease);
                        loadedMaterialCount++;
                        retainedMaterialLease = null;
                    }
                    finally
                    {
                        retainedMaterialLease?.Dispose();
                    }
                }
                finally
                {
                    materialLease.Dispose();
                }
            }

            ValidateSpriteBindings(spriteBindings, atlasEntries, materialEntries);
            var bundle = new RenderSceneAssetBundle(
                spriteBindings,
                atlasEntries,
                materialEntries,
                finalReservation);
            finalReservation = null;
            atlasEntries = null;
            materialEntries = null;
            loadedAtlasCount = 0;
            loadedMaterialCount = 0;
            return bundle;
        }
        catch
        {
            if (atlasEntries is not null)
            {
                for (var index = 0; index < loadedAtlasCount; index++)
                {
                    atlasEntries[index].PageSet.Dispose();
                }
            }

            if (materialEntries is not null)
            {
                for (var index = 0; index < loadedMaterialCount; index++)
                {
                    materialEntries[index].Lease.Dispose();
                }
            }

            throw;
        }
        finally
        {
            finalReservation?.Dispose();
            scratchReservation.Dispose();
        }
    }

    private static long ArrayBytes(int count, int elementSize)
    {
        var payloadBytes = checked((long)count * elementSize);
        return checked(64L + ((payloadBytes + 7L) & ~7L));
    }

    private static int CompactDistinct(int[] values, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        var writeIndex = 1;
        var previous = values[0];

        for (var readIndex = 1; readIndex < count; readIndex++)
        {
            var value = values[readIndex];

            if (value == previous)
            {
                continue;
            }

            values[writeIndex++] = value;
            previous = value;
        }

        return writeIndex;
    }

    private static int CountSprites(SceneExpansionPlan plan)
    {
        var spriteCount = 0;

        foreach (var entity in plan.Entities)
        {
            foreach (var component in entity.Components)
            {
                if (component.Schema.SchemaId == s_spriteSchemaId)
                {
                    spriteCount = checked(spriteCount + 1);
                }
            }
        }

        return spriteCount;
    }

    private static void FillAssetIds(
        SceneExpansionPlan plan,
        int[] atlasIds,
        int[] materialIds,
        ref int materialIdCount,
        CancellationToken cancellationToken)
    {
        var spriteIndex = 0;

        foreach (var entity in plan.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var component in entity.Components)
            {
                if (component.Schema.SchemaId != s_spriteSchemaId)
                {
                    continue;
                }

                var references = RenderComponentDefinitions.GetSpriteAssetReferences(component.EncodedRecord);
                atlasIds[spriteIndex++] = references.AtlasAssetId;

                if (references.MaterialAssetId is int materialAssetId)
                {
                    materialIds[materialIdCount++] = materialAssetId;
                }
            }
        }

        if (spriteIndex != atlasIds.Length)
        {
            throw new InvalidOperationException("The expanded Sprite count changed during package preparation.");
        }
    }

    private static void FillSpriteBindings(
        SceneExpansionPlan plan,
        SpriteBinding[] spriteBindings,
        CancellationToken cancellationToken)
    {
        var spriteIndex = 0;

        for (var entityIndex = 0; entityIndex < plan.Entities.Length; entityIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entity = plan.Entities[entityIndex];

            foreach (var component in entity.Components)
            {
                if (component.Schema.SchemaId != s_spriteSchemaId)
                {
                    continue;
                }

                var references = RenderComponentDefinitions.GetSpriteAssetReferences(component.EncodedRecord);
                spriteBindings[spriteIndex++] = new SpriteBinding(
                    entityIndex,
                    references.AtlasAssetId,
                    references.FrameId,
                    references.MaterialAssetId);
            }
        }

        if (spriteIndex != spriteBindings.Length)
        {
            throw new InvalidOperationException("The expanded Sprite count changed while filling its asset bundle.");
        }
    }

    private static AtlasEntry FindAtlasEntry(int atlasAssetId, AtlasEntry[] atlasEntries)
    {
        var low = 0;
        var high = atlasEntries.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = atlasEntries[middle].AssetId.CompareTo(atlasAssetId);

            if (comparison == 0)
            {
                return atlasEntries[middle];
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        throw new InvalidOperationException("A prepared Sprite Atlas is absent from its bundle.");
    }

    private static RenderAtlasFrame FindFrame(ImmutableArray<RenderAtlasFrame> frames, Guid frameId)
    {
        var low = 0;
        var high = frames.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = FoundationCatalogue.CompareRfc(frames[middle].FrameId, frameId);

            if (comparison == 0)
            {
                return frames[middle];
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        throw new InvalidDataException("A render/sprite component names a FrameId absent from its selected Atlas.");
    }

    private static MaterialEntry FindMaterialEntry(int materialAssetId, MaterialEntry[] materialEntries)
    {
        var low = 0;
        var high = materialEntries.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = materialEntries[middle].AssetId.CompareTo(materialAssetId);

            if (comparison == 0)
            {
                return materialEntries[middle];
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        throw new InvalidOperationException("A prepared Sprite Material is absent from its bundle.");
    }

    private static long GetRequiredByteReservation(int spriteCount, int atlasCount, int materialCount)
        => checked(
            256L
            + ArrayBytes(spriteCount, Unsafe.SizeOf<SpriteBinding>())
            + ArrayBytes(atlasCount, Unsafe.SizeOf<AtlasEntry>())
            + ArrayBytes(materialCount, Unsafe.SizeOf<MaterialEntry>()));

    private static long GetScratchByteReservation(int spriteCount)
        => checked(128L + (2L * ArrayBytes(spriteCount, sizeof(int))));

    private static void ValidateSpriteBindings(
        SpriteBinding[] spriteBindings,
        AtlasEntry[] atlasEntries,
        MaterialEntry[] materialEntries)
    {
        foreach (var sprite in spriteBindings)
        {
            var atlasEntry = FindAtlasEntry(sprite.AtlasAssetId, atlasEntries);
            var frame = FindFrame(atlasEntry.PageSet.Atlas.Frames, sprite.FrameId);

            if (sprite.MaterialAssetId is not int materialAssetId)
            {
                continue;
            }

            var material = FindMaterialEntry(materialAssetId, materialEntries).Lease.Value;

            if (!material.IsSprite)
            {
                throw new InvalidDataException("A render/sprite Material must select the exact Sprite profile.");
            }

            if (material.Parameters.SpriteTexture is { } texture
                && (texture.TextureAssetId.Value != atlasEntry.PageSet.Atlas.Pages[frame.Page].Value))
            {
                throw new InvalidDataException("A Sprite Material texture must be the exact Atlas page selected by its FrameId.");
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            foreach (var atlasEntry in _atlasEntries)
            {
                atlasEntry.PageSet.Dispose();
            }
        }
        finally
        {
            try
            {
                foreach (var materialEntry in _materialEntries)
                {
                    materialEntry.Lease.Dispose();
                }
            }
            finally
            {
                Interlocked.Exchange(ref _storageReservation, null)?.Dispose();
            }
        }
    }

    internal RenderAtlasPageSet GetAtlasPageSet(int atlasAssetId)
    {
        ThrowIfDisposed();
        return FindAtlasEntry(atlasAssetId, _atlasEntries).PageSet;
    }

    internal RenderMaterialAsset GetMaterial(int materialAssetId)
    {
        ThrowIfDisposed();
        return FindMaterialEntry(materialAssetId, _materialEntries).Lease.Value;
    }

    internal bool TryGetSpriteBinding(int expandedEntityIndex, out SpriteBinding spriteBinding)
    {
        ThrowIfDisposed();
        var low = 0;
        var high = _spriteBindings.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = _spriteBindings[middle].ExpandedEntityIndex.CompareTo(expandedEntityIndex);

            if (comparison == 0)
            {
                spriteBinding = _spriteBindings[middle];
                return true;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        spriteBinding = default;
        return false;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

    internal readonly record struct SpriteBinding(
        int ExpandedEntityIndex,
        int AtlasAssetId,
        Guid FrameId,
        int? MaterialAssetId);

    private readonly record struct AtlasEntry(int AssetId, RenderAtlasPageSet PageSet);

    private readonly record struct MaterialEntry(int AssetId, AssetLease<RenderMaterialAsset> Lease);
}
