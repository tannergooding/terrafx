// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Contains the immutable public facts for one registered component factory.</summary>
public sealed class ComponentFactoryDescriptor
{
    /// <summary>Initializes an immutable component factory descriptor.</summary>
    /// <param name="schema">The exact component schema tuple.</param>
    /// <param name="layout">The component physical layout.</param>
    /// <param name="storage">The complete component storage declaration.</param>
    /// <param name="restore">The transient restore policy.</param>
    /// <param name="flags">The component storage class.</param>
    /// <param name="factoryId">The nonempty factory identity.</param>
    /// <param name="factoryVersion">The positive factory version.</param>
    /// <param name="fields">The complete ordered field bindings.</param>
    /// <param name="derivedDependencies">The exact persistent Derived dependencies.</param>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    internal ComponentFactoryDescriptor(
        ContentSchemaReference schema,
        ComponentLayout layout,
        ComponentStorageDeclaration storage,
        ComponentTransientRestore restore,
        ComponentStorageFlags flags,
        Guid factoryId,
        int factoryVersion,
        ImmutableArray<ComponentFieldDescriptor> fields,
        ImmutableArray<ComponentDerivedDependency> derivedDependencies)
    {
        ArgumentNullException.ThrowIfNull(storage);

        foreach (var field in fields)
        {
            ArgumentNullException.ThrowIfNull(field);
        }

        foreach (var dependency in derivedDependencies)
        {
            ArgumentNullException.ThrowIfNull(dependency);
        }

        Schema = schema;
        Layout = layout;
        Storage = storage;
        Restore = restore;
        Flags = flags;
        FactoryId = factoryId;
        FactoryVersion = factoryVersion;
        Fields = ImmutableArrayCopy.Create(fields);
        DerivedDependencies = ImmutableArrayCopy.Create(derivedDependencies);
    }

    /// <summary>Gets the complete immutable Derived dependency set.</summary>
    public ImmutableArray<ComponentDerivedDependency> DerivedDependencies { get; }

    /// <summary>Gets the stable factory identity.</summary>
    public Guid FactoryId { get; }

    /// <summary>Gets the positive factory version.</summary>
    public int FactoryVersion { get; }

    /// <summary>Gets the complete ordered field descriptor set.</summary>
    public ImmutableArray<ComponentFieldDescriptor> Fields { get; }

    /// <summary>Gets the component's selected storage class.</summary>
    public ComponentStorageFlags Flags { get; }

    /// <summary>Gets the physical component layout.</summary>
    public ComponentLayout Layout { get; }

    /// <summary>Gets the transient restore policy.</summary>
    public ComponentTransientRestore Restore { get; }

    /// <summary>Gets the exact component schema tuple.</summary>
    public ContentSchemaReference Schema { get; }

    /// <summary>Gets the complete managed storage declaration.</summary>
    public ComponentStorageDeclaration Storage { get; }
}
