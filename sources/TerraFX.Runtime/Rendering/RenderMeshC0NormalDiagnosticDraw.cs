// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Rendering;

internal readonly record struct RenderMeshC0NormalDiagnosticDraw(
    Guid DrawId,
    int DrawIndex,
    int MeshIndex,
    int PaletteBase,
    int ResultRow,
    int TestedVertexCount,
    long WorkOffset,
    long WorkByteLength) : IComparable<RenderMeshC0NormalDiagnosticDraw>
{
    public int CompareTo(RenderMeshC0NormalDiagnosticDraw other)
        => FoundationCatalogue.CompareRfc(DrawId, other.DrawId);
}
