// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal interface ICommandWriterDispatch
{
    void Add<T>(Entity entity, in T value)
        where T : unmanaged;

    void Add<T>(EntityToken entity, in T value)
        where T : unmanaged;

    void AddTag(Entity entity, System.Guid schemaId);

    void AddTag(EntityToken entity, System.Guid schemaId);

    EntityToken Create(EntityDescription description);

    void Destroy(Entity entity, HierarchyDestroyMode mode);

    void Destroy(EntityToken entity, HierarchyDestroyMode mode);

    void Remove<T>(Entity entity)
        where T : unmanaged;

    void Remove<T>(EntityToken entity)
        where T : unmanaged;

    void RemoveTag(Entity entity, System.Guid schemaId);

    void RemoveTag(EntityToken entity, System.Guid schemaId);

    void Set<T>(Entity entity, in T value)
        where T : unmanaged;

    void Set<T>(EntityToken entity, in T value)
        where T : unmanaged;

    void SetEntityReference<T>(Entity owner, ComponentFieldPath path, Entity? target)
        where T : unmanaged;

    void SetEntityReference<T>(Entity owner, ComponentFieldPath path, EntityToken? target)
        where T : unmanaged;

    void SetEntityReference<T>(EntityToken owner, ComponentFieldPath path, Entity? target)
        where T : unmanaged;

    void SetEntityReference<T>(EntityToken owner, ComponentFieldPath path, EntityToken? target)
        where T : unmanaged;
}
