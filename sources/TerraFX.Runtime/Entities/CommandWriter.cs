// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Admits typed structural commands from one active system invocation.</summary>
public sealed class CommandWriter
{
    private readonly ICommandWriterDispatch _dispatch;

    internal CommandWriter(ICommandWriterDispatch dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        _dispatch = dispatch;
    }

    internal ICommandWriterDispatch Dispatch => _dispatch;

    /// <summary>Adds a component to a live Entity.</summary>
    public void Add<T>(Entity entity, in T value)
        where T : unmanaged
        => _dispatch.Add(entity, in value);

    /// <summary>Adds a component to an Entity created by this command prefix.</summary>
    public void Add<T>(EntityToken entity, in T value)
        where T : unmanaged
        => _dispatch.Add(entity, in value);

    /// <summary>Adds a registered tag to a live Entity.</summary>
    public void AddTag(Entity entity, Guid schemaId) => _dispatch.AddTag(entity, schemaId);

    /// <summary>Adds a registered tag to an Entity created by this command prefix.</summary>
    public void AddTag(EntityToken entity, Guid schemaId) => _dispatch.AddTag(entity, schemaId);

    /// <summary>Creates one temporary Entity token.</summary>
    /// <param name="description">The immutable Entity initialization.</param>
    /// <returns>The token for this writer's admitted creation.</returns>
    public EntityToken Create(EntityDescription description) => _dispatch.Create(description);

    /// <summary>Destroys a live Entity with the selected hierarchy policy.</summary>
    public void Destroy(Entity entity, HierarchyDestroyMode mode) => _dispatch.Destroy(entity, mode);

    /// <summary>Destroys a token-created Entity with the selected hierarchy policy.</summary>
    public void Destroy(EntityToken entity, HierarchyDestroyMode mode) => _dispatch.Destroy(entity, mode);

    /// <summary>Removes a registered component from a live Entity.</summary>
    public void Remove<T>(Entity entity)
        where T : unmanaged
        => _dispatch.Remove<T>(entity);

    /// <summary>Removes a registered component from a token-created Entity.</summary>
    public void Remove<T>(EntityToken entity)
        where T : unmanaged
        => _dispatch.Remove<T>(entity);

    /// <summary>Removes a registered tag from a live Entity.</summary>
    public void RemoveTag(Entity entity, Guid schemaId) => _dispatch.RemoveTag(entity, schemaId);

    /// <summary>Removes a registered tag from a token-created Entity.</summary>
    public void RemoveTag(EntityToken entity, Guid schemaId) => _dispatch.RemoveTag(entity, schemaId);

    /// <summary>Sets a component on a live Entity.</summary>
    public void Set<T>(Entity entity, in T value)
        where T : unmanaged
        => _dispatch.Set(entity, in value);

    /// <summary>Sets a component on a token-created Entity.</summary>
    public void Set<T>(EntityToken entity, in T value)
        where T : unmanaged
        => _dispatch.Set(entity, in value);

    /// <summary>Sets an Entity-reference field from one live Entity owner to a live target or clears an optional field.</summary>
    public void SetEntityReference<T>(Entity owner, ComponentFieldPath path, Entity? target)
        where T : unmanaged
        => _dispatch.SetEntityReference<T>(owner, path, target);

    /// <summary>Sets an Entity-reference field from one live Entity owner to a token target or clears an optional field.</summary>
    public void SetEntityReference<T>(Entity owner, ComponentFieldPath path, EntityToken? target)
        where T : unmanaged
        => _dispatch.SetEntityReference<T>(owner, path, target);

    /// <summary>Sets an Entity-reference field from a token owner to a live target or clears an optional field.</summary>
    public void SetEntityReference<T>(EntityToken owner, ComponentFieldPath path, Entity? target)
        where T : unmanaged
        => _dispatch.SetEntityReference<T>(owner, path, target);

    /// <summary>Sets an Entity-reference field from a token owner to a token target or clears an optional field.</summary>
    public void SetEntityReference<T>(EntityToken owner, ComponentFieldPath path, EntityToken? target)
        where T : unmanaged
        => _dispatch.SetEntityReference<T>(owner, path, target);
}
