// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class WorldSystemFactoryFamily<TSystem, TCreate, TRelease> :
    IWorldSystemFactoryDispatch,
    IWorldSystemCreateControl,
    IWorldSystemDefaultsControl,
    IWorldSystemReleaseControl,
    IWorldSystemValidationControl
    where TSystem : class, IWorldSystem
    where TCreate : struct, IWorldSystemCreate<TSystem>
    where TRelease : struct, IWorldSystemRelease<TSystem>
{
    private readonly TCreate _create;
    private readonly TRelease _release;

    internal WorldSystemFactoryFamily(
        WorldSystemFactoryDescriptor descriptor,
        TCreate create,
        TRelease release)
    {
        Descriptor = descriptor;
        _create = create;
        _release = release;
    }

    public IWorldSystemCreateControl CreateControl => this;

    public IWorldSystemDefaultsControl DefaultsControl => this;

    public WorldSystemFactoryDescriptor Descriptor { get; }

    public IWorldSystemReleaseControl ReleaseControl => this;

    public IWorldSystemValidationControl ValidationControl => this;

    private static TSystem ValidateSystem(IWorldSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        return system is TSystem typedSystem
            ? typedSystem
            : throw new InvalidOperationException("The World system instance does not match the captured closed type.");
    }

    IWorldSystem IWorldSystemCreateControl.Invoke()
        => _create.Invoke() ?? throw new InvalidOperationException("The system creation callback returned null.");

    void IWorldSystemDefaultsControl.Invoke(IWorldSystem system)
        => ValidateSystem(system);

    void IWorldSystemReleaseControl.Invoke(IWorldSystem system)
    {
        _release.Invoke(ValidateSystem(system));
    }

    void IWorldSystemValidationControl.Validate(IWorldSystem system, Guid systemId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(systemId, Guid.Empty);
        _ = ValidateSystem(system);
    }
}
