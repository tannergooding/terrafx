// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Generic;
using System;

namespace TerraFX.Content.Tools;

internal sealed class ShaderCompileRequest
{
    internal ShaderCompileRequest(
        string sourcePath,
        string outputDirectory,
        string entryPoint,
        string stage,
        string dxcPath,
        string vulkanSdkPath,
        IReadOnlyList<string> includeRoots,
        IReadOnlyList<string> defines,
        bool debug,
        TimeSpan timeout)
    {
        SourcePath = sourcePath;
        OutputDirectory = outputDirectory;
        EntryPoint = entryPoint;
        Stage = stage;
        DxcPath = dxcPath;
        VulkanSdkPath = vulkanSdkPath;
        IncludeRoots = includeRoots;
        Defines = defines;
        Debug = debug;
        Timeout = timeout;
    }

    internal bool Debug { get; }

    internal IReadOnlyList<string> Defines { get; }

    internal string DxcPath { get; }

    internal string EntryPoint { get; }

    internal IReadOnlyList<string> IncludeRoots { get; }

    internal string OutputDirectory { get; }

    internal string SourcePath { get; }

    internal string Stage { get; }

    internal TimeSpan Timeout { get; }

    internal string VulkanSdkPath { get; }
}
