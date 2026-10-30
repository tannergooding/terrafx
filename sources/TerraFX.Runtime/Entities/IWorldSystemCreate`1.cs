// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Creates one fresh instance of a closed World system type.</summary>
/// <typeparam name="TSystem">The concrete World system type.</typeparam>
public interface IWorldSystemCreate<TSystem>
    where TSystem : class, IWorldSystem
{
    /// <summary>Creates the World-owned system instance.</summary>
    /// <returns>The fresh system instance.</returns>
    TSystem Invoke();
}
