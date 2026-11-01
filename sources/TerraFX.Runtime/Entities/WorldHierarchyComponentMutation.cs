// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly record struct WorldHierarchyComponentMutation(
    Entity Entity,
    Type ComponentType,
    WorldHierarchyComponentMutationKind Kind,
    ComponentInitialization? Initialization);
