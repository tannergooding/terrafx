// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;

namespace TerraFX.Animation;

internal sealed class AnimationPreparedRestoreAssets : IDisposable
{
    private Exception? _cleanupFailure;
    private AssetLease<AnimationClipAsset>?[] _clips = [];
    private AssetLease<AnimationControllerAsset>?[] _controllers = [];
    private bool _isDisposed;
    private bool _isPreparationStarted;
    private bool _isPrepared;
    private AssetPackageLease? _packageLease;
    private AssetLease<SkeletonAsset>?[] _skeletons = [];
    private IAnimationRestoreAssetSourceOwner? _sourceOwner;

    internal static long MeasureByteReservation()
    {
        const int MaximumControllers = 4096;
        const int MaximumClips = 4 * MaximumControllers;
        // The bundle/input/task controls, demand arrays, and retained handles coexist.
        // Assets accounts for the decoded values and lease backing independently.
        return checked(
            1024L
            + ArrayBytes(MaximumControllers, sizeof(int))
            + ArrayBytes(MaximumClips, sizeof(int))
            + ArrayBytes(MaximumControllers, IntPtr.Size)
            + (2 * ArrayBytes(MaximumClips, IntPtr.Size))
            // LoadSkeletonAsync validates at most 256 joints in one HashSet at a time.
            + ArrayBytes(512, sizeof(int))
            + ArrayBytes(512, Unsafe.SizeOf<Guid>() + (2 * sizeof(int))));
    }

    private static long ArrayBytes(int count, int stride)
        => checked(64 + ((((long)count * stride) + 7) & ~7L));

