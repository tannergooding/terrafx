// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Content.Tools;

internal sealed class ShaderSourceFile
{
    internal ShaderSourceFile(string fullPath, string relativePath, int rootIndex, byte[] contents)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
        RootIndex = rootIndex;
        Contents = contents;
    }

    internal byte[] Contents { get; }

    internal string FullPath { get; }

    internal string RelativePath { get; }

    internal int RootIndex { get; }
}
