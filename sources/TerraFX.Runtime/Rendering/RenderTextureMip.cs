// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;

namespace TerraFX.Rendering;

internal readonly record struct RenderTextureMip(
    int Level,
    int Width,
    int Height,
    ImmutableArray<byte> Pixels);
