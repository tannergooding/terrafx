// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

/// <summary>Describes a bounded non-destructive diagnostic snapshot query.</summary>
public readonly struct DiagnosticQuery : IEquatable<DiagnosticQuery>
{
    /// <summary>Initializes a diagnostic snapshot query.</summary>
    /// <param name="maximumRecords">The maximum number of returned records.</param>
    /// <param name="minimumSeverity">The minimum admitted severity.</param>
    /// <param name="worldGuid">The optional exact World filter.</param>
    /// <param name="minimumTick">The optional inclusive lower tick bound.</param>
    /// <exception cref="ArgumentOutOfRangeException">A count, severity, World identity, or tick bound is invalid.</exception>
    public DiagnosticQuery(int maximumRecords, DiagnosticSeverity minimumSeverity, Guid? worldGuid, long? minimumTick)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecords, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumRecords, 8192);

        if (minimumSeverity is < DiagnosticSeverity.Trace or > DiagnosticSeverity.Critical)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSeverity));
        }

        if (worldGuid == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(worldGuid));
        }

        if (minimumTick is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumTick));
        }

        MaximumRecords = maximumRecords;
        MinimumSeverity = minimumSeverity;
        WorldGuid = worldGuid;
        MinimumTick = minimumTick;
    }

    /// <summary>Gets the maximum number of returned records.</summary>
    public int MaximumRecords { get; }

    /// <summary>Gets the minimum admitted severity.</summary>
    public DiagnosticSeverity MinimumSeverity { get; }

    /// <summary>Gets the optional inclusive lower tick bound.</summary>
    public long? MinimumTick { get; }

    /// <summary>Gets the optional exact World filter.</summary>
    public Guid? WorldGuid { get; }

    /// <summary>Negates equality of the complete diagnostic query.</summary>
    public static bool operator !=(DiagnosticQuery left, DiagnosticQuery right) => !(left == right);

    /// <summary>Compares the complete diagnostic query.</summary>
    public static bool operator ==(DiagnosticQuery left, DiagnosticQuery right) => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(DiagnosticQuery other)
        => MaximumRecords == other.MaximumRecords
        && MinimumSeverity == other.MinimumSeverity
        && WorldGuid == other.WorldGuid
        && MinimumTick == other.MinimumTick;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DiagnosticQuery other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(MaximumRecords, MinimumSeverity, WorldGuid, MinimumTick);
}
