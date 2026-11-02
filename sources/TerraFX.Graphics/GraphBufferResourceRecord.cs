// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX.Graphics;

internal sealed class GraphBufferResourceRecord
{
    private readonly GraphBufferContentLedger _contentLedger;
    private GraphBufferVersion _currentVersion;
    private readonly GraphBufferImportRecord? _importRecord;
    private readonly HashSet<GraphBufferVersion> _versions = [];

    internal GraphBufferResourceRecord(
        GraphBufferVersion initialVersion,
        bool contentsValid,
        GraphBufferImportRecord? importRecord)
    {
        ArgumentNullException.ThrowIfNull(initialVersion);
        _currentVersion = initialVersion;
        _contentLedger = new GraphBufferContentLedger(initialVersion, contentsValid);
        _importRecord = importRecord;
        _ = _versions.Add(initialVersion);
    }

    internal GraphBufferVersion CurrentVersion => _currentVersion;

    internal GraphBufferImportRecord? ImportRecord => _importRecord;

    internal bool IsInitialized(GraphBufferVersion version, GraphBufferRange range)
        => _contentLedger.IsInitialized(version, range);

    internal bool Owns(GraphBufferVersion version)
        => _versions.Contains(version);

    internal GraphBufferVersion Write(GraphBufferVersion previousVersion, GraphBufferRange range)
    {
        if (!Owns(previousVersion))
        {
            throw new ArgumentException("The buffer version is not owned by this graph resource.", nameof(previousVersion));
        }

        var nextVersion = _contentLedger.Write(previousVersion, range);
        _ = _versions.Add(nextVersion);
        _currentVersion = nextVersion;
        return nextVersion;
    }
}
