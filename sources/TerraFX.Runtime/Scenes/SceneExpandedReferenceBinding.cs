// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;
using TerraFX;

namespace TerraFX.Scenes;

internal readonly record struct SceneExpandedReferenceBinding(
    int ExpandedEntityIndex,
    SceneEntitySelectorScope SourceScope,
    ContentSchemaReference ComponentSchema,
    ImmutableArray<ScenePropertyPathSegment> PropertyPath,
    int OwnerOccurrenceIndex,
    int TargetAnchorOccurrenceIndex,
    SceneReferenceTarget Target,
    bool IsPresent,
    int? ResolvedTargetExpandedEntityIndex);
