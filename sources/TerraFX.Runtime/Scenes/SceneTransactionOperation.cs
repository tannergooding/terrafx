// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Scenes;

internal readonly record struct SceneTransactionOperation(
    SceneTransactionOperationKind Kind,
    Entity Entity,
    Entity? Parent,
    ReparentMode Mode,
    SpatialPosition3 RootPosition);
