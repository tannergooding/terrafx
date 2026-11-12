// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Rendering;

internal readonly record struct RenderSpriteBatchKey(
    Guid SceneInstanceId,
    int TextureAssetId,
    int? MaterialAssetId);
