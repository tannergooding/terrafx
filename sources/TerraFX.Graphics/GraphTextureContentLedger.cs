// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX.Graphics;

internal sealed class GraphTextureContentLedger
{
    private readonly Dictionary<GraphTextureVersion, GraphTextureRangeCoverage> _coverageByVersion = [];
    private GraphTextureVersion _currentVersion;

    internal GraphTextureContentLedger(GraphTextureVersion initialVersion, bool contentsValid)
    {
        ArgumentNullException.ThrowIfNull(initialVersion);
        _currentVersion = initialVersion;
        var coverage = new GraphTextureRangeCoverage();

        if (contentsValid)
        {
            coverage.Add(initialVersion.Range);
        }

        _coverageByVersion.Add(initialVersion, coverage);
    }

    private static GraphTextureRange ValidateRange(GraphTextureVersion version, GraphTextureRange range)
    {
        if (!range.IsWithin(version.Range))
        {
            throw new ArgumentException("The texture range must fit its graph version.", nameof(range));
        }

        return range;
    }

    internal bool IsInitialized(GraphTextureVersion version, GraphTextureRange range)
        => GetCoverage(version).Covers(ValidateRange(version, range));

    internal GraphTextureVersion Write(GraphTextureVersion previousVersion, GraphTextureRange range)
    {
        var previousCoverage = GetCoverage(previousVersion);
        range = ValidateRange(previousVersion, range);

        if (!ReferenceEquals(previousVersion, _currentVersion))
        {
            throw new InvalidOperationException("A texture write must advance the current graph version.");
        }

        var nextVersion = new GraphTextureVersion(
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

    internal GraphTextureVersion WriteAttachment(
        GraphTextureVersion previousVersion,
        GraphTextureRange range,
        AttachmentLoadAction loadAction,
        AttachmentStoreAction storeAction)
    {
        if (!Enum.IsDefined(loadAction))
        {
            throw new ArgumentOutOfRangeException(nameof(loadAction));
        }

        if (!Enum.IsDefined(storeAction))
        {
            throw new ArgumentOutOfRangeException(nameof(storeAction));
        }

        var previousCoverage = GetCoverage(previousVersion);
        range = ValidateRange(previousVersion, range);

        if (!ReferenceEquals(previousVersion, _currentVersion))
        {
            throw new InvalidOperationException("An attachment write must advance the current graph version.");
        }

        if ((loadAction == AttachmentLoadAction.Load) && !previousCoverage.Covers(range))
        {
            throw new InvalidOperationException("Loading an attachment requires initialized contents over its complete range.");
        }

        var nextVersion = new GraphTextureVersion(
            previousVersion.DeviceEpoch,
            previousVersion.ResourceId,
            checked(previousVersion.Version + 1),
            previousVersion.Description,
            previousVersion.Range);
        var nextCoverage = previousCoverage.Clone();

        if (storeAction == AttachmentStoreAction.Store)
        {
            nextCoverage.Add(range);
        }
        else
        {
            nextCoverage.Remove(range);
        }

        _coverageByVersion.Add(nextVersion, nextCoverage);
        _currentVersion = nextVersion;
        return nextVersion;
    }

    private GraphTextureRangeCoverage GetCoverage(GraphTextureVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return _coverageByVersion.TryGetValue(version, out var coverage)
            ? coverage
            : throw new ArgumentException("The texture version does not belong to this content ledger.", nameof(version));
    }
}
