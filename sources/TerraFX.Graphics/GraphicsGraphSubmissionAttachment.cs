// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Numerics;

namespace TerraFX.Graphics;

internal readonly record struct GraphicsGraphSubmissionAttachment(
    GraphTextureVersion Version,
    GraphTextureRange Range,
    AttachmentLoadAction LoadAction,
    AttachmentStoreAction StoreAction,
    Vector4 ClearColor,
    float ClearDepth,
    bool IsDepth);
