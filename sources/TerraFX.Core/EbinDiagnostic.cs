// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

internal readonly record struct EbinDiagnostic(
    Guid SchemaId,
    uint Tag,
    string? Category,
    ulong Offset = 0,
    ulong Limit = 0,
    ulong Claim = 0);
