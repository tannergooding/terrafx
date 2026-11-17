// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Rendering;

internal sealed class RenderSceneAssetBundle : IDisposable
{
    private static readonly Guid s_spriteSchemaId = new ContentSchemaIdentityAssignment("render/sprite").SchemaId;
    private static readonly Guid s_meshSchemaId = new ContentSchemaIdentityAssignment("render/mesh").SchemaId;

    private readonly AtlasEntry[] _atlasEntries;
    private int _isDisposed;
    private readonly MaterialEntry[] _materialEntries;
    private readonly MeshEntry[] _meshEntries;
    private WorldSceneStorageReservation? _storageReservation;

    private RenderSceneAssetBundle(
        AtlasEntry[] atlasEntries,
        MaterialEntry[] materialEntries,
        MeshEntry[] meshEntries,
        WorldSceneStorageReservation storageReservation)
    {
        ArgumentNullException.ThrowIfNull(atlasEntries);
        ArgumentNullException.ThrowIfNull(materialEntries);
        ArgumentNullException.ThrowIfNull(meshEntries);
        ArgumentNullException.ThrowIfNull(storageReservation);

        if ((atlasEntries.Length == 0) && (meshEntries.Length == 0))
        {
            throw new ArgumentException("A render asset bundle requires a prepared Sprite Atlas or Mesh.", nameof(atlasEntries));
        }

        _atlasEntries = atlasEntries;
        _materialEntries = materialEntries;
        _meshEntries = meshEntries;
        _storageReservation = storageReservation;
    }

