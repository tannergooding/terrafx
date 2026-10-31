// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Scenes;

internal readonly record struct SceneExpandedEntity(
    Guid ExpandedEntityKey,
    Guid AuthoredEntityId,
    int OccurrenceIndex,
    int SourceEntityIndex,
    int ParentEntityIndex,
    int ChildOrdinal,
    int HierarchyDepth,
    ImmutableArray<SceneAuthoredComponentRecord> Components);
