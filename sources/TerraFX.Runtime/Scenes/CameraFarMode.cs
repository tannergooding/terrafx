// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Scenes;

/// <summary>Specifies whether a Scene camera uses a finite or infinite far plane.</summary>
[SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "The byte values are fixed by the reviewed camera wire contract.")]
public enum CameraFarMode : byte
{
    /// <summary>Uses an explicitly supplied finite far distance.</summary>
    Finite = 0,

    /// <summary>Uses an infinite far plane; only perspective cameras admit this mode.</summary>
    Infinite = 1,
}
