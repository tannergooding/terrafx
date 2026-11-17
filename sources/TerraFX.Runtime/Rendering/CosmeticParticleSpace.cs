// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Rendering;

/// <summary>Specifies the authored plane or camera-facing orientation of a cosmetic emitter.</summary>
[SuppressMessage(
    "Design",
    "CA1028:Enum Storage should be Int32",
    Justification = "The byte values are fixed by the reviewed cosmetic-particle schema contract.")]
public enum CosmeticParticleSpace : byte
{
    /// <summary>Uses the emitter's world XY plane and fixed spawn depth.</summary>
    PlaneXY = 0,

    /// <summary>Faces 3D cameras using their copied orientation.</summary>
    CameraFacing3D = 1,
}
