// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal readonly record struct WorldHierarchyParentMutation(
    Entity Child,
    Entity? Parent,
    int FinalDepth,
    int FinalSubtreeHeight,
    int OperationOrder);
