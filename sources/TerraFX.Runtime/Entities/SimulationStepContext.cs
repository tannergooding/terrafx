// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

/// <summary>Provides immutable timing metadata for one simulation step.</summary>
public readonly struct SimulationStepContext : IEquatable<SimulationStepContext>
{
    /// <summary>Initializes timing metadata for one executed simulation tick.</summary>
    /// <param name="tickIndex">The zero-based index of the executing tick.</param>
    /// <param name="tickRate">The fixed number of ticks per second.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickIndex"/> is negative or <paramref name="tickRate"/> is outside the supported range.</exception>
    internal SimulationStepContext(long tickIndex, int tickRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tickIndex);
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tickRate, 1000);

        TickIndex = tickIndex;
        TickRate = tickRate;
        FixedDeltaTimeSeconds = 1.0f / tickRate;
    }

    /// <summary>Gets the fixed duration of one tick in seconds.</summary>
    public float FixedDeltaTimeSeconds { get; }

    /// <summary>Gets the zero-based index of the executing tick.</summary>
    public long TickIndex { get; }

    /// <summary>Gets the fixed number of ticks per second.</summary>
    public int TickRate { get; }

    /// <summary>Negates equality of the complete simulation-step metadata.</summary>
    public static bool operator !=(SimulationStepContext left, SimulationStepContext right) => !(left == right);

    /// <summary>Compares the complete simulation-step metadata.</summary>
    public static bool operator ==(SimulationStepContext left, SimulationStepContext right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(SimulationStepContext other) =>
        (TickIndex == other.TickIndex)
        && (TickRate == other.TickRate)
        && FixedDeltaTimeSeconds.Equals(other.FixedDeltaTimeSeconds);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is SimulationStepContext other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(TickIndex, TickRate, FixedDeltaTimeSeconds);
}
