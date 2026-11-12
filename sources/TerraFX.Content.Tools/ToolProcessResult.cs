// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Content.Tools;

internal readonly struct ToolProcessResult
{
    internal ToolProcessResult(int exitCode, string standardOutput, string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    internal int ExitCode { get; }

    internal string StandardError { get; }

    internal string StandardOutput { get; }
}
