// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Buffers;

namespace TerraFX.Graphics;

public sealed partial class GraphicsDevice
{
    internal Task<OwnedBuffer<byte>> ReadbackBufferAsync(
        GpuBuffer source,
        long sourceOffset,
        nint byteLength,
        GraphicsBufferReadbackStorage completionStorage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completionStorage);

        if (completionStorage.Admission is null)
        {
            throw new InvalidOperationException("The private diagnostic readback requires a preclaimed Core completion allowance.");
        }

        return ReadbackBufferCoreAsync(source, sourceOffset, byteLength, completionStorage, cancellationToken);
    }
}
