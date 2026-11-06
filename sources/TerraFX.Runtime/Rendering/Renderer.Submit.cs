// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
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

        var commandLists = await graph.RecordPassCallbacksAsync().ConfigureAwait(false);
        GraphicsGraphSubmission submission;

        try
        {
            submission = graph.CreateGraphicsSubmission(commandLists);
        }
        catch
        {
            foreach (var commandList in commandLists)
            {
                commandList.Dispose();
            }

            throw;
        }

        var result = await device.SubmitGraphAsync(submission, cancellationToken).ConfigureAwait(false);
        return new RenderSubmissionReceipt(
            result.SubmissionSequence,
            result.CompletedFence,
            presentationSequence: null,
            result.DeviceEpoch,
            surfaceEpoch: 0,
            swapchainEpoch: 0);
    }
}
