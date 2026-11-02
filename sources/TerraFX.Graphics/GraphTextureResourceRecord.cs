// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX.Graphics;

internal sealed class GraphTextureResourceRecord
{
    private readonly GraphTextureContentLedger _contentLedger;
    private GraphTextureVersion _currentVersion;
    private readonly GraphTextureImportRecord? _importRecord;
    private readonly GraphResourceState _initialState;
    private readonly GraphTextureVersion _initialVersion;
    private readonly HashSet<GraphTextureVersion> _versions = [];

    internal GraphTextureResourceRecord(
        GraphTextureVersion initialVersion,
        bool contentsValid,
        GraphResourceState initialState,
        GraphTextureImportRecord? importRecord)
    {
        ArgumentNullException.ThrowIfNull(initialVersion);

        if (!Enum.IsDefined(initialState))
        {
            throw new ArgumentOutOfRangeException(nameof(initialState));
        }

        _currentVersion = initialVersion;
        _initialState = initialState;
        _initialVersion = initialVersion;
        _contentLedger = new GraphTextureContentLedger(initialVersion, contentsValid);
        _importRecord = importRecord;
        _ = _versions.Add(initialVersion);
    }

    internal GraphTextureVersion CurrentVersion => _currentVersion;

    internal GraphTextureImportRecord? ImportRecord => _importRecord;

    internal GraphResourceState InitialState => _initialState;

    internal GraphTextureVersion InitialVersion => _initialVersion;

    internal bool IsInitialized(GraphTextureVersion version, GraphTextureRange range)
        => _contentLedger.IsInitialized(version, range);

    internal bool Owns(GraphTextureVersion version)
        => _versions.Contains(version);

    internal GraphTextureVersion Write(GraphTextureVersion previousVersion, GraphTextureRange range)
    {
        if (!Owns(previousVersion))
        {
            throw new ArgumentException("The texture version is not owned by this graph resource.", nameof(previousVersion));
        }

        var nextVersion = _contentLedger.Write(previousVersion, range);
        _ = _versions.Add(nextVersion);
        _currentVersion = nextVersion;
        return nextVersion;
    }

    internal (GraphTextureVersion Version, bool LoadContentsValid) WriteAttachment(
        GraphTextureVersion previousVersion,
        GraphTextureRange range,
        AttachmentLoadAction loadAction,
        AttachmentStoreAction storeAction)
    {
        if (!Owns(previousVersion))
        {
            throw new ArgumentException("The texture version is not owned by this graph resource.", nameof(previousVersion));
        }

        var (version, loadContentsValid) =
            _contentLedger.WriteAttachment(previousVersion, range, loadAction, storeAction);
        _ = _versions.Add(version);
        _currentVersion = version;
        return (version, loadContentsValid);
    }
}
