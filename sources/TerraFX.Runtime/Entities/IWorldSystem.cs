// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

/// <summary>Defines one World-owned system that executes at its registered stage.</summary>
public interface IWorldSystem
{
    /// <summary>Executes one admitted fixed simulation tick.</summary>
    /// <param name="context">The engine-issued invocation context.</param>
    void Execute(WorldSystemContext context);
}
