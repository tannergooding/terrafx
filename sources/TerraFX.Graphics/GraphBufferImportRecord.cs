// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal sealed class GraphBufferImportRecord
{
    private readonly bool _initialContentsValid;
    private readonly bool _initialIsIdle;
    private readonly long _initialLastUseSequence;
    private readonly GraphResourceState _initialState;
    private readonly GraphImportOptions _options;

    internal GraphBufferImportRecord(GpuBuffer buffer, GraphBufferVersion version, GraphImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(options);
        Buffer = buffer;
        Version = version;
        _options = options;
        (_initialState, _initialContentsValid, _initialLastUseSequence, _initialIsIdle) =
            buffer.ResourceOwner.CaptureGraphImportState(version.Range);
    }

    internal GpuBuffer Buffer { get; }

    internal bool HasOverlappingImport { get; set; }

    internal bool InitialContentsValid => _initialContentsValid;

    internal bool InitialIsIdle => _initialIsIdle;

    internal long InitialLastUseSequence => _initialLastUseSequence;

    internal GraphResourceState InitialState => _initialState;

    internal GraphBufferVersion Version { get; }

    internal GraphResourceImportValidation CaptureValidation()
    {
        var (state, contentsValid, lastUseSequence, isIdle) =
            Buffer.ResourceOwner.CaptureGraphImportState(Version.Range);
        var stateMatches = (state == _initialState) && (state == _options.InitialState);
        var contentsMatch = (contentsValid == _initialContentsValid) && (contentsValid == _options.ContentsValid);

        return new GraphResourceImportValidation(
            stateMatches,
            contentsMatch,
            isIdle,
            lastUseSequence,
            _options.CompletionPolicy,
            HasOverlappingImport);
    }
}
