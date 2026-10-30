// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal interface IWorldSystemFieldAccessor<TSystem, TAuthor>
    where TSystem : class, IWorldSystem
    where TAuthor : notnull
{
    void ValidateAuthor(in TAuthor author);

    void ValidateRuntime(in TSystem system);
}
