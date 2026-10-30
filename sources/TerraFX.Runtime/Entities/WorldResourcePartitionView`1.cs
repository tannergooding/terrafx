// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Provides scoped copy-only access to one World resource partition.</summary>
/// <typeparam name="T">The unmanaged resource partition type.</typeparam>
public readonly ref struct WorldResourcePartitionView<T>
    where T : unmanaged
{
    private readonly WorldResourcePartitionControl? _control;
    private readonly int _partitionIndex;

    internal WorldResourcePartitionView(WorldResourcePartitionControl control, int partitionIndex)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentOutOfRangeException.ThrowIfNegative(partitionIndex);
        _control = control;
        _partitionIndex = partitionIndex;
    }

    /// <summary>Gets the zero-based partition index.</summary>
    public int PartitionIndex
    {
        get
        {
            _ = GetControl();
            return _partitionIndex;
        }
    }

    /// <summary>Reads one copied resource partition value.</summary>
    /// <returns>The copied resource value.</returns>
    /// <exception cref="InvalidOperationException">The view has expired or is used from another thread.</exception>
    public T Read() => GetControl().Read<T>(_partitionIndex);

    /// <summary>Writes one resource partition value under a declared Write grant.</summary>
    /// <param name="value">The copied replacement value.</param>
    /// <exception cref="InvalidOperationException">The view has expired or has no Write grant.</exception>
    public void Write(in T value) => GetControl().Write(_partitionIndex, in value);

    private WorldResourcePartitionControl GetControl()
    {
        var control = _control
            ?? throw new InvalidOperationException("The default resource partition view is not issued.");
        control.EnsureValidView();
        return control;
    }
}
