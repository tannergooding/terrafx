// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Threading;

namespace TerraFX.Entities;

/// <summary>Restores or rebuilds one component value through scoped candidate-state authority.</summary>
/// <typeparam name="T">The unmanaged component type.</typeparam>
/// <typeparam name="TAuthor">The symbolic author value type.</typeparam>
public interface IComponentRestoreProvider<T, TAuthor>
    where T : unmanaged
{
    /// <summary>Rebuilds one Derived component from the exact candidate dependencies.</summary>
    /// <param name="binding">The engine-issued candidate restore binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The rebuilt component value.</returns>
    T RebuildDerived(WorldStateRestoreBinding binding, CancellationToken cancellationToken);

    /// <summary>Restores one persistent or Reset component from its symbolic author value.</summary>
    /// <param name="author">The symbolic author value.</param>
    /// <param name="binding">The engine-issued candidate restore binding.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>The restored component value.</returns>
    T Restore(in TAuthor author, WorldStateRestoreBinding binding, CancellationToken cancellationToken);
}
