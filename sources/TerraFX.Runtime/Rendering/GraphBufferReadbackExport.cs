// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Graphics;

namespace TerraFX.Rendering;

internal readonly record struct GraphBufferReadbackExport(
    GraphBufferVersion Version,
    GraphBufferRange Range);
