// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal static class RenderSpriteGeometry
{
    private static readonly ImmutableArray<ushort> s_indices = [0, 1, 2, 0, 2, 3];

    internal static ImmutableArray<ushort> Indices => s_indices;

    internal static RenderSpriteQuad Create(
        Vector2 pivot,
        Vector2 size,
        byte flipMask,
        RenderAtlasFrame frame,
        int pageWidth,
        int pageHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageHeight);

        if (!float.IsFinite(pivot.X) || !float.IsFinite(pivot.Y)
            || (pivot.X < 0.0f) || (pivot.X > 1.0f)
            || (pivot.Y < 0.0f) || (pivot.Y > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(pivot));
        }

        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y)
            || (size.X <= 0.0f) || (size.Y <= 0.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        if ((flipMask & ~0x03) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flipMask));
        }

        if ((frame.FrameId == Guid.Empty)
            || (frame.Page < 0)
            || (frame.X < 0)
            || (frame.Y < 0)
            || (frame.Width <= 0)
            || (frame.Height <= 0)
            || (frame.Padding < 0)
            || (frame.X < frame.Padding)
            || (frame.Y < frame.Padding)
            || (((long)frame.X + frame.Width + frame.Padding) > pageWidth)
            || (((long)frame.Y + frame.Height + frame.Padding) > pageHeight))
        {
            throw new ArgumentException("The Atlas frame must be a nonempty in-bounds page rectangle.", nameof(frame));
        }

        var left = -pivot.X * size.X;
        var bottom = -pivot.Y * size.Y;
        var right = (1.0f - pivot.X) * size.X;
        var top = (1.0f - pivot.Y) * size.Y;
        var leftU = (float)frame.X / pageWidth;
        var rightU = (float)((long)frame.X + frame.Width) / pageWidth;
        var topV = (float)frame.Y / pageHeight;
        var bottomV = (float)((long)frame.Y + frame.Height) / pageHeight;

        if ((flipMask & 0x01) != 0)
        {
            (leftU, rightU) = (rightU, leftU);
        }

        if ((flipMask & 0x02) != 0)
        {
            (topV, bottomV) = (bottomV, topV);
        }

        return new RenderSpriteQuad(
            new RenderSpriteVertex(new Vector3(left, bottom, 0.0f), new Vector2(leftU, bottomV)),
            new RenderSpriteVertex(new Vector3(left, top, 0.0f), new Vector2(leftU, topV)),
            new RenderSpriteVertex(new Vector3(right, top, 0.0f), new Vector2(rightU, topV)),
            new RenderSpriteVertex(new Vector3(right, bottom, 0.0f), new Vector2(rightU, bottomV)));
    }
}
