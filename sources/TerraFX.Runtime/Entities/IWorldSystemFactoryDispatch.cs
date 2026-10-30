// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal interface IWorldSystemFactoryDispatch
{
    IWorldSystemCreateControl CreateControl { get; }

    IWorldSystemDefaultsControl DefaultsControl { get; }

    WorldSystemFactoryDescriptor Descriptor { get; }

    IWorldSystemReleaseControl ReleaseControl { get; }

    IWorldSystemValidationControl ValidationControl { get; }
}
