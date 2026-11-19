// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal sealed class RenderSpriteGraphState
{
    internal RenderSpriteGraphState(
        TextureFormat spriteColorTargetFormat,
        TextureFormat toneMapColorTargetFormat,
        ImmutableArray<RenderSpriteGraphBufferUpload> bufferUploads,
        ImmutableArray<RenderSpriteGraphDeferredTexture> deferredTextures,
        ImmutableArray<RenderMeshGraphDeferredTexture> meshDeferredTextures,
        ImmutableDictionary<RenderPassId, RenderSpriteC0GraphPass> passes,
        ImmutableDictionary<RenderPassId, RenderMeshC0GraphPass> meshPasses,
        ImmutableDictionary<RenderPassId, RenderMeshC0ShadowGraphPass> meshShadowPasses,
        ImmutableDictionary<RenderPassId, RenderSpriteToneMapGraphPass> toneMapPasses,
        RenderPassId? meshNormalDiagnosticPassId,
        RenderMeshC0NormalDiagnosticGraphPass? meshNormalDiagnosticPass)
    {
        if (!Enum.IsDefined(spriteColorTargetFormat) || !Enum.IsDefined(toneMapColorTargetFormat))
        {
            throw new ArgumentOutOfRangeException(nameof(spriteColorTargetFormat));
        }

        if (bufferUploads.IsDefault
            || deferredTextures.IsDefault
            || meshDeferredTextures.IsDefault
            || passes is null
            || meshPasses is null
            || meshShadowPasses is null
            || toneMapPasses is null
            || (meshNormalDiagnosticPassId.HasValue != meshNormalDiagnosticPass.HasValue))
        {
            throw new ArgumentException("Built-in Sprite graph state must be initialized.");
        }

        if (meshNormalDiagnosticPassId is RenderPassId passId
            && (!passId.IsValid || (meshNormalDiagnosticPass?.DrawCount is not > 0)))
        {
            throw new ArgumentException("The built-in Mesh diagnostic pass identity and state must be valid.");
        }

        SpriteColorTargetFormat = spriteColorTargetFormat;
        ToneMapColorTargetFormat = toneMapColorTargetFormat;
        BufferUploads = bufferUploads;
        DeferredTextures = deferredTextures;
        MeshDeferredTextures = meshDeferredTextures;
        Passes = passes;
        MeshPasses = meshPasses;
        MeshNormalDiagnosticPassId = meshNormalDiagnosticPassId;
        MeshNormalDiagnosticPass = meshNormalDiagnosticPass;
        MeshShadowPasses = meshShadowPasses;
        ToneMapPasses = toneMapPasses;
    }

    internal ImmutableArray<RenderSpriteGraphBufferUpload> BufferUploads { get; }

    internal ImmutableArray<RenderSpriteGraphDeferredTexture> DeferredTextures { get; }

    internal ImmutableArray<RenderMeshGraphDeferredTexture> MeshDeferredTextures { get; }

    internal RenderMeshC0NormalDiagnosticGraphPass? MeshNormalDiagnosticPass { get; }

    internal RenderPassId? MeshNormalDiagnosticPassId { get; }

    internal ImmutableDictionary<RenderPassId, RenderMeshC0GraphPass> MeshPasses { get; }

    internal ImmutableDictionary<RenderPassId, RenderMeshC0ShadowGraphPass> MeshShadowPasses { get; }

    internal ImmutableDictionary<RenderPassId, RenderSpriteC0GraphPass> Passes { get; }

    internal TextureFormat SpriteColorTargetFormat { get; }

    internal TextureFormat ToneMapColorTargetFormat { get; }

    internal ImmutableDictionary<RenderPassId, RenderSpriteToneMapGraphPass> ToneMapPasses { get; }
}
