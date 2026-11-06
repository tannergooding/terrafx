// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

public sealed partial class RenderGraphPlan
{
    internal GraphicsGraphSubmission CreateGraphicsSubmission(
        ImmutableArray<RecordedPassCommandList> recordedPassCommandLists)
    {
        if (recordedPassCommandLists.IsDefault)
        {
            throw new ArgumentException("Recorded pass command lists must be initialized.", nameof(recordedPassCommandLists));
        }

        var commandListsByPass = new Dictionary<RenderPassId, RecordedPassCommandList>();

        foreach (var commandList in recordedPassCommandLists)
        {
            if (!commandListsByPass.TryAdd(commandList.PassId, commandList))
            {
                throw new ArgumentException("A graph pass cannot have duplicate recorded command lists.", nameof(recordedPassCommandLists));
            }
        }

        var passes = ImmutableArray.CreateBuilder<GraphicsGraphSubmissionPass>(OrderedPasses.Length);

        for (var index = 0; index < OrderedPasses.Length; index++)
        {
            var passId = OrderedPasses[index];
            var passKind = OrderedPassKinds[index] switch {
                RenderGraphPassKind.Raster => CommandRecorderPassKind.Raster,
                RenderGraphPassKind.Compute => CommandRecorderPassKind.Compute,
                RenderGraphPassKind.Copy => CommandRecorderPassKind.Copy,
                RenderGraphPassKind.Presentation or RenderGraphPassKind.OffscreenOutput
                    => CommandRecorderPassKind.Presentation,
                _ => throw new InvalidOperationException("The graph plan contains an unsupported pass kind."),
            };
            var commands = ImmutableArray<RecordedGraphicsCommand>.Empty;

            if (commandListsByPass.TryGetValue(passId, out var commandList))
            {
                if (commandList.PassKind != passKind)
                {
                    throw new ArgumentException("A recorded pass command list has the wrong pass kind.", nameof(recordedPassCommandLists));
                }

                commands = commandList.Commands;
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
                commands,
                attachments.ToImmutable()));
        }

        if (commandListsByPass.Count != 0)
        {
            throw new ArgumentException("A recorded command list does not belong to this compiled graph.", nameof(recordedPassCommandLists));
        }

        var outputTarget = OffscreenColorTarget
            ?? throw new NotSupportedException("Native submission of surface-targeted graphs is not available.");

        return new GraphicsGraphSubmission(
            DeviceEpoch,
            outputTarget,
            OrderedPasses,
            passes.ToImmutable(),
            StateTransitions,
            Accesses,
            BufferImports,
            TextureImports,
            recordedPassCommandLists);
    }
}
