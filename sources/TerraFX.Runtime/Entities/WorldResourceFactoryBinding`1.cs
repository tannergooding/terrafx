// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Captures the erased registration facts for one typed resource family.</summary>
/// <typeparam name="T">The unmanaged resource partition type.</typeparam>
public sealed class WorldResourceFactoryBinding<T>
    where T : unmanaged
{
    private readonly ResourceFactoryControl<T> _control;

    internal WorldResourceFactoryBinding(ResourceFactoryControl<T> control)
    {
        _control = control;
    }

    /// <summary>Gets the immutable factory descriptor for registration and lookup.</summary>
    public WorldResourceFactoryDescriptor Descriptor => _control.Descriptor;

    internal ResourceFactoryControl<T> Control => _control;
}
