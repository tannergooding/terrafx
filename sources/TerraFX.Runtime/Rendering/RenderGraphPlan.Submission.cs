// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

public sealed partial class RenderGraphPlan
{
    internal GraphicsGraphSubmission CreateGraphicsSubmission(
        GpuTexture outputColorTarget,
        ImmutableArray<RecordedPassCommandList> recordedPassCommandLists,
        ImmutableArray<GraphBufferImportRecord> transientBufferImports,
        ImmutableArray<GraphTextureImportRecord> transientTextureImports,
        IReadOnlyDictionary<ulong, GraphResourceState> deferredTextureInitialStates)
    {
        ArgumentNullException.ThrowIfNull(outputColorTarget);
        ArgumentNullException.ThrowIfNull(deferredTextureInitialStates);

        if (recordedPassCommandLists.IsDefault)
        {
            throw new ArgumentException("Recorded pass command lists must be initialized.", nameof(recordedPassCommandLists));
        }

        if (transientBufferImports.IsDefault || (transientBufferImports.Length != TransientBuffers.Length))
        {
            throw new ArgumentException("The graph submission requires one materialized buffer per transient graph buffer.", nameof(transientBufferImports));
        }

        if (transientTextureImports.IsDefault || (transientTextureImports.Length != TransientTextures.Length))
        {
            throw new ArgumentException("The graph submission requires one materialized texture per transient graph texture.", nameof(transientTextureImports));
        }

        var bufferImports = ImmutableArray.CreateBuilder<GraphBufferImportRecord>(
            checked(BufferImports.Length + transientBufferImports.Length));
        bufferImports.AddRange(BufferImports);

        for (var index = 0; index < transientBufferImports.Length; index++)
        {
            var import = transientBufferImports[index];

            if (!import.IsTransient || !ReferenceEquals(import.Version, TransientBuffers[index]))
            {
                throw new ArgumentException("The materialized transient buffer order does not match the compiled graph.", nameof(transientBufferImports));
            }

            bufferImports.Add(import);
        }

        var textureImports = ImmutableArray.CreateBuilder<GraphTextureImportRecord>(
            checked(TextureImports.Length + transientTextureImports.Length));
        textureImports.AddRange(TextureImports);
        var deferredTextureImportCount = 0;

        for (var index = 0; index < transientTextureImports.Length; index++)
        {
            var import = transientTextureImports[index];
            var isDeferredTexture = deferredTextureInitialStates.ContainsKey(import.Version.ResourceId);

            if (isDeferredTexture)
            {
                deferredTextureImportCount++;
            }

            if ((import.IsTransient == isDeferredTexture)
                || !ReferenceEquals(import.Version, TransientTextures[index]))
            {
                throw new ArgumentException("The materialized transient texture order does not match the compiled graph.", nameof(transientTextureImports));
            }

            textureImports.Add(import);
        }

        if (deferredTextureImportCount != deferredTextureInitialStates.Count)
        {
            throw new ArgumentException("The deferred texture states do not match the materialized graph imports.", nameof(deferredTextureInitialStates));
        }

        var commandListsByPass = new Dictionary<RenderPassId, RecordedPassCommandList>();

        foreach (var commandList in recordedPassCommandLists)
        {
            if (!commandListsByPass.TryAdd(commandList.PassId, commandList))
            {
                throw new ArgumentException("A graph pass cannot have duplicate recorded command lists.", nameof(recordedPassCommandLists));
            }
        }

        var passNodesById = new Dictionary<RenderPassId, RenderGraphPassNode>(Passes.Length);

        foreach (var pass in Passes)
        {
            passNodesById.Add(pass.Id, pass);
        }

        var passes = ImmutableArray.CreateBuilder<GraphicsGraphSubmissionPass>(OrderedPasses.Length);

        for (var index = 0; index < OrderedPasses.Length; index++)
        {
            var passId = OrderedPasses[index];
            var passNode = passNodesById[passId];
            var passKind = OrderedPassKinds[index] switch {
                RenderGraphPassKind.Raster => CommandRecorderPassKind.Raster,
                RenderGraphPassKind.Compute => CommandRecorderPassKind.Compute,
                RenderGraphPassKind.Copy => CommandRecorderPassKind.Copy,
                RenderGraphPassKind.Presentation or RenderGraphPassKind.OffscreenOutput
                    => CommandRecorderPassKind.Presentation,
                _ => throw new InvalidOperationException("The graph plan contains an unsupported pass kind."),
            };

            if (commandListsByPass.TryGetValue(passId, out var commandList))
            {
                if (commandList.PassKind != passKind)
                {
                    throw new ArgumentException("A recorded pass command list has the wrong pass kind.", nameof(recordedPassCommandLists));
                }

                _ = commandListsByPass.Remove(passId);
            }

            var attachments = ImmutableArray.CreateBuilder<GraphicsGraphSubmissionAttachment>();

            foreach (var attachment in AttachmentWrites)
            {
                if (attachment.PassId == passId)
                {
                    attachments.Add(new GraphicsGraphSubmissionAttachment(
                        attachment.Version,
                        attachment.Range,
                        attachment.LoadAction,
                        attachment.StoreAction,
                        attachment.ClearColor,
                        attachment.ClearDepth,
                        attachment.IsDepth));
                }
            }

            passes.Add(new GraphicsGraphSubmissionPass(
                passId,
                passKind,
                passNode.Description.RenderArea,
                commandList,
                attachments.ToImmutable()));
        }

        if (commandListsByPass.Count != 0)
        {
            throw new ArgumentException("A recorded command list does not belong to this compiled graph.", nameof(recordedPassCommandLists));
        }

        var outputImportIndex = -1;

        for (var index = 0; index < textureImports.Count; index++)
        {
            if (ReferenceEquals(textureImports[index].Texture, outputColorTarget))
            {
                if (outputImportIndex >= 0)
                {
                    throw new ArgumentException("The selected graph output texture has duplicate imports.", nameof(outputColorTarget));
                }

                outputImportIndex = index;
            }
        }

        if (outputImportIndex < 0)
        {
            throw new ArgumentException("The selected graph output texture is not imported by the compiled plan.", nameof(outputColorTarget));
        }

        var orderedTextureImports = ImmutableArray.CreateBuilder<GraphTextureImportRecord>(textureImports.Count);
        orderedTextureImports.Add(textureImports[outputImportIndex]);

        for (var index = 0; index < textureImports.Count; index++)
        {
            if (index != outputImportIndex)
            {
                orderedTextureImports.Add(textureImports[index]);
            }
        }

        var stateTransitions = ImmutableArray.CreateBuilder<GraphResourceStateTransition>(StateTransitions.Length);

        foreach (var transition in StateTransitions)
        {
            if (transition.IsTexture
                && deferredTextureInitialStates.TryGetValue(transition.ResourceId, out var initialState)
                && (transition.AfterState == GraphResourceState.ShaderSampledRead)
                && (transition.BeforeState == GraphResourceState.General))
            {
                if (initialState == GraphResourceState.ShaderSampledRead)
                {
                    continue;
                }

                if (initialState != GraphResourceState.General)
                {
                    throw new InvalidOperationException("A deferred Sprite texture has an unsupported initial graph state.");
                }
            }

            stateTransitions.Add(transition);
        }

        return new GraphicsGraphSubmission(
            DeviceEpoch,
            outputColorTarget,
            SurfaceTarget,
            PresentationSource,
            SurfaceEpoch,
            SwapchainEpoch,
            OrderedPasses,
            passes.ToImmutable(),
            stateTransitions.ToImmutable(),
            TransientBufferAliasingBarriers,
            TransientTextureAliasingBarriers,
            Accesses,
            bufferImports.MoveToImmutable(),
            orderedTextureImports.MoveToImmutable(),
            recordedPassCommandLists);
    }
}
