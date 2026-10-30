// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Input;

/// <summary>Defines an action map in the version-one input schema.</summary>
public sealed class InputActionMapV1
{
    /// <summary>Initializes the immutable action-map values.</summary>
    /// <param name="schemaVersion">The action-map schema version.</param>
    /// <param name="mapId">The stable map identifier.</param>
    /// <param name="revision">The positive map revision.</param>
    /// <param name="initialActiveContextMask">The initial active context mask.</param>
    /// <param name="actions">The ordered action definitions.</param>
    /// <param name="contexts">The ordered context definitions.</param>
    /// <param name="bindings">The ordered binding definitions.</param>
    public InputActionMapV1(
        int schemaVersion,
        Guid mapId,
        long revision,
        ulong initialActiveContextMask,
        ImmutableArray<ActionDefinition> actions,
        ImmutableArray<ContextDefinition> contexts,
        ImmutableArray<BindingDefinition> bindings)
    {
        SchemaVersion = schemaVersion;
        MapId = mapId;
        Revision = revision;
        InitialActiveContextMask = initialActiveContextMask;
        Actions = actions;
        Contexts = contexts;
        Bindings = bindings;
    }

    /// <summary>Gets the ordered action definitions.</summary>
    public ImmutableArray<ActionDefinition> Actions { get; }

    /// <summary>Gets the ordered binding definitions.</summary>
    public ImmutableArray<BindingDefinition> Bindings { get; }

    /// <summary>Gets the ordered context definitions.</summary>
    public ImmutableArray<ContextDefinition> Contexts { get; }

    /// <summary>Gets the initial active context mask.</summary>
    public ulong InitialActiveContextMask { get; }

    /// <summary>Gets the stable map identifier.</summary>
    public Guid MapId { get; }

    /// <summary>Gets the positive map revision.</summary>
    public long Revision { get; }

    /// <summary>Gets the action-map schema version.</summary>
    public int SchemaVersion { get; }
}
