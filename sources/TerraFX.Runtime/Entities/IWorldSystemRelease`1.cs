// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Releases one claimed World system instance.</summary>
/// <typeparam name="TSystem">The concrete World system type.</typeparam>
public interface IWorldSystemRelease<TSystem>
    where TSystem : class, IWorldSystem
{
    /// <summary>Releases the World-owned system instance exactly once.</summary>
    /// <param name="system">The claimed system instance.</param>
    void Invoke(TSystem system);
}
