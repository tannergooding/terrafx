// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Initializes one newly claimed World system instance.</summary>
/// <typeparam name="TSystem">The exact closed World system type.</typeparam>
public interface IWorldSystemDefaults<TSystem>
    where TSystem : class, IWorldSystem
{
    /// <summary>Applies declared defaults to one fresh system instance.</summary>
    /// <param name="system">The exact newly claimed system instance.</param>
    void Invoke(TSystem system);
}
