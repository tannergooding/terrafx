// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Scenes;

/// <summary>Specifies the projection used by a Scene camera.</summary>
[SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "The byte values are fixed by the reviewed camera wire contract.")]
public enum CameraProjectionKind : byte
{
    /// <summary>Uses a perspective projection.</summary>
    Perspective = 0,

    /// <summary>Uses an orthographic projection.</summary>
    Orthographic = 1,
}
