// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal sealed class GraphicsTransientBufferGroup : IDisposable
{
    internal GraphicsTransientBufferGroup(ImmutableArray<GpuBuffer> buffers)
    {
        if (buffers.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A materialized transient-buffer group requires at least one buffer.", nameof(buffers));
        }

        Buffers = [.. buffers];
    }

    internal ImmutableArray<GpuBuffer> Buffers { get; }

    public void Dispose()
    {
        foreach (var buffer in Buffers)
        {
            buffer.Dispose();
        }
    }
}
