// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

internal enum GraphicsCommandKind
{
    CopyBuffer,
    Dispatch,
    Draw,
    DrawIndexed,
    SetBindings,
    SetIndexBuffer,
    SetPipeline,
    SetVertexBuffer,
    StorageBarrier,
}
