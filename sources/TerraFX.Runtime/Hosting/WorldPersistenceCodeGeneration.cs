// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Hosting;

/// <summary>Identifies the code-generation values stored in the State2 runtime profile.</summary>
public enum WorldPersistenceCodeGeneration
{
    /// <summary>Just-in-time code generation.</summary>
    Jit = 0,

    /// <summary>Native ahead-of-time code generation.</summary>
    NativeAot = 1,
}
