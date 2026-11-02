// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal sealed class GraphTextureImportRecord
{
    private readonly bool _initialContentsValid;
    private readonly bool _initialIsIdle;
    private readonly long _initialLastUseSequence;
    private readonly GraphResourceState _initialState;
    private readonly GraphImportOptions _options;

    internal GraphTextureImportRecord(GpuTexture texture, GraphTextureVersion version, GraphImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(options);
        Texture = texture;
        Version = version;
        _options = options;
        (_initialState, _initialContentsValid, _initialLastUseSequence, _initialIsIdle) =
            texture.ResourceOwner.CaptureGraphImportState(version.Range);
    }

    internal bool HasOverlappingImport { get; set; }

    internal bool InitialContentsValid => _initialContentsValid;

    internal bool InitialIsIdle => _initialIsIdle;

    internal long InitialLastUseSequence => _initialLastUseSequence;

    internal GraphResourceState InitialState => _initialState;

    internal GpuTexture Texture { get; }

    internal GraphTextureVersion Version { get; }

    internal GraphResourceImportValidation CaptureValidation()
    {
        var (state, contentsValid, lastUseSequence, isIdle) =
            Texture.ResourceOwner.CaptureGraphImportState(Version.Range);
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
