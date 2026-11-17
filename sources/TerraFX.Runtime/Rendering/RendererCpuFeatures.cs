// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Rendering;

/// <summary>Specifies the optional M05 CPU-owned rendering feature families.</summary>
[Flags]
[SuppressMessage(
    "Design",
    "CA1008:Enums should have zero value",
    Justification = "The reviewed M05 CPU feature contract uses Static=0 for the existing composition.")]
public enum RendererCpuFeatures
{
    /// <summary>Selects the existing static rendering composition without M05 codecs.</summary>
    Static = 0,

    /// <summary>Selects Animation playback and animated-pose support.</summary>
    Animation = 1,

    /// <summary>Selects cosmetic particle emitter content.</summary>
    CosmeticParticles = 2,

    /// <summary>Selects authored Mesh LOD content.</summary>
    MeshLod = 4,

    /// <summary>Selects the visual picking Material content path.</summary>
    Picking = 8,
}
