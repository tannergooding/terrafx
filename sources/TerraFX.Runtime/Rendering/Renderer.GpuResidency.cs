// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

public sealed partial class Renderer
{
    private static async Task<GpuBuffer> UploadUniformBufferAsync(
        GraphicsDevice device,
        ReadOnlyMemory<byte> bytes,
        string debugName,
        CancellationToken cancellationToken)
    {
        GpuBuffer? buffer = null;

        try
        {
            buffer = await device.CreateBufferAsync(
                new BufferDescription(
                    bytes.Length,
                    BufferUsage.Uniform | BufferUsage.CopyDestination,
                    GpuMemoryKind.DeviceLocal,
                    structuredStride: null,
                    debugName),
                cancellationToken).ConfigureAwait(false);
            var upload = await device.UploadBufferAsync(buffer, 0, bytes.Span, cancellationToken).ConfigureAwait(false);

            if (upload.Status != UploadStatus.Ready)
            {
                throw new InvalidOperationException($"The '{debugName}' upload did not complete.");
            }

            var result = buffer;
            buffer = null;
            return result;
        }
        finally
        {
            buffer?.Dispose();
        }
    }

    internal async Task<GpuBuffer> CreateCameraUniformBufferAsync(
        RenderSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(snapshot.IsDisposed, snapshot);

        var device = _graphicsDevice
            ?? throw new NotSupportedException("Headless rendering does not upload camera draw data.");
        var bytes = RenderCameraGpuDataBufferBuilder.CreateFrameBuffer(
            snapshot.Cameras.AsSpan(),
            checked((int)device.Capabilities.Limits.UniformBufferOffsetAlignment),
            out _);

        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("The render snapshot contains no camera data.");
        }

        return await UploadUniformBufferAsync(device, bytes, "sprite-camera-data", cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<GpuBuffer?> CreateSpriteObjectBufferAsync(
        RenderSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(snapshot.IsDisposed, snapshot);

        var device = _graphicsDevice
            ?? throw new NotSupportedException("Headless rendering does not upload Sprite draw data.");

        if (snapshot.SpriteDraws.IsEmpty)
        {
            return null;
        }

        var bytes = RenderSpriteGpuDataBufferBuilder.CreateObjectBuffer(
            snapshot.SpriteDraws.AsSpan(),
            checked((int)device.Capabilities.Limits.UniformBufferOffsetAlignment),
            out _);
        return await UploadUniformBufferAsync(device, bytes, "sprite-object-data", cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<GpuResidencyLease> EnsureSpritePageResidencyAsync(
        RenderSnapshot snapshot,
        int spriteDrawIndex,
        ResidencyOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        ObjectDisposedException.ThrowIf(snapshot.IsDisposed, snapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(spriteDrawIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(spriteDrawIndex, snapshot.SpriteDraws.Length);

        var residencyService = _gpuResidencyService
            ?? throw new NotSupportedException("Headless rendering does not upload Sprite assets.");
        var sprite = snapshot.SpriteDraws[spriteDrawIndex];
        using var snapshotAssetPins = snapshot.RetainAssetPins();
        var sceneAssets = snapshotAssetPins?.GetSceneAssets(sprite.SceneInstanceId)
            ?? throw new InvalidOperationException("The Sprite snapshot has no retained Scene assets.");
        var pageSet = sceneAssets.GetAtlasPageSet(sprite.AtlasAssetId);
        var frame = pageSet.GetFrame(sprite.FrameId);
        using var pageLease = pageSet.RetainPageLease(frame.Page);
        return await residencyService.EnsureSpritePageResidencyAsync(
            pageLease,
            options,
            cancellationToken).ConfigureAwait(false);
    }
}
