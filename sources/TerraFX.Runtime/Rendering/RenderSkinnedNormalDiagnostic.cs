// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Rendering;

/// <summary>Contains one fully validated private skinned-Mesh normal-degeneracy row.</summary>
internal readonly struct RenderSkinnedNormalDiagnostic : IEquatable<RenderSkinnedNormalDiagnostic>
{
    internal RenderSkinnedNormalDiagnostic(
        Guid snapshotId,
        Guid worldGuid,
        long tickIndex,
        Guid drawId,
        int testedVertexCount,
        int degenerateVertexCount)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(snapshotId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(tickIndex);
        ArgumentOutOfRangeException.ThrowIfEqual(drawId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(testedVertexCount);

        if ((degenerateVertexCount < 0) || (degenerateVertexCount > testedVertexCount))
        {
            throw new ArgumentOutOfRangeException(nameof(degenerateVertexCount));
        }

        DegenerateVertexCount = degenerateVertexCount;
        DrawId = drawId;
        SnapshotId = snapshotId;
        TestedVertexCount = testedVertexCount;
        TickIndex = tickIndex;
        WorldGuid = worldGuid;
    }

    /// <summary>Gets the count of tested vertices whose blended skinned normal was near zero.</summary>
    public int DegenerateVertexCount { get; }

    /// <summary>Gets the stable identity of the Mesh draw.</summary>
    public Guid DrawId { get; }

    /// <summary>Gets the submitted render-snapshot identity.</summary>
    public Guid SnapshotId { get; }

    /// <summary>Gets the number of unique referenced vertices tested for this draw.</summary>
    public int TestedVertexCount { get; }

    /// <summary>Gets the committed World tick represented by the submitted snapshot.</summary>
    public long TickIndex { get; }

    /// <summary>Gets the source World identity.</summary>
    public Guid WorldGuid { get; }

    /// <summary>Negates diagnostic value equality.</summary>
    public static bool operator !=(RenderSkinnedNormalDiagnostic left, RenderSkinnedNormalDiagnostic right)
        => !left.Equals(right);

    /// <summary>Compares diagnostic values.</summary>
    public static bool operator ==(RenderSkinnedNormalDiagnostic left, RenderSkinnedNormalDiagnostic right)
        => left.Equals(right);

    /// <inheritdoc />
    public bool Equals(RenderSkinnedNormalDiagnostic other)
        => (SnapshotId == other.SnapshotId)
            && (WorldGuid == other.WorldGuid)
            && (TickIndex == other.TickIndex)
            && (DrawId == other.DrawId)
            && (TestedVertexCount == other.TestedVertexCount)
            && (DegenerateVertexCount == other.DegenerateVertexCount);

    /// <inheritdoc />
    public override bool Equals(object? obj)
        => (obj is RenderSkinnedNormalDiagnostic other) && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => HashCode.Combine(SnapshotId, WorldGuid, TickIndex, DrawId, TestedVertexCount, DegenerateVertexCount);
}
