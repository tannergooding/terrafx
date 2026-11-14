// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

public sealed partial class RenderGraphPlan
{
    internal async Task<ImmutableArray<RecordedPassCommandList>> RecordPassCallbacksAsync(
        RenderBuiltInSubmissionResources? builtInResources)
    {
        var surface = SurfaceTarget;
        var device = surface?.Device
            ?? OffscreenColorTarget?.ResourceOwner.Device
            ?? throw new InvalidOperationException("The graph plan has no output target owner.");

        if (device.DeviceEpoch != DeviceEpoch)
        {
            throw new InvalidOperationException("The graph plan belongs to a different GraphicsDevice generation.");
        }

        if (surface is not null)
        {
            ObjectDisposedException.ThrowIf(!surface.IsLive, surface);
            var current = surface.CaptureSnapshot();

            if ((current.Capabilities.SurfaceEpoch != SurfaceEpoch)
                || (current.Capabilities.SwapchainEpoch != SwapchainEpoch)
                || (current.ClientPixelSize != TargetExtent))
            {
                throw new InvalidOperationException("The Surface graph plan belongs to a stale Surface or swapchain generation.");
            }
        }

        var commandLists = ImmutableArray.CreateBuilder<RecordedPassCommandList>();

        try
        {
            for (var index = 0; index < OrderedPasses.Length; index++)
            {
                var passId = OrderedPasses[index];
                var passKind = OrderedPassKinds[index];
                var callback = OrderedPassCallbacks[index];
                var builtInSpriteState = BuiltInSpriteState;
                RenderSpriteC0GraphPass? spritePass = null;
                RenderMeshC0GraphPass? meshPass = null;
                RenderSpriteToneMapGraphPass? toneMapPass = null;
                var hasSpritePass = (builtInSpriteState is not null)
                    && builtInSpriteState.Passes.TryGetValue(passId, out spritePass);
                var hasMeshPass = (builtInSpriteState is not null)
                    && builtInSpriteState.MeshPasses.TryGetValue(passId, out meshPass);
                var hasToneMapPass = (builtInSpriteState is not null)
                    && builtInSpriteState.ToneMapPasses.TryGetValue(passId, out toneMapPass);

                if ((callback is null) && !hasSpritePass && !hasMeshPass && !hasToneMapPass)
                {
                    continue;
                }

                var commandRecorderPassKind = passKind switch {
                    RenderGraphPassKind.Copy => CommandRecorderPassKind.Copy,
                    RenderGraphPassKind.Compute => CommandRecorderPassKind.Compute,
                    RenderGraphPassKind.Raster => CommandRecorderPassKind.Raster,
                    RenderGraphPassKind.Presentation or RenderGraphPassKind.OffscreenOutput
                        => throw new InvalidOperationException("A synthetic terminal-output pass cannot have a recording callback."),
                    _ => throw new InvalidOperationException("The graph plan contains an unsupported pass kind."),
                };
                var accesses = ImmutableArray.CreateBuilder<GraphResourceAccess>();
                var colorTargetFormats = ImmutableArray.CreateBuilder<TextureFormat>();
                TextureFormat? depthTargetFormat = null;

                foreach (var access in Accesses)
                {
                    if (access.PassId == passId)
                    {
                        accesses.Add(access);
                    }
                }

                foreach (var attachment in AttachmentWrites)
                {
                    if (attachment.PassId != passId)
                    {
                        continue;
                    }

                    if (attachment.IsDepth)
                    {
                        if (depthTargetFormat.HasValue)
                        {
                            throw new InvalidOperationException("A raster pass declared more than one depth attachment.");
                        }

                        depthTargetFormat = attachment.Version.Description.Format;
                    }
                    else
                    {
                        colorTargetFormats.Add(attachment.Version.Description.Format);
                    }
                }

                RecordedPassCommandList? commandList = null;

                try
                {
                    var selectedCallback = callback;

                    if (hasSpritePass)
                    {
                        var selectedSpritePass = spritePass
                            ?? throw new InvalidOperationException("The built-in Sprite pass state is missing.");
                        var selectedResources = builtInResources
                            ?? throw new InvalidOperationException("The built-in Sprite pass has no submission resources.");

                        void RecordSpritePass(scoped ref CommandRecorder recorder)
                        {
                            selectedSpritePass.Record(ref recorder, selectedResources);
                        }

                        selectedCallback = RecordSpritePass;
                    }
                    else if (hasMeshPass)
                    {
                        var selectedMeshPass = meshPass
                            ?? throw new InvalidOperationException("The built-in Mesh pass state is missing.");
                        var selectedResources = builtInResources
                            ?? throw new InvalidOperationException("The built-in Mesh pass has no submission resources.");

                        void RecordMeshPass(scoped ref CommandRecorder recorder)
                        {
                            selectedMeshPass.Record(ref recorder, selectedResources);
                        }

                        selectedCallback = RecordMeshPass;
                    }
                    else if (hasToneMapPass)
                    {
                        var selectedToneMapPass = toneMapPass
                            ?? throw new InvalidOperationException("The built-in Sprite tone-map pass state is missing.");
                        var selectedResources = builtInResources
                            ?? throw new InvalidOperationException("The built-in Sprite tone-map pass has no submission resources.");

                        void RecordToneMapPass(scoped ref CommandRecorder recorder)
                        {
                            selectedToneMapPass.Record(ref recorder, selectedResources);
                        }

                        selectedCallback = RecordToneMapPass;
                    }

                    commandList = await device.RecordPassAsync(
                        passId,
                        commandRecorderPassKind,
                        accesses.ToImmutable(),
                        colorTargetFormats.ToImmutable(),
                        depthTargetFormat,
                        selectedCallback
                            ?? throw new InvalidOperationException("The graph pass has no recording callback."))
                        .ConfigureAwait(false);
                    commandLists.Add(commandList);
                    commandList = null;
                }
                finally
                {
                    commandList?.Dispose();
                }
            }

            return commandLists.ToImmutable();
        }
        catch
        {
            foreach (var commandList in commandLists)
            {
                commandList.Dispose();
            }

            throw;
        }
    }
}
