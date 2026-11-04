// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Hosting;

/// <summary>Identifies the x64 instruction-set flags stored in State2.</summary>
[Flags]
public enum WorldPersistenceInstructionSet
{
    /// <summary>No optional instruction-set flags.</summary>
    None = 0,

    /// <summary>SSE2.</summary>
    Sse2 = 1,

    /// <summary>AVX.</summary>
    Avx = 2,

    /// <summary>AVX2.</summary>
    Avx2 = 4,

    /// <summary>AVX-512F.</summary>
    Avx512F = 8,
}