    private static int CompactDistinct(int[] values)
    {
        Array.Sort(values);
        var count = 0;

        foreach (var value in values)
        {
            if ((count == 0) || (value != values[count - 1]))
            {
                values[count++] = value;
            }
        }

        return count;
    }

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "Every retained asset release is attempted; a failed owner remains anchored and charged by the real prepared source control.")]
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        void Release<T>(AssetLease<T>?[] leases)
        {
            for (var index = leases.Length - 1; index >= 0; index--)
            {
                try
                {
                    leases[index]?.Dispose();
                    leases[index] = null;
                }
                catch (Exception exception)
                {
                    _cleanupFailure ??= exception;
                }
            }
        }

        Release(_skeletons);
        Release(_clips);
        Release(_controllers);

        if (_cleanupFailure is { } failure)
        {
            throw failure;
        }

        _isDisposed = true;
    }

    internal Task PrepareAsync(
        IAnimationRestoreAssetSourceOwner sourceOwner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceOwner);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (_isPreparationStarted)
        {
            throw new InvalidOperationException("The prepared Animation asset owner can prepare only once.");
        }

        // Acquire both caches and the package's actual source interest before a task escapes.
        var source = sourceOwner.AcquireAnimationAssets();
        _isPreparationStarted = true;
        return PrepareCoreAsync(source, cancellationToken);
    }

    internal void ValidateSourceOwner(
        IAnimationRestoreAssetSourceOwner sourceOwner,
        AssetPackageLease packageLease)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        if (!_isPrepared || !ReferenceEquals(_sourceOwner, sourceOwner)
            || !ReferenceEquals(_packageLease, packageLease))
        {
            throw new InvalidOperationException("The prepared Animation assets belong to another source/package relationship or are incomplete.");
        }
    }

    private async Task PrepareCoreAsync(
        AnimationRestoreAssetSourceLease source,
        CancellationToken cancellationToken)
    {
        using (source)
        {
            _sourceOwner = source.Owner;
            var packageLease = source.PackageLease;
            _packageLease = packageLease;
            _ = source.PlaybackState.Author;
            var requests = source.RootMotionRequests.Author;
            var controllerDemandCount = 0;
            var clipDemandCount = 0;

            foreach (var slot in requests.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (slot.Present)
                {
                    controllerDemandCount++;
                    clipDemandCount = checked(clipDemandCount
                        + (slot.Sources ?? throw new InvalidOperationException("The prepared present request omitted Sources.")).Length);
                }
            }

            var controllerIds = new int[controllerDemandCount];
            var clipIds = new int[clipDemandCount];
            var controllerIndex = 0;
            var clipIndex = 0;

            foreach (var slot in requests.Values)
            {
                if (!slot.Present)
                {
                    continue;
                }

                controllerIds[controllerIndex++] = (slot.Controller
                    ?? throw new InvalidOperationException("The prepared present request omitted Controller.")).AssetId;

                foreach (var contribution in slot.Sources
                    ?? throw new InvalidOperationException("The prepared present request omitted Sources."))
                {
                    clipIds[clipIndex++] = contribution.Clip.AssetId;
                }
            }

            var controllerCount = CompactDistinct(controllerIds);
            var clipCount = CompactDistinct(clipIds);
            _controllers = new AssetLease<AnimationControllerAsset>?[controllerCount];
            _clips = new AssetLease<AnimationClipAsset>?[clipCount];
            _skeletons = new AssetLease<SkeletonAsset>?[clipCount];

            for (var index = 0; index < controllerCount; index++)
            {
                var assetId = new AssetId(controllerIds[index]);
                var controller = await packageLease.LoadFromPackageAsync<AnimationControllerAsset>(
                    assetId, cancellationToken).ConfigureAwait(false);
                _controllers[index] = controller;
                source.ThrowIfFailed();

                if ((controller.AssetId != assetId) || (controller.Value.AssetId != assetId))
                {
                    throw new InvalidDataException("A root-motion Controller decoder returned a different AssetId.");
                }
            }

            foreach (var slot in requests.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!slot.Present)
                {
                    continue;
                }

                var controller = (_controllers[Array.BinarySearch(
                    controllerIds, 0, controllerCount, (slot.Controller
                        ?? throw new InvalidOperationException("The prepared present request omitted Controller.")).AssetId)]
                    ?? throw new InvalidOperationException("The prepared root-motion Controller is missing.")).Value;

                if (controller.RootMotionMode != 1)
                {
                    throw new InvalidDataException("A present root-motion request requires its retained Controller's Extract mode.");
                }

                var previousSlot = -1;

                foreach (var contribution in slot.Sources
                    ?? throw new InvalidOperationException("The prepared present request omitted Sources."))
                {
                    var selectedSlot = -1;

                    for (var index = 0; index < controller.ClipSlots.Length; index++)
                    {
                        if (controller.ClipSlots[index].ClipSlotId == contribution.ClipSlotId)
                        {
                            selectedSlot = index;
                            break;
                        }
                    }

                    if ((selectedSlot <= previousSlot)
                        || (controller.ClipSlots[selectedSlot].ClipAssetId.Value != contribution.Clip.AssetId))
                    {
                        throw new InvalidDataException("Root-motion Sources must be unique, match the retained Controller's Clip slots, and preserve its authored slot order.");
                    }

                    previousSlot = selectedSlot;
                }
            }

            for (var index = 0; index < clipCount; index++)
            {
                var assetId = new AssetId(clipIds[index]);
                var clip = await packageLease.LoadFromPackageAsync<AnimationClipAsset>(
                    assetId, cancellationToken).ConfigureAwait(false);
                _clips[index] = clip;
                source.ThrowIfFailed();

                if ((clip.AssetId != assetId) || (clip.Value.AssetId != assetId)
                    || (clip.Value.RootMotion is not { Policy: 0 }))
                {
                    throw new InvalidDataException("A root-motion source requires the exact extractable native Clip.");
                }

                var skeleton = await AnimationClipAssetBinding.LoadSkeletonAsync(
                    clip, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("An extractable root-motion Clip requires its exact Skeleton.");
                _skeletons[index] = skeleton;
                source.ThrowIfFailed();

                if ((skeleton.AssetId != clip.Value.SkeletonAssetId)
                    || (skeleton.Value.AssetId != skeleton.AssetId))
                {
                    throw new InvalidDataException("A root-motion Skeleton decoder returned a different AssetId.");
                }
            }

            foreach (var slot in requests.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!slot.Present)
                {
                    continue;
                }

                var controller = (_controllers[Array.BinarySearch(
                    controllerIds, 0, controllerCount, (slot.Controller
                        ?? throw new InvalidOperationException("The prepared present request omitted Controller.")).AssetId)]
                    ?? throw new InvalidOperationException("The prepared root-motion Controller is missing.")).Value;

                foreach (var contribution in slot.Sources
                    ?? throw new InvalidOperationException("The prepared present request omitted Sources."))
                {
                    var clip = (_clips[Array.BinarySearch(clipIds, 0, clipCount, contribution.Clip.AssetId)]
                        ?? throw new InvalidOperationException("The prepared root-motion Clip is missing.")).Value;

                    if ((controller.SkeletonAssetId != clip.SkeletonAssetId)
                        || (clip.SceneAssetId.HasValue && (controller.SceneAssetId != clip.SceneAssetId)))
                    {
                        throw new InvalidDataException("A root-motion source Clip does not match its retained Controller's Skeleton or Scene definition.");
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            source.ThrowIfFailed();
            _isPrepared = true;
        }
    }
}
