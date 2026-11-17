// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Animation;

/// <summary>Identifies the traversal direction of an Animation timeline.</summary>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "The accepted animation contract uses only Reverse=-1 and Forward=1; zero is intentionally invalid.")]
[SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "The signed byte values are fixed by the Animation event contract.")]
public enum AnimationDirection : sbyte
{
    /// <summary>Traverses increasing clip time.</summary>
    Forward = 1,

    /// <summary>Traverses decreasing clip time.</summary>
    Reverse = -1,
}
