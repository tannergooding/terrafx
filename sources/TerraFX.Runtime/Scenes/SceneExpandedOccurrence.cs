// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Assets;

namespace TerraFX.Scenes;

internal readonly record struct SceneExpandedOccurrence(
    AssetId AssetId,
    Guid OccurrenceId,
    int ParentOccurrenceIndex,
    Guid PlacementId,
    int PrefabDepth,
    int EntityMapStart,
    int EntityCount,
    int ChildPlacementMapStart,
    int PlacementCount,
    int RootEntityIndicesStart,
    int RootEntityCount);
