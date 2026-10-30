// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal interface IComponentRestoreContext : IComponentProviderScope
{
    TDependency ReadSameEntity<TDependency>()
        where TDependency : unmanaged;

    Entity ResolveEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId);

    Entity ResolveEntity(WorldSystemFieldDescriptor referenceField, Guid targetEntityId);
}
