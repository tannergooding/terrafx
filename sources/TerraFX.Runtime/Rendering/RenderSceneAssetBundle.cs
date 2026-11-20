// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
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

    private int _additionalCleanupFailureCount;
    private AtlasEntry[] _atlasEntries;
    private long _atlasEntriesWorkspaceClaimBytes;
    private int[] _atlasIds;
    private RenderAtlasPageLoader.OwnedProgress? _atlasPageLoadingProgress;
    private Exception? _disposeFailure;
    private int _disposeState;
    private int _loadedAtlasCount;
    private int _loadedMaterialCount;
    private int _loadedMeshCount;
    private MaterialEntry[] _materialEntries;
    private long _materialEntriesWorkspaceClaimBytes;
    private int[] _materialIds;
    private WorldSceneStorageReservation? _materialScratchReservation;
    private long _materialScratchWorkspaceClaimBytes;
    private RenderMaterialTextureLoader.OwnedProgress? _materialTextureLoadingProgress;
    private MeshEntry[] _meshEntries;
    private long _meshEntriesWorkspaceClaimBytes;
    private int[] _meshIds;
    private int[] _meshMaterialIds;
    private AssetLease<RenderAtlasAsset>? _pendingAtlasLease;
    private AssetLease<RenderMaterialAsset>? _pendingMaterialLease;
    private AssetLease<RenderMeshAsset>? _pendingMeshLease;
    private RenderAtlasPageSet? _pendingPageSet;
    private AssetLease<RenderMaterialAsset>? _pendingRetainedMaterialLease;
    private AssetLease<RenderMeshAsset>? _pendingRetainedMeshLease;
    private AssetLease<SkeletonAsset>? _pendingSkeletonLease;
    private RenderMaterialTextureSet? _pendingTextureSet;
    private ScenePreparationFunding _preparationFunding;
    private WorldSceneStorageReservation? _scratchReservation;
    private long _scratchWorkspaceClaimBytes;
    private int _spriteMaterialIdCount;
    private int[] _spriteMaterialIds;
    private WorldSceneStorageReservation? _storageReservation;

    private RenderSceneAssetBundle()
    {
        _atlasEntries = [];
        _atlasIds = [];
        _materialIds = [];
        _materialEntries = [];
        _meshIds = [];
        _meshEntries = [];
        _meshMaterialIds = [];
        _spriteMaterialIds = [];
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
            var meshAssetId = new AssetId(meshIds[index]);
            var meshDescriptor = RendererAssetCodecProfiles.DescribeMeshRead(plan, meshAssetId);
            using var meshLease = await plan.LoadDescribedFromPackageAsync<RenderMeshAsset>(
                meshDescriptor,
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
            + GetRequiredByteReservation(atlasCount, materialCount, meshCount)
            + (3 * WorldSceneStorageReservation.ControlByteCharge));
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Prepared Mesh, Material, texture, and page leases transfer together into the Scene bundle and snapshot pin owners; all failure paths dispose untransferred leases.")]
    internal static Task<RenderSceneAssetBundle?> PrepareAsync(
        SceneExpansionPlan plan,
        Func<long, WorldSceneStorageReservation?> reserveStorage,
        Func<long, WorldSceneStorageReservation?> reserveWorkspace,
        CancellationToken cancellationToken)
        => PrepareOwnedCoreAsync(plan, reserveStorage, reserveWorkspace, funding: default, cancellationToken);

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The exact PreparedScene stages the empty bundle before it acquires package-backed state and owns cleanup on every asynchronous failure.")]
    internal static Task<RenderSceneAssetBundle?> PrepareOwnedAsync(
        SceneExpansionPlan plan,
        ScenePreparationFunding funding,
        CancellationToken cancellationToken)
    {
        if (!funding.IsInitialized)
        {
            throw new ArgumentException("The Scene preparation funding value is uninitialized.", nameof(funding));
        }

        return PrepareOwnedCoreAsync(plan, reserveStorage: null, reserveWorkspace: null, funding, cancellationToken);
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Prepared Mesh, Material, texture, and page leases transfer together into the Scene bundle and snapshot pin owners; all failure paths dispose untransferred leases.")]
    internal static async Task<RenderSceneAssetBundle?> PrepareOwnedCoreAsync(
        SceneExpansionPlan plan,
        Func<long, WorldSceneStorageReservation?>? reserveStorage,
        Func<long, WorldSceneStorageReservation?>? reserveWorkspace,
        ScenePreparationFunding funding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!funding.IsInitialized)
        {
            ArgumentNullException.ThrowIfNull(reserveStorage);
            ArgumentNullException.ThrowIfNull(reserveWorkspace);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var spriteCount = CountSprites(plan);
        var meshComponentCount = CountMeshes(plan);

        if ((spriteCount == 0) && (meshComponentCount == 0))
        {
            return null;
        }

        RenderSceneAssetBundle? bundle = null;
        var preparationOwner = funding.IsInitialized ? funding.Preparation : null;

        if (preparationOwner is not null)
        {
            if (!preparationOwner.TryBeginPendingOwnerConstruction())
            {
                throw new InvalidOperationException("The Scene preparation cannot elect its render bundle receiver.");
            }

            var constructionCommitted = false;

            try
            {
                bundle = new RenderSceneAssetBundle();
                constructionCommitted = preparationOwner.TryCommitPendingOwnerConstruction(bundle);

                if (!constructionCommitted)
                {
                    throw new InvalidOperationException("The Scene preparation cannot publish its empty render bundle receiver.");
                }

                bundle._preparationFunding = funding;
            }
            finally
            {
                if (!constructionCommitted)
                {
                    preparationOwner.CancelPendingOwnerConstruction();
                }
            }
        }

        WorldSceneStorageReservation? scratchReservation = null;

        try
        {
            var scratchBytes = GetScratchByteReservation(spriteCount, meshComponentCount);
            bundle ??= new RenderSceneAssetBundle();

            if (funding.IsInitialized)
            {
                if (!funding.TryClaimWorkspace(scratchBytes))
                {
                    throw new InvalidOperationException("The Scene preparation cannot claim render asset-preparation scratch storage.");
                }

                bundle._scratchWorkspaceClaimBytes = scratchBytes;
            }
            else
            {
                scratchReservation = (reserveWorkspace
                    ?? throw new InvalidOperationException("The render asset-preparation workspace owner is unavailable."))(scratchBytes)
                    ?? throw new InvalidOperationException("The World cannot reserve render asset-preparation scratch storage.");
                bundle._scratchReservation = scratchReservation;
                scratchReservation = null;
            }

            var atlasIds = bundle._atlasIds = new int[spriteCount];
            var spriteMaterialIds = bundle._spriteMaterialIds = new int[spriteCount];
            var meshIds = bundle._meshIds = new int[meshComponentCount];
            var spriteMaterialIdCount = 0;
            FillAssetIds(
                plan,
                atlasIds,
                spriteMaterialIds,
                ref spriteMaterialIdCount,
                meshIds,
                cancellationToken);
            bundle._spriteMaterialIdCount = spriteMaterialIdCount;
            Array.Sort(atlasIds);
            Array.Sort(spriteMaterialIds, 0, spriteMaterialIdCount);
            Array.Sort(meshIds);
            var atlasCount = CompactDistinct(atlasIds, atlasIds.Length);
            var meshCount = CompactDistinct(meshIds, meshIds.Length);
            var atlasEntriesBytes = ArrayBytes(atlasCount, Unsafe.SizeOf<AtlasEntry>());

            if (funding.IsInitialized)
            {
                if (!funding.TryClaimWorkspace(atlasEntriesBytes))
                {
                    throw new InvalidOperationException("The Scene preparation cannot claim Atlas-entry storage.");
                }

                bundle._atlasEntriesWorkspaceClaimBytes = atlasEntriesBytes;
            }

            var atlasEntries = bundle._atlasEntries = new AtlasEntry[atlasCount];

            var meshEntriesBytes = ArrayBytes(meshCount, Unsafe.SizeOf<MeshEntry>());

            if (funding.IsInitialized)
            {
                if (!funding.TryClaimWorkspace(meshEntriesBytes))
                {
                    throw new InvalidOperationException("The Scene preparation cannot claim Mesh-entry storage.");
                }

                bundle._meshEntriesWorkspaceClaimBytes = meshEntriesBytes;
            }

            var meshEntries = bundle._meshEntries = new MeshEntry[meshCount];

            for (var index = 0; index < atlasCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assetId = new AssetId(atlasIds[index]);
                var atlasDescriptor = RendererAssetCodecProfiles.DescribeAtlasRead(plan, assetId);
                bundle._pendingAtlasLease = await plan.LoadDescribedFromPackageAsync<RenderAtlasAsset>(
                    atlasDescriptor,
                    cancellationToken).ConfigureAwait(false);
                var atlasLease = bundle._pendingAtlasLease
                    ?? throw new InvalidOperationException("The Atlas load returned no owned lease.");
                var atlasPageLoadingProgress = bundle._atlasPageLoadingProgress ??=
                    new RenderAtlasPageLoader.OwnedProgress();
                var pageSet = await RenderAtlasPageLoader.RetainOwnedAsync(
                    atlasLease,
                    atlasPageLoadingProgress,
                    cancellationToken).ConfigureAwait(false);
                bundle._pendingPageSet = pageSet;
                _ = atlasPageLoadingProgress.TakeResult();
                atlasEntries[index] = new AtlasEntry(assetId.Value, pageSet);
                bundle._loadedAtlasCount++;
                bundle._pendingPageSet = null;
                DisposePending(ref bundle._pendingAtlasLease);
            }

            for (var index = 0; index < meshCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assetId = new AssetId(meshIds[index]);
                var meshDescriptor = RendererAssetCodecProfiles.DescribeMeshRead(plan, assetId);
                bundle._pendingMeshLease = await plan.LoadDescribedFromPackageAsync<RenderMeshAsset>(
                    meshDescriptor,
                    cancellationToken).ConfigureAwait(false);
                var meshLease = bundle._pendingMeshLease
                    ?? throw new InvalidOperationException("The Mesh load returned no owned lease.");
                bundle._pendingSkeletonLease = await RenderMeshAssetBinding.LoadProfiledSkeletonAsync(
                    meshLease,
                    cancellationToken).ConfigureAwait(false);
                bundle._pendingRetainedMeshLease = meshLease.Retain();
                var ownedMeshLease = bundle._pendingRetainedMeshLease
                    ?? throw new InvalidOperationException("The Mesh lease could not be retained.");
                meshEntries[index] = new MeshEntry(
                    assetId.Value,
                    ownedMeshLease,
                    bundle._pendingSkeletonLease);
                bundle._loadedMeshCount++;
                bundle._pendingRetainedMeshLease = null;
                bundle._pendingSkeletonLease = null;
                DisposePending(ref bundle._pendingMeshLease);
            }

            var meshMaterialReferenceCount = 0;

            foreach (var meshEntry in meshEntries)
            {
                meshMaterialReferenceCount = checked(
                    meshMaterialReferenceCount + meshEntry.Mesh.MaterialSlots.Length);
            }

            var totalMaterialReferenceCount = checked(spriteMaterialIdCount + meshMaterialReferenceCount);
            var materialScratchBytes = GetMaterialScratchByteReservation(
                spriteMaterialIdCount,
                meshMaterialReferenceCount);

            if (funding.IsInitialized)
            {
                if (!funding.TryClaimWorkspace(materialScratchBytes))
                {
                    throw new InvalidOperationException("The Scene preparation cannot claim Mesh Material-reference scratch storage.");
                }

                bundle._materialScratchWorkspaceClaimBytes = materialScratchBytes;
            }
            else
            {
                bundle._materialScratchReservation = (reserveWorkspace
                    ?? throw new InvalidOperationException("The render asset-preparation workspace owner is unavailable."))(materialScratchBytes)
                    ?? throw new InvalidOperationException("The World cannot reserve Mesh Material-reference scratch storage.");
            }

            var materialIds = bundle._materialIds = new int[totalMaterialReferenceCount];
            spriteMaterialIds.AsSpan(0, spriteMaterialIdCount).CopyTo(materialIds);
            var meshMaterialIds = bundle._meshMaterialIds = new int[meshMaterialReferenceCount];
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
            var materialEntriesBytes = ArrayBytes(materialCount, Unsafe.SizeOf<MaterialEntry>());

            if (funding.IsInitialized)
            {
                if (!funding.TryClaimWorkspace(materialEntriesBytes))
                {
                    throw new InvalidOperationException("The Scene preparation cannot claim Material-entry storage.");
                }

                bundle._materialEntriesWorkspaceClaimBytes = materialEntriesBytes;
            }

            var materialEntries = bundle._materialEntries = new MaterialEntry[materialCount];

            for (var index = 0; index < materialCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assetId = new AssetId(materialIds[index]);
                var materialDescriptor = RendererAssetCodecProfiles.DescribeMaterialRead(plan, assetId);
                bundle._pendingMaterialLease = await plan.LoadDescribedFromPackageAsync<RenderMaterialAsset>(
                    materialDescriptor,
                    cancellationToken).ConfigureAwait(false);
                var materialLease = bundle._pendingMaterialLease
                    ?? throw new InvalidOperationException("The Material load returned no owned lease.");
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
                    var materialTextureLoadingProgress = bundle._materialTextureLoadingProgress ??=
                        new RenderMaterialTextureLoader.OwnedProgress();
                    var textureSet = await RenderMaterialTextureLoader.RetainOwnedAsync(
                        materialLease,
                        materialTextureLoadingProgress,
                        cancellationToken).ConfigureAwait(false);
                    bundle._pendingTextureSet = textureSet;
                    _ = materialTextureLoadingProgress.TakeResult();
                }
                else
                {
                    bundle._pendingRetainedMaterialLease = materialLease.Retain();
                }

                materialEntries[index] = new MaterialEntry(
                    assetId.Value,
                    bundle._pendingRetainedMaterialLease,
                    bundle._pendingTextureSet);
                bundle._loadedMaterialCount++;
                bundle._pendingRetainedMaterialLease = null;
                bundle._pendingTextureSet = null;
                DisposePending(ref bundle._pendingMaterialLease);
            }

            ValidateMeshBindings(meshEntries, materialEntries);
            ValidateSpriteBindings(plan, atlasEntries, materialEntries, cancellationToken);

            if (funding.IsInitialized)
            {
                throw new InvalidOperationException(
                    "Scene asset-bundle settlement remains unavailable until its complete retained-storage charge is qualified.");
            }

            var storageReservation = (reserveStorage
                ?? throw new InvalidOperationException("The render asset-bundle storage owner is unavailable."))(
                GetRequiredByteReservation(atlasCount, materialCount, meshCount))
                ?? throw new InvalidOperationException("The World cannot reserve retained render asset-bundle storage.");
            bundle._storageReservation = storageReservation;
            storageReservation.SetRetainedOwner(bundle);
            bundle.ReleaseWorkspaceReservations();
            bundle._atlasIds = [];
            bundle._spriteMaterialIds = [];
            bundle._meshIds = [];
            bundle._materialIds = [];
            bundle._meshMaterialIds = [];
            bundle._spriteMaterialIdCount = 0;
            atlasIds = [];
            spriteMaterialIds = [];
            meshIds = [];
            materialIds = [];
            meshMaterialIds = [];
            return bundle;
        }
        catch
        {
            if (!funding.IsInitialized)
            {
                bundle?.Dispose();
            }

            throw;
        }
        finally
        {
            scratchReservation?.Dispose();
        }
    }

    private static long ArrayBytes(int count, int elementSize)
        => WorldAdmissionDomain.ArrayCharge(count, elementSize);

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

    private static void DisposePending<T>(ref T? owner)
        where T : class, IDisposable
    {
        var current = Volatile.Read(ref owner);
        current?.Dispose();

        if (current is not null)
        {
            _ = Interlocked.CompareExchange(ref owner, null, current);
        }
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

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Independent bundle entries, pending owners, and reservation debts are all attempted before failure is retained.")]
    public void Dispose()
    {
        var state = Interlocked.CompareExchange(ref _disposeState, 1, 0);

        if (state is 2)
        {
            return;
        }

        if (state is 3)
        {
            RethrowDisposeFailure();
        }

        if (state is not 0)
        {
            throw new InvalidOperationException("Render asset-bundle cleanup is already in progress.");
        }

        Exception? firstFailure = null;

        for (var index = 0; index < _loadedAtlasCount; index++)
        {
            try
            {
                _atlasEntries[index].PageSet.Dispose();
            }
            catch (Exception failure)
            {
                RecordCleanupFailure(ref firstFailure, failure);
            }
        }

        for (var index = 0; index < _loadedMaterialCount; index++)
        {
            try
            {
                _materialEntries[index].Dispose();
            }
            catch (Exception failure)
            {
                RecordCleanupFailure(ref firstFailure, failure);
            }
        }

        for (var index = 0; index < _loadedMeshCount; index++)
        {
            try
            {
                _meshEntries[index].Dispose();
            }
            catch (Exception failure)
            {
                RecordCleanupFailure(ref firstFailure, failure);
            }
        }

        try
        {
            DisposePendingAssets();
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(ref firstFailure, failure);
        }

        if (firstFailure is not null)
        {
            Volatile.Write(ref _disposeFailure, firstFailure);
            Volatile.Write(ref _disposeState, 3);
            RethrowDisposeFailure();
        }

        _atlasEntries = [];
        _atlasIds = [];
        _loadedAtlasCount = 0;
        _materialEntries = [];
        _materialIds = [];
        _loadedMaterialCount = 0;
        _meshEntries = [];
        _meshIds = [];
        _meshMaterialIds = [];
        _loadedMeshCount = 0;
        _spriteMaterialIds = [];
        _spriteMaterialIdCount = 0;

        try
        {
            ReleaseWorkspaceReservations();
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(ref firstFailure, failure);
        }

        try
        {
            DisposePending(ref _storageReservation);
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(ref firstFailure, failure);
        }

        if (firstFailure is not null)
        {
            Volatile.Write(ref _disposeFailure, firstFailure);
            Volatile.Write(ref _disposeState, 3);
            RethrowDisposeFailure();
        }

        _preparationFunding = default;
        Volatile.Write(ref _disposeState, 2);
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

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Each pending child owner is attempted independently and failed references remain in their exact fields.")]
    private void DisposePendingAssets()
    {
        Exception? firstFailure = null;

        try
        {
            DisposePending(ref _pendingPageSet);
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(ref firstFailure, failure);
        }

        try
        {
            DisposePending(ref _atlasPageLoadingProgress);
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(ref firstFailure, failure);
        }

        try
        {
            DisposePending(ref _pendingTextureSet);
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(ref firstFailure, failure);
        }

        try
        {
            DisposePending(ref _materialTextureLoadingProgress);
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(ref firstFailure, failure);
        }

        try
        {
            DisposePending(ref _pendingRetainedMaterialLease);
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(ref firstFailure, failure);
        }

        try
        {
            DisposePending(ref _pendingRetainedMeshLease);
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(ref firstFailure, failure);
        }

        try
        {
            DisposePending(ref _pendingSkeletonLease);
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(ref firstFailure, failure);
        }

        try
        {
            DisposePending(ref _pendingMaterialLease);
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(ref firstFailure, failure);
        }

        try
        {
            DisposePending(ref _pendingMeshLease);
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(ref firstFailure, failure);
        }

        try
        {
            DisposePending(ref _pendingAtlasLease);
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(ref firstFailure, failure);
        }

        if (firstFailure is not null)
        {
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
    }

    private void RecordCleanupFailure(ref Exception? firstFailure, Exception failure)
    {
        if (firstFailure is null)
        {
            firstFailure = failure;
        }
        else
        {
            _additionalCleanupFailureCount = checked(_additionalCleanupFailureCount + 1);
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Independent workspace claims and reservation owners are all attempted before the first cleanup failure is rethrown.")]
    private void ReleaseWorkspaceReservations()
    {
        Exception? firstFailure = null;

        try
        {
            ReturnWorkspaceClaim(
                ref _materialEntriesWorkspaceClaimBytes,
                "The render bundle could not return its Material-entry claim.");
        }
        catch (Exception failure)
        {
            firstFailure ??= failure;
        }

        try
        {
            ReturnWorkspaceClaim(
                ref _meshEntriesWorkspaceClaimBytes,
                "The render bundle could not return its Mesh-entry claim.");
        }
        catch (Exception failure)
        {
            firstFailure ??= failure;
        }

        try
        {
            ReturnWorkspaceClaim(
                ref _atlasEntriesWorkspaceClaimBytes,
                "The render bundle could not return its Atlas-entry claim.");
        }
        catch (Exception failure)
        {
            firstFailure ??= failure;
        }

        try
        {
            ReturnWorkspaceClaim(
                ref _materialScratchWorkspaceClaimBytes,
                "The render bundle could not return its Material scratch claim.");
        }
        catch (Exception failure)
        {
            firstFailure ??= failure;
        }

        try
        {
            DisposePending(ref _materialScratchReservation);
        }
        catch (Exception failure)
        {
            firstFailure ??= failure;
        }

        try
        {
            ReturnWorkspaceClaim(
                ref _scratchWorkspaceClaimBytes,
                "The render bundle could not return its initial scratch claim.");
        }
        catch (Exception failure)
        {
            firstFailure ??= failure;
        }

        try
        {
            DisposePending(ref _scratchReservation);
        }
        catch (Exception failure)
        {
            firstFailure ??= failure;
        }

        if (firstFailure is not null)
        {
            ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
    }

    private void RethrowDisposeFailure()
    {
        var failure = Volatile.Read(ref _disposeFailure)
            ?? new InvalidOperationException("Render asset-bundle cleanup failed without a retained failure.");
        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void ReturnWorkspaceClaim(ref long claimBytes, string failureMessage)
    {
        var bytes = claimBytes;

        if ((bytes != 0) && !_preparationFunding.TryReturnWorkspaceClaim(bytes))
        {
            throw new InvalidOperationException(failureMessage);
        }

        claimBytes = 0;
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

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

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "The independently retained Material and texture owners are both attempted before failure is returned.")]
        internal void Dispose()
        {
            Exception? firstFailure = null;

            try
            {
                Lease?.Dispose();
            }
            catch (Exception failure)
            {
                firstFailure = failure;
            }

            try
            {
                TextureSet?.Dispose();
            }
            catch (Exception failure)
            {
                firstFailure ??= failure;
            }

            if (firstFailure is not null)
            {
                ExceptionDispatchInfo.Capture(firstFailure).Throw();
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

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "The Mesh and optional Skeleton leases are independent owners and both are attempted before failure is returned.")]
        internal void Dispose()
        {
            Exception? firstFailure = null;

            try
            {
                Lease.Dispose();
            }
            catch (Exception failure)
            {
                firstFailure = failure;
            }

            try
            {
                SkeletonLease?.Dispose();
            }
            catch (Exception failure)
            {
                firstFailure ??= failure;
            }

            if (firstFailure is not null)
            {
                ExceptionDispatchInfo.Capture(firstFailure).Throw();
            }
        }
    }
}
