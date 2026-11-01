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
    private WorldSceneStorageReservation? _storageReservation;

    private RenderSceneAssetBundle(
        AtlasEntry[] atlasEntries,
        MaterialEntry[] materialEntries,
        WorldSceneStorageReservation storageReservation)
    {
        ArgumentNullException.ThrowIfNull(atlasEntries);
        ArgumentNullException.ThrowIfNull(materialEntries);
        ArgumentNullException.ThrowIfNull(storageReservation);

        if (atlasEntries.Length == 0)
        {
            throw new ArgumentException("A Sprite asset bundle requires at least one prepared Atlas.", nameof(atlasEntries));
        }

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
            finalReservation = reserveStorage(GetRequiredByteReservation(atlasCount, materialCount))
                ?? throw new InvalidOperationException("The World cannot reserve retained Sprite asset-bundle storage.");
            atlasEntries = new AtlasEntry[atlasCount];
            materialEntries = new MaterialEntry[materialCount];

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

            ValidateSpriteBindings(plan, atlasEntries, materialEntries, cancellationToken);
            var bundle = new RenderSceneAssetBundle(
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

    private static long GetRequiredByteReservation(int atlasCount, int materialCount)
        => checked(
            256L
            + ArrayBytes(atlasCount, Unsafe.SizeOf<AtlasEntry>())
            + ArrayBytes(materialCount, Unsafe.SizeOf<MaterialEntry>()));

    private static long GetScratchByteReservation(int spriteCount)
        => checked(128L + (2L * ArrayBytes(spriteCount, sizeof(int))));

    private static void ValidateSpriteBindings(
        SceneExpansionPlan plan,
        AtlasEntry[] atlasEntries,
        MaterialEntry[] materialEntries,
        CancellationToken cancellationToken)
    {
        foreach (var entity in plan.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var component in entity.Components)
            {
                if (component.Schema.SchemaId != s_spriteSchemaId)
                {
                    continue;
                }

                var sprite = RenderComponentDefinitions.GetSpriteAssetReferences(component.EncodedRecord);
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

    internal RenderSnapshotAssetPins.SceneAssetPins RetainForSnapshot(Guid sceneInstanceId)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);
        var atlasPages = ImmutableArray.CreateBuilder<RenderSnapshotAssetPins.SceneAssetPins.AtlasPin>(_atlasEntries.Length);
        var materials = ImmutableArray.CreateBuilder<RenderSnapshotAssetPins.SceneAssetPins.MaterialPin>(_materialEntries.Length);

        try
        {
            foreach (var atlasEntry in _atlasEntries)
            {
                var retainedPageSet = (RenderAtlasPageSet?)atlasEntry.PageSet.Retain();

                try
                {
                    var pageSet = retainedPageSet
                        ?? throw new InvalidOperationException("The Atlas page set could not be retained.");
                    atlasPages.Add(new RenderSnapshotAssetPins.SceneAssetPins.AtlasPin(atlasEntry.AssetId, pageSet));
                    retainedPageSet = null;
                }
                finally
                {
                    retainedPageSet?.Dispose();
                }
            }

            foreach (var materialEntry in _materialEntries)
            {
                var retainedMaterial = (AssetLease<RenderMaterialAsset>?)materialEntry.Lease.Retain();

                try
                {
                    var material = retainedMaterial
                        ?? throw new InvalidOperationException("The Material lease could not be retained.");
                    materials.Add(new RenderSnapshotAssetPins.SceneAssetPins.MaterialPin(materialEntry.AssetId, material));
                    retainedMaterial = null;
                }
                finally
                {
                    retainedMaterial?.Dispose();
                }
            }

            return new RenderSnapshotAssetPins.SceneAssetPins(
                sceneInstanceId,
                atlasPages.MoveToImmutable(),
                materials.MoveToImmutable());
        }
        catch
        {
            foreach (var atlasPage in atlasPages)
            {
                atlasPage.PageSet.Dispose();
            }

            foreach (var material in materials)
            {
                material.Lease.Dispose();
            }

            throw;
        }
    }

    internal void ValidateSpriteReferences(int atlasAssetId, Guid frameId, int? materialAssetId)
    {
        ThrowIfDisposed();
        var atlasEntry = FindAtlasEntry(atlasAssetId, _atlasEntries);
        var frame = FindFrame(atlasEntry.PageSet.Atlas.Frames, frameId);

        if (materialAssetId is not int selectedMaterialId)
        {
            return;
        }

        var material = FindMaterialEntry(selectedMaterialId, _materialEntries).Lease.Value;

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

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

    private readonly record struct AtlasEntry(int AssetId, RenderAtlasPageSet PageSet);

    private readonly record struct MaterialEntry(int AssetId, AssetLease<RenderMaterialAsset> Lease);
}
