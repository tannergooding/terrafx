// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

public sealed partial class RenderGraphPlan
{
    internal async Task<ImmutableArray<RecordedPassCommandList>> RecordPassCallbacksAsync()
    {
        var target = OffscreenColorTarget
            ?? throw new NotSupportedException("A surface-targeted graph cannot be recorded before its Surface executor is available.");
        var device = target.ResourceOwner.Device;

        if (device.DeviceEpoch != DeviceEpoch)
        {
            throw new InvalidOperationException("The graph plan belongs to a different GraphicsDevice generation.");
        }

        var commandLists = ImmutableArray.CreateBuilder<RecordedPassCommandList>();

        try
        {
            for (var index = 0; index < OrderedPasses.Length; index++)
            {
                var passId = OrderedPasses[index];
                var passKind = OrderedPassKinds[index];
                var callback = OrderedPassCallbacks[index];

                if (callback is null)
                {
                    continue;
                }

                var commandRecorderPassKind = passKind switch {
                    RenderGraphPassKind.Copy => CommandRecorderPassKind.Copy,
                    RenderGraphPassKind.Compute => CommandRecorderPassKind.Compute,
                    RenderGraphPassKind.Raster => CommandRecorderPassKind.Raster,
                    RenderGraphPassKind.Presentation or RenderGraphPassKind.OffscreenOutput
                        => throw new InvalidOperationException("A synthetic terminal-output pass cannot have a recording callback."),
                    _ => throw new ArgumentOutOfRangeException(nameof(passKind)),
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
                    commandList = await device.RecordPassAsync(
                        passId,
                        commandRecorderPassKind,
                        accesses.ToImmutable(),
                        colorTargetFormats.ToImmutable(),
                        depthTargetFormat,
                        callback).ConfigureAwait(false);
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
