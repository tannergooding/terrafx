// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal readonly struct RenderGraphPassNode
{
    internal RenderGraphPassNode(RenderPassId id, RenderPassDescription description)
    {
        if (!id.IsValid)
        {
            throw new ArgumentException("The graph pass ID must be valid.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(description);
        Id = id;
        Description = description;
    }

    internal RenderPassDescription Description { get; }

    internal RenderPassId Id { get; }
}