    internal static async Task<long> MeasureStorageByteReservationAsync(
        SceneExpansionPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        var spriteCount = CountSprites(plan);
        var meshComponentCount = CountMeshes(plan);

        if ((spriteCount == 0) && (meshComponentCount == 0))
        {
            return 0;
        }

        var atlasIds = new int[spriteCount];
        var spriteMaterialIds = new int[spriteCount];
        var meshIds = new int[meshComponentCount];
        var spriteMaterialIdCount = 0;
        FillAssetIds(
            plan,
            atlasIds,
            spriteMaterialIds,
            ref spriteMaterialIdCount,
            meshIds,
            cancellationToken);
        Array.Sort(atlasIds);
        Array.Sort(spriteMaterialIds, 0, spriteMaterialIdCount);
        Array.Sort(meshIds);
        var atlasCount = CompactDistinct(atlasIds, atlasIds.Length);
        var meshCount = CompactDistinct(meshIds, meshIds.Length);
        var meshMaterialSlots = new ImmutableArray<AssetId>[meshCount];
        var meshMaterialReferenceCount = 0;

        for (var index = 0; index < meshCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var meshLease = await plan.LoadFromPackageAsync<RenderMeshAsset>(
                new AssetId(meshIds[index]),
                cancellationToken).ConfigureAwait(false);
            var materialSlots = meshLease.Value.MaterialSlots;
            meshMaterialSlots[index] = materialSlots;
            meshMaterialReferenceCount = checked(meshMaterialReferenceCount + materialSlots.Length);
        }

        var materialReferenceCount = checked(spriteMaterialIdCount + meshMaterialReferenceCount);
        var materialIds = new int[materialReferenceCount];
        spriteMaterialIds.AsSpan(0, spriteMaterialIdCount).CopyTo(materialIds);
        var materialIdIndex = spriteMaterialIdCount;

        foreach (var materialSlots in meshMaterialSlots)
        {
            foreach (var materialAssetId in materialSlots)
            {
                materialIds[materialIdIndex++] = materialAssetId.Value;
            }
        }

        Array.Sort(materialIds);
        var materialCount = CompactDistinct(materialIds, materialIds.Length);
        return checked(
            GetScratchByteReservation(spriteCount, meshComponentCount)
            + GetMaterialScratchByteReservation(spriteMaterialIdCount, meshMaterialReferenceCount)
            + GetRequiredByteReservation(atlasCount, materialCount, meshCount));
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Prepared Mesh, Material, texture, and page leases transfer together into the Scene bundle and snapshot pin owners; all failure paths dispose untransferred leases.")]
    internal static async Task<RenderSceneAssetBundle?> PrepareAsync(
        SceneExpansionPlan plan,
        Func<long, WorldSceneStorageReservation?> reserveStorage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(reserveStorage);
        cancellationToken.ThrowIfCancellationRequested();
        var spriteCount = CountSprites(plan);
        var meshComponentCount = CountMeshes(plan);

        if ((spriteCount == 0) && (meshComponentCount == 0))
        {
            return null;
        }

        var scratchReservation = reserveStorage(GetScratchByteReservation(spriteCount, meshComponentCount))
            ?? throw new InvalidOperationException("The World cannot reserve render asset-preparation scratch storage.");
        WorldSceneStorageReservation? finalReservation = null;
        WorldSceneStorageReservation? materialScratchReservation = null;
        AtlasEntry[]? atlasEntries = null;
        var loadedAtlasCount = 0;
        MaterialEntry[]? materialEntries = null;
        var loadedMaterialCount = 0;
        MeshEntry[]? meshEntries = null;
        var loadedMeshCount = 0;

        try
        {
            var atlasIds = new int[spriteCount];
            var spriteMaterialIds = new int[spriteCount];
            var meshIds = new int[meshComponentCount];
            var spriteMaterialIdCount = 0;
            FillAssetIds(
                plan,
                atlasIds,
                spriteMaterialIds,
                ref spriteMaterialIdCount,
                meshIds,
                cancellationToken);
            Array.Sort(atlasIds);
            Array.Sort(spriteMaterialIds, 0, spriteMaterialIdCount);
            Array.Sort(meshIds);
            var atlasCount = CompactDistinct(atlasIds, atlasIds.Length);
            var meshCount = CompactDistinct(meshIds, meshIds.Length);
            atlasEntries = new AtlasEntry[atlasCount];
            meshEntries = new MeshEntry[meshCount];

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

            for (var index = 0; index < meshCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assetId = new AssetId(meshIds[index]);
                var meshLease = await plan.LoadFromPackageAsync<RenderMeshAsset>(
                    assetId,
                    cancellationToken).ConfigureAwait(false);
                AssetLease<RenderMeshAsset>? retainedMeshLease = null;
                AssetLease<SkeletonAsset>? skeletonLease = null;

                try
                {
                    skeletonLease = await RenderMeshAssetBinding.LoadSkeletonAsync(
                        meshLease,
                        cancellationToken).ConfigureAwait(false);
                    retainedMeshLease = meshLease.Retain();
                    var ownedMeshLease = retainedMeshLease
                        ?? throw new InvalidOperationException("The Mesh lease could not be retained.");
                    meshEntries[index] = new MeshEntry(assetId.Value, ownedMeshLease, skeletonLease);
                    loadedMeshCount++;
                    retainedMeshLease = null;
                    skeletonLease = null;
                }
                finally
                {
                    retainedMeshLease?.Dispose();
                    skeletonLease?.Dispose();
                    meshLease.Dispose();
                }
            }

            var meshMaterialReferenceCount = 0;

            foreach (var meshEntry in meshEntries)
            {
                meshMaterialReferenceCount = checked(
                    meshMaterialReferenceCount + meshEntry.Mesh.MaterialSlots.Length);
            }

            var totalMaterialReferenceCount = checked(spriteMaterialIdCount + meshMaterialReferenceCount);
            materialScratchReservation = reserveStorage(
                GetMaterialScratchByteReservation(spriteMaterialIdCount, meshMaterialReferenceCount))
                ?? throw new InvalidOperationException("The World cannot reserve Mesh Material-reference scratch storage.");
            var materialIds = new int[totalMaterialReferenceCount];
            spriteMaterialIds.AsSpan(0, spriteMaterialIdCount).CopyTo(materialIds);
            var meshMaterialIds = new int[meshMaterialReferenceCount];
            var materialWriteIndex = 0;

            foreach (var meshEntry in meshEntries)
            {
                foreach (var materialAssetId in meshEntry.Mesh.MaterialSlots)
                {
                    var materialAssetIdValue = materialAssetId.Value;
                    materialIds[spriteMaterialIdCount + materialWriteIndex] = materialAssetIdValue;
                    meshMaterialIds[materialWriteIndex++] = materialAssetIdValue;
                }
            }

            Array.Sort(materialIds);
            Array.Sort(meshMaterialIds);
            var materialCount = CompactDistinct(materialIds, materialIds.Length);
            finalReservation = reserveStorage(GetRequiredByteReservation(atlasCount, materialCount, meshCount))
                ?? throw new InvalidOperationException("The World cannot reserve retained render asset-bundle storage.");
            materialEntries = new MaterialEntry[materialCount];

            for (var index = 0; index < materialCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assetId = new AssetId(materialIds[index]);
                var materialLease = await plan.LoadFromPackageAsync<RenderMaterialAsset>(
                    assetId,
                    cancellationToken).ConfigureAwait(false);
                AssetLease<RenderMaterialAsset>? retainedMaterialLease = null;
                RenderMaterialTextureSet? retainedTextureSet = null;

                try
                {
                    var material = materialLease.Value;
                    var isSpriteMaterial = Array.BinarySearch(
                        spriteMaterialIds,
                        0,
                        spriteMaterialIdCount,
                        assetId.Value) >= 0;
                    var isMeshMaterial = Array.BinarySearch(meshMaterialIds, assetId.Value) >= 0;

                    if (isSpriteMaterial)
                    {
                        if (!material.IsSprite)
                        {
                            throw new InvalidDataException("A render/sprite Material must select the exact Sprite profile.");
                        }

                        if (material.Parameters.SpriteTexture is { } texture
                            && (texture.Sampler.Mip != 0))
                        {
                            throw new InvalidDataException("An Atlas-page Sprite Material cannot sample unavailable mip levels.");
                        }
                    }

                    if (isMeshMaterial)
                    {
                        var meshTextureSet = await RenderMaterialTextureLoader.RetainAsync(
                            materialLease,
                            cancellationToken).ConfigureAwait(false);
                        retainedTextureSet = meshTextureSet;
                    }
                    else
                    {
                        retainedMaterialLease = materialLease.Retain();
                    }

                    materialEntries[index] = new MaterialEntry(
                        assetId.Value,
                        retainedMaterialLease,
                        retainedTextureSet);
                    loadedMaterialCount++;
                    retainedMaterialLease = null;
                    retainedTextureSet = null;
                }
                finally
                {
                    retainedMaterialLease?.Dispose();
                    retainedTextureSet?.Dispose();
                    materialLease.Dispose();
                }
            }

            ValidateMeshBindings(meshEntries, materialEntries);
            ValidateSpriteBindings(plan, atlasEntries, materialEntries, cancellationToken);
            var ownedStorageReservation = finalReservation
                ?? throw new InvalidOperationException("The prepared render bundle has no storage reservation.");
            var bundle = new RenderSceneAssetBundle(
                atlasEntries,
                materialEntries,
                meshEntries,
                ownedStorageReservation);
            finalReservation = null;
            atlasEntries = null;
            materialEntries = null;
            meshEntries = null;
            loadedAtlasCount = 0;
            loadedMaterialCount = 0;
            loadedMeshCount = 0;
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
                    materialEntries[index].Dispose();
                }
            }

            if (meshEntries is not null)
            {
                for (var index = 0; index < loadedMeshCount; index++)
                {
                    meshEntries[index].Dispose();
                }
            }

            throw;
        }
        finally
        {
            finalReservation?.Dispose();
            materialScratchReservation?.Dispose();
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

    private static int CountMeshes(SceneExpansionPlan plan)
    {
        var meshCount = 0;

        foreach (var entity in plan.Entities)
        {
            foreach (var component in entity.Components)
            {
                if (component.Schema.SchemaId == s_meshSchemaId)
                {
                    meshCount = checked(meshCount + 1);
                }
            }
        }

        return meshCount;
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
        int[] spriteMaterialIds,
        ref int spriteMaterialIdCount,
        int[] meshIds,
        CancellationToken cancellationToken)
    {
        var spriteIndex = 0;
        var meshIndex = 0;

        foreach (var entity in plan.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var component in entity.Components)
            {
                if (component.Schema.SchemaId != s_spriteSchemaId)
                {
                    if (component.Schema.SchemaId == s_meshSchemaId)
                    {
                        var mesh = RenderComponentDefinitions.GetMeshAssetReferences(component.EncodedRecord);
                        meshIds[meshIndex++] = mesh.MeshAssetId;

                        if (mesh.PoseMode != 0)
                        {
                            throw new InvalidDataException("Only static BindPose is admitted by the selected Mesh component schema.");
                        }
                    }

                    continue;
                }

                var references = RenderComponentDefinitions.GetSpriteAssetReferences(component.EncodedRecord);
                atlasIds[spriteIndex++] = references.AtlasAssetId;

                if (references.MaterialAssetId is int materialAssetId)
                {
                    spriteMaterialIds[spriteMaterialIdCount++] = materialAssetId;
                }
            }
        }

        if ((spriteIndex != atlasIds.Length) || (meshIndex != meshIds.Length))
        {
            throw new InvalidOperationException("The expanded render component counts changed during package preparation.");
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

    private static MeshEntry FindMeshEntry(int meshAssetId, MeshEntry[] meshEntries)
    {
        var low = 0;
        var high = meshEntries.Length - 1;

        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = meshEntries[middle].AssetId.CompareTo(meshAssetId);

            if (comparison == 0)
            {
                return meshEntries[middle];
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

        throw new InvalidOperationException("A prepared Mesh is absent from its bundle.");
    }

    private static long GetMaterialScratchByteReservation(int spriteMaterialReferenceCount, int meshMaterialReferenceCount)
        => checked(
            64L
            + ArrayBytes(checked(spriteMaterialReferenceCount + meshMaterialReferenceCount), sizeof(int))
            + ArrayBytes(meshMaterialReferenceCount, sizeof(int)));

    private static long GetRequiredByteReservation(int atlasCount, int materialCount, int meshCount)
        => checked(
            256L
            + ArrayBytes(atlasCount, Unsafe.SizeOf<AtlasEntry>())
            + ArrayBytes(materialCount, Unsafe.SizeOf<MaterialEntry>())
            + ArrayBytes(meshCount, Unsafe.SizeOf<MeshEntry>()));

    private static long GetScratchByteReservation(int spriteCount, int meshComponentCount)
        => checked(
            128L
            + (2L * ArrayBytes(spriteCount, sizeof(int)))
            + ArrayBytes(meshComponentCount, sizeof(int)));

    private static void ValidateMeshBindings(MeshEntry[] meshEntries, MaterialEntry[] materialEntries)
    {
        foreach (var meshEntry in meshEntries)
        {
            var mesh = meshEntry.Mesh;

            foreach (var materialAssetId in mesh.MaterialSlots)
            {
                var material = FindMaterialEntry(materialAssetId.Value, materialEntries).Material;
                RenderMeshAssetBinding.ValidateMaterialBindings(mesh, material);
            }
        }
    }

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

                var material = FindMaterialEntry(materialAssetId, materialEntries).Material;

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
                    materialEntry.Dispose();
                }
            }
            finally
            {
                foreach (var meshEntry in _meshEntries)
                {
                    meshEntry.Dispose();
                }
            }
        }

