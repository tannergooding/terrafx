// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Scenes;

internal enum SceneHierarchyValidationError : byte
{
    None = 0,
    InvalidWorkspace,
    EntityCountMismatch,
    InvalidHierarchy,
    EmptyEntityId,
    DuplicateEntityId,
    EntityNodeMismatch,
    EmptyPlacementId,
    DuplicatePlacementId,
    InvalidParent,
    InvalidEntry,
    MissingEntity,
    MissingPlacement,
    ParentMismatch,
    DuplicateMembership,
    MissingMembership,
    Cycle,
    DepthExceeded,
}
