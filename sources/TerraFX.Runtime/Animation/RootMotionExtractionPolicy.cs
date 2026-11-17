// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Animation;

/// <summary>Identifies the root-motion extraction behavior attached to a copied request.</summary>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "The accepted root-motion contract uses only Extract=1; zero is intentionally invalid.")]
[SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "The byte value is fixed by the Animation root-motion resource schema.")]
public enum RootMotionExtractionPolicy : byte
{
    /// <summary>Extracts the root delta from the visual pose.</summary>
    Extract = 1,
}
