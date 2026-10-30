// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal enum WorldResourceOperationKind : byte
{
    Validation = 1,
    Restore = 2,
    Count = 3,
    Write = 4,
}
