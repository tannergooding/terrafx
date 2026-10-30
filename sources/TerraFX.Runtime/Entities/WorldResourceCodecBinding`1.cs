// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Pairs one resource codec with the exact captured typed factory family.</summary>
/// <typeparam name="T">The unmanaged resource partition type.</typeparam>
[SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "The closed resource type is required by the typed factory/codec family contract.")]
public sealed class WorldResourceCodecBinding<T>
    where T : unmanaged
{
    private readonly IWorldResourceStateCodec<T> _codec;
    private readonly ResourceFactoryControl<T> _control;
    private readonly IWorldResourceCodecDispatch<T> _dispatch;

    private WorldResourceCodecBinding(
        ResourceFactoryControl<T> control,
        IWorldResourceStateCodec<T> codec,
        IWorldResourceCodecDispatch<T> dispatch)
    {
        _control = control;
        _codec = codec;
        _dispatch = dispatch;
    }

    /// <summary>Gets the immutable descriptor shared with the captured factory family.</summary>
    public WorldResourceFactoryDescriptor Descriptor => _control.Descriptor;

    internal IWorldResourceStateCodec<T> Codec => _codec;

    internal ResourceFactoryControl<T> Control => _control;

    internal IWorldResourceCodecDispatch<T> Dispatch => _dispatch;

    /// <summary>Creates a codec binding paired with one typed factory family.</summary>
    /// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
    /// <param name="factoryBinding">The typed factory binding that owns the selected family.</param>
    /// <param name="codec">The concrete typed codec implementation.</param>
    /// <returns>The immutable codec binding.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The codec binding retains the captured family and callback object and owns no disposable resource.")]
    public static WorldResourceCodecBinding<T> Create<TAuthor>(
        WorldResourceFactoryBinding<T, TAuthor> factoryBinding,
        IWorldResourceStateCodec<T, TAuthor> codec)
        where TAuthor : notnull
    {
        ArgumentNullException.ThrowIfNull(factoryBinding);
        ArgumentNullException.ThrowIfNull(codec);
        var family = factoryBinding.Family;
        var dispatch = new WorldResourceCodecDispatch<T, TAuthor>(family, codec);
        return new WorldResourceCodecBinding<T>(family.Control, codec, dispatch);
    }
}
