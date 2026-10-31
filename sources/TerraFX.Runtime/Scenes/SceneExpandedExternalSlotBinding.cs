// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Scenes;

internal readonly record struct SceneExpandedExternalSlotBinding(
    int OccurrenceIndex,
    Guid SlotId,
    int? TargetExpandedEntityIndex,
    Guid? RootExternalSlotId);
