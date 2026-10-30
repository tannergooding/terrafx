// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal sealed class ResourceFactoryControl<T>
    where T : unmanaged
{
    internal ResourceFactoryControl(IWorldResourceFactoryDispatch<T> dispatch)
    {
        Dispatch = dispatch;
    }

    internal WorldResourceFactoryDescriptor Descriptor => Dispatch.Descriptor;

    internal IWorldResourceFactoryDispatch<T> Dispatch { get; }
}
