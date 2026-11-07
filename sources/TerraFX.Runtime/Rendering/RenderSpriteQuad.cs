// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Rendering;

internal readonly record struct RenderSpriteQuad(
    RenderSpriteVertex Vertex0,
    RenderSpriteVertex Vertex1,
    RenderSpriteVertex Vertex2,
    RenderSpriteVertex Vertex3);
