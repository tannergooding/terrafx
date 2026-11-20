// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Rendering;

internal enum BoundedStaticMeshFileDisposition : byte
{
    NotAttempted = 0,
    NotFinal = 1,
    Consumed = 2,
    ConsumedWithError = 3,
    KnownRetained = 4,
    Indeterminate = 5,
}
