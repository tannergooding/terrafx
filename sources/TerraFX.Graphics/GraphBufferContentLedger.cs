// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX.Graphics;

internal sealed class GraphBufferContentLedger
{
    private readonly Dictionary<GraphBufferVersion, GraphBufferRangeCoverage> _coverageByVersion = [];
    private GraphBufferVersion _currentVersion;

    internal GraphBufferContentLedger(GraphBufferVersion initialVersion, bool contentsValid)
    {
        ArgumentNullException.ThrowIfNull(initialVersion);
        _currentVersion = initialVersion;
        var coverage = new GraphBufferRangeCoverage();

        if (contentsValid)
        {
            coverage.Add(initialVersion.Range);
        }

        _coverageByVersion.Add(initialVersion, coverage);
    }

    private static GraphBufferRange ValidateRange(GraphBufferVersion version, GraphBufferRange range)
    {
        if (!range.IsWithin(version.Range))
        {
            throw new ArgumentException("The buffer range must fit its graph version.", nameof(range));
        }

        return range;
    }

    internal bool IsInitialized(GraphBufferVersion version, GraphBufferRange range)
        => GetCoverage(version).Covers(ValidateRange(version, range));

    internal GraphBufferVersion Write(GraphBufferVersion previousVersion, GraphBufferRange range)
    {
        var previousCoverage = GetCoverage(previousVersion);
        range = ValidateRange(previousVersion, range);

        if (!ReferenceEquals(previousVersion, _currentVersion))
        {
            throw new InvalidOperationException("A buffer write must advance the current graph version.");
        }

        var nextVersion = new GraphBufferVersion(
            previousVersion.DeviceEpoch,
            previousVersion.ResourceId,
            checked(previousVersion.Version + 1),
            previousVersion.Description,
            previousVersion.Range);
        var nextCoverage = previousCoverage.Clone();
        nextCoverage.Add(range);
        _coverageByVersion.Add(nextVersion, nextCoverage);
        _currentVersion = nextVersion;
        return nextVersion;
    }

    private GraphBufferRangeCoverage GetCoverage(GraphBufferVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return _coverageByVersion.TryGetValue(version, out var coverage)
            ? coverage
            : throw new ArgumentException("The buffer version does not belong to this content ledger.", nameof(version));
    }
}
