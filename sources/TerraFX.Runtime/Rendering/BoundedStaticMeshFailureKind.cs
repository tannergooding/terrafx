// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Rendering;

internal enum BoundedStaticMeshFailureKind : byte
{
    None = 0,
    Native = 1,
    Digest = 2,
    Layout = 3,
    Profile = 4,
    TypedRecord = 5,
    Reference = 6,
    UnsupportedContent = 7,
    ManagedExecution = 8,
    Invariant = 9,
}
