// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Buffers;

namespace TerraFX.Graphics;

internal interface IGraphicsBackendBufferReadback : IDisposable
{
    long CompletionFence { get; }

    void CopyCompleted(BufferWriteLease<byte> destination);
}
