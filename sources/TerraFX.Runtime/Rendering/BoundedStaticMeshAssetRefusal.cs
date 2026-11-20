// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Rendering;

internal enum BoundedStaticMeshAssetRefusal : byte
{
    None = 0,
    UnsupportedRegistration = 1,
    UnsupportedToken = 2,
    UnsupportedContext = 3,
    ProfileUnavailable = 4,
    InvalidOptions = 5,
    Capacity = 6,
    Busy = 7,
    Closed = 8,
}