        var storageReservation = Volatile.Read(ref _storageReservation);
        storageReservation?.Dispose();

        if (storageReservation is not null)
        {
            _ = Interlocked.CompareExchange(ref _storageReservation, null, storageReservation);
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
        return FindMaterialEntry(materialAssetId, _materialEntries).Material;
    }

    internal RenderMeshAsset GetMesh(int meshAssetId)
    {
        ThrowIfDisposed();
        return FindMeshEntry(meshAssetId, _meshEntries).Mesh;
    }

    internal SkeletonAsset? GetSkeleton(int meshAssetId)
    {
        ThrowIfDisposed();
        return FindMeshEntry(meshAssetId, _meshEntries).Skeleton;
    }

    internal RenderSnapshotAssetPins.SceneAssetPins RetainForSnapshot(Guid sceneInstanceId)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);
        var atlasPages = ImmutableArray.CreateBuilder<RenderSnapshotAssetPins.SceneAssetPins.AtlasPin>(_atlasEntries.Length);
        var materials = ImmutableArray.CreateBuilder<RenderSnapshotAssetPins.SceneAssetPins.MaterialPin>(_materialEntries.Length);
        var meshes = ImmutableArray.CreateBuilder<RenderSnapshotAssetPins.SceneAssetPins.MeshPin>(_meshEntries.Length);

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
                if (materialEntry.TextureSet is { } sourceTextureSet)
                {
                    RenderMaterialTextureSet? retainedTextureSet = null;

                    try
                    {
                        retainedTextureSet = sourceTextureSet.Retain();
                        var ownedTextureSet = retainedTextureSet
                            ?? throw new InvalidOperationException("The Material texture set could not be retained.");
                        materials.Add(new RenderSnapshotAssetPins.SceneAssetPins.MaterialPin(
                            materialEntry.AssetId,
                            null,
                            ownedTextureSet));
                        retainedTextureSet = null;
                    }
                    finally
                    {
                        retainedTextureSet?.Dispose();
                    }
                }
                else
                {
                    var sourceMaterialLease = materialEntry.Lease
                        ?? throw new InvalidOperationException("The prepared Material has no owned package lease.");
                    AssetLease<RenderMaterialAsset>? retainedMaterial = null;

                    try
                    {
                        retainedMaterial = sourceMaterialLease.Retain();
                        var ownedMaterial = retainedMaterial
                            ?? throw new InvalidOperationException("The Material lease could not be retained.");
                        materials.Add(new RenderSnapshotAssetPins.SceneAssetPins.MaterialPin(
                            materialEntry.AssetId,
                            ownedMaterial,
                            null));
                        retainedMaterial = null;
                    }
                    finally
                    {
                        retainedMaterial?.Dispose();
                    }
                }
            }

            foreach (var meshEntry in _meshEntries)
            {
                AssetLease<RenderMeshAsset>? retainedMesh = null;
                AssetLease<SkeletonAsset>? retainedSkeleton = null;

                try
                {
                    retainedMesh = meshEntry.Lease.Retain();
                    retainedSkeleton = meshEntry.SkeletonLease?.Retain();
                    var ownedMesh = retainedMesh
                        ?? throw new InvalidOperationException("The Mesh lease could not be retained.");
                    meshes.Add(new RenderSnapshotAssetPins.SceneAssetPins.MeshPin(
                        meshEntry.AssetId,
                        ownedMesh,
                        retainedSkeleton));
                    retainedSkeleton = null;
                    retainedMesh = null;
                }
                finally
                {
                    retainedMesh?.Dispose();
                    retainedSkeleton?.Dispose();
                }
            }

            return new RenderSnapshotAssetPins.SceneAssetPins(
                sceneInstanceId,
                atlasPages.MoveToImmutable(),
                materials.MoveToImmutable(),
                meshes.MoveToImmutable());
        }
        catch
        {
            foreach (var atlasPage in atlasPages)
            {
                atlasPage.PageSet.Dispose();
            }

            foreach (var material in materials)
            {
                material.Dispose();
            }

            foreach (var mesh in meshes)
            {
                mesh.Dispose();
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

        var material = FindMaterialEntry(selectedMaterialId, _materialEntries).Material;

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

    private readonly record struct MaterialEntry(
        int AssetId,
        AssetLease<RenderMaterialAsset>? Lease,
        RenderMaterialTextureSet? TextureSet)
    {
        internal RenderMaterialAsset Material
            => TextureSet?.Material
            ?? Lease?.Value
            ?? throw new ObjectDisposedException(nameof(MaterialEntry));

        internal void Dispose()
        {
            try
            {
                Lease?.Dispose();
            }
            finally
            {
                TextureSet?.Dispose();
            }
        }
    }

    private readonly record struct MeshEntry(
        int AssetId,
        AssetLease<RenderMeshAsset> Lease,
        AssetLease<SkeletonAsset>? SkeletonLease)
    {
        internal RenderMeshAsset Mesh => Lease.Value;

        internal SkeletonAsset? Skeleton => SkeletonLease?.Value;

        internal void Dispose()
        {
            try
            {
                Lease.Dispose();
            }
            finally
            {
                SkeletonLease?.Dispose();
            }
        }
    }
}
