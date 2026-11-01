// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

internal interface IWorldSceneParticipant
{
    Type HierarchyLocalTransformComponentType { get; }

    Type HierarchyRootPositionComponentType { get; }

    long OwnedByteReservation { get; }

    void Dispose();

    ImmutableArray<WorldHierarchyDetachUpdate> PrepareHierarchyDetachment(
        int ancestorNodeCount,
        ReadOnlyMemory<WorldHierarchyNodeSnapshot> nodes);

    void PumpControlBoundary();
}
