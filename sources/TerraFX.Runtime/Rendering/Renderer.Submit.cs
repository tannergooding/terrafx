// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

public sealed partial class Renderer
{
    /// <summary>Submits an immutable graph compiled for the exact render snapshot.</summary>
    /// <param name="snapshot">The World-produced snapshot used to build the graph.</param>
    /// <param name="graph">The successfully compiled graph plan.</param>
    /// <param name="cancellationToken">A token that cancels admission but not accepted GPU work.</param>
    /// <returns>A receipt after GPU completion and submission-pin retirement.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="graph"/> is null.</exception>
    /// <exception cref="ArgumentException">The graph was compiled for another snapshot or target.</exception>
    /// <exception cref="InvalidOperationException">The graph belongs to a stale GraphicsDevice epoch.</exception>
    /// <exception cref="NotSupportedException">This Renderer or backend does not support the graph's target or commands.</exception>
    public async Task<RenderSubmissionReceipt> SubmitAsync(
        RenderSnapshot snapshot,
        RenderGraphPlan graph,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(graph);
        cancellationToken.ThrowIfCancellationRequested();

        var device = _graphicsDevice
            ?? throw new NotSupportedException("Headless rendering does not submit native render graphs.");
        ObjectDisposedException.ThrowIf(snapshot.IsDisposed, snapshot);
        using var snapshotAssetPins = snapshot.RetainAssetPins();

        if ((graph.SnapshotId != snapshot.SnapshotId)
            || (graph.WorldGuid != snapshot.WorldGuid)
            || (graph.TickIndex != snapshot.TickIndex)
            || (graph.FrameDimension != snapshot.FrameDimension)
            || (graph.FrameRevision != snapshot.FrameRevision)
            || (graph.ProfileId != snapshot.ProfileId)
            || (graph.TargetExtent != snapshot.TargetExtent)
            || !graph.CameraIds.AsSpan().SequenceEqual(snapshot.CameraIds.AsSpan()))
        {
            throw new ArgumentException("The graph plan does not match the exact render snapshot.", nameof(graph));
        }

        if (graph.DeviceEpoch != device.DeviceEpoch)
        {
            throw new InvalidOperationException("The graph plan belongs to a stale GraphicsDevice epoch.");
        }

        if (graph.OffscreenColorTarget is not GpuTexture outputTarget)
        {
            throw new NotSupportedException("Native submission of surface-targeted graphs is not available.");
        }

        if (!ReferenceEquals(outputTarget.ResourceOwner.Device, device)
            || (outputTarget.DeviceEpoch != graph.DeviceEpoch))
        {
            throw new ArgumentException("The graph output target belongs to another device or epoch.", nameof(graph));
        }

        var graphResourceOwners = new HashSet<GraphicsResourceOwner> {
            outputTarget.ResourceOwner,
        };

        foreach (var import in graph.BufferImports)
        {
            _ = graphResourceOwners.Add(import.Buffer.ResourceOwner);
        }

        foreach (var import in graph.TextureImports)
        {
            _ = graphResourceOwners.Add(import.Texture.ResourceOwner);
        }

        var transientBuffers = new List<GpuBuffer>(graph.TransientBuffers.Length);
        var transientBufferGroups = new List<GraphicsTransientBufferGroup>(graph.TransientBufferGroups.Length);
        var transientBuffersByResourceId = new Dictionary<ulong, GpuBuffer>(graph.TransientBuffers.Length);
        var transientTextures = new List<GpuTexture>(graph.TransientTextures.Length);
        var transientTextureGroups = new List<GraphicsTransientTextureGroup>(graph.TransientTextureGroups.Length);
        var transientTexturesByResourceId = new Dictionary<ulong, GpuTexture>(graph.TransientTextures.Length);
        var graphResourceUseLeases = device.AcquireGraphResourceUseLeases(graphResourceOwners);
        GraphicsDevice.GraphFrameSlotReservation? graphFrameSlotReservation = null;
        var graphResourceUseLeasesTransferred = false;

        try
        {
            graphFrameSlotReservation = await device.AcquireGraphFrameSlotAsync(cancellationToken).ConfigureAwait(false);

            foreach (var transientGroup in graph.TransientBufferGroups)
            {
                if (transientGroup.Length > 1)
                {
                    var materializedGroup = await device.CreateTransientBufferGroupAsync(
                        transientGroup,
                        cancellationToken).ConfigureAwait(false);
                    transientBufferGroups.Add(materializedGroup);

                    for (var index = 0; index < transientGroup.Length; index++)
                    {
                        transientBuffersByResourceId.Add(
                            transientGroup[index].ResourceId,
                            materializedGroup.Buffers[index]);
                    }
                }
                else
                {
                    var transientVersion = transientGroup[0];
                    var buffer = await device.CreateBufferAsync(
                        transientVersion.Description,
                        cancellationToken).ConfigureAwait(false);
                    transientBuffers.Add(buffer);
                    transientBuffersByResourceId.Add(transientVersion.ResourceId, buffer);
                }
            }

            var transientBufferImports = ImmutableArray.CreateBuilder<GraphBufferImportRecord>(graph.TransientBuffers.Length);

            for (var index = 0; index < graph.TransientBuffers.Length; index++)
            {
                var transientVersion = graph.TransientBuffers[index];
                var buffer = transientBuffersByResourceId[transientVersion.ResourceId];
                transientBufferImports.Add(new GraphBufferImportRecord(
                    buffer,
                    transientVersion,
                    isTransient: true));
            }

            foreach (var transientGroup in graph.TransientTextureGroups)
            {
                if (transientGroup.Length > 1)
                {
                    var materializedGroup = await device.CreateTransientTextureGroupAsync(
                        transientGroup,
                        cancellationToken).ConfigureAwait(false);
                    transientTextureGroups.Add(materializedGroup);

                    for (var index = 0; index < transientGroup.Length; index++)
                    {
                        transientTexturesByResourceId.Add(
                            transientGroup[index].ResourceId,
                            materializedGroup.Textures[index]);
                    }
                }
                else
                {
                    var transientVersion = transientGroup[0];
                    var texture = await device.CreateTextureAsync(
                        transientVersion.Description,
                        cancellationToken).ConfigureAwait(false);
                    transientTextures.Add(texture);
                    transientTexturesByResourceId.Add(transientVersion.ResourceId, texture);
                }
            }

            var transientTextureImports = ImmutableArray.CreateBuilder<GraphTextureImportRecord>(graph.TransientTextures.Length);

            foreach (var transientVersion in graph.TransientTextures)
            {
                var texture = transientTexturesByResourceId[transientVersion.ResourceId];
                transientTextureImports.Add(new GraphTextureImportRecord(
                    texture,
                    transientVersion,
                    new GraphImportOptions(
                        GraphResourceState.Undefined,
                        contentsValid: false,
                        GraphImportCompletionPolicy.WaitForLastUse),
                    isTransient: true));
            }

            var commandLists = await graph.RecordPassCallbacksAsync().ConfigureAwait(false);
            GraphicsGraphSubmission submission;

            try
            {
                submission = graph.CreateGraphicsSubmission(
                    commandLists,
                    transientBufferImports.MoveToImmutable(),
                    transientTextureImports.MoveToImmutable());
            }
            catch
            {
                foreach (var commandList in commandLists)
                {
                    commandList.Dispose();
                }

                throw;
            }

            var submissionTask = device.SubmitGraphAsync(
                submission,
                graphResourceUseLeases,
                graphFrameSlotReservation,
                cancellationToken);
            graphResourceUseLeasesTransferred = true;
            graphFrameSlotReservation = null;
            var result = await submissionTask.ConfigureAwait(false);
            return new RenderSubmissionReceipt(
                result.SubmissionSequence,
                result.CompletedFence,
                presentationSequence: null,
                result.DeviceEpoch,
                surfaceEpoch: 0,
                swapchainEpoch: 0);
        }
        finally
        {
            if (!graphResourceUseLeasesTransferred)
            {
                graphResourceUseLeases.Dispose();
            }

            graphFrameSlotReservation?.Dispose();

            foreach (var transientBufferGroup in transientBufferGroups)
            {
                transientBufferGroup.Dispose();
            }

            foreach (var transientBuffer in transientBuffers)
            {
                transientBuffer.Dispose();
            }

            foreach (var transientTexture in transientTextures)
            {
                transientTexture.Dispose();
            }

            foreach (var transientTextureGroup in transientTextureGroups)
            {
                transientTextureGroup.Dispose();
            }
        }
    }
}
