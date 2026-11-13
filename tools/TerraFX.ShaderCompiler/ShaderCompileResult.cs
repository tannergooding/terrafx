// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Generic;

namespace TerraFX.Content.Tools;

internal sealed class ShaderCompileResult
{
    internal ShaderCompileResult(
        string fingerprint,
        IReadOnlyList<string> warnings,
        string dxilPath,
        string spirvPath,
        string dxilReflectionPath,
        string spirvReflectionPath)
    {
        Fingerprint = fingerprint;
        Warnings = warnings;
        DxilPath = dxilPath;
        SpirvPath = spirvPath;
        DxilReflectionPath = dxilReflectionPath;
        SpirvReflectionPath = spirvReflectionPath;
    }

    internal string DxilPath { get; }

    internal string DxilReflectionPath { get; }

    internal string Fingerprint { get; }

    internal string SpirvPath { get; }

    internal string SpirvReflectionPath { get; }

    internal IReadOnlyList<string> Warnings { get; }
}
