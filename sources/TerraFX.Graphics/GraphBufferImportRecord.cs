// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal sealed class GraphBufferImportRecord
{
    private readonly bool _initialContentsValid;
    private readonly bool _initialIsIdle;
    private readonly long _initialLastUseSequence;
    private readonly GraphResourceState _initialState;
    private readonly bool _isTransient;
    private readonly GraphImportOptions _options;

    internal GraphBufferImportRecord(
        GpuBuffer buffer,
        GraphBufferVersion version,
        GraphImportOptions options,
        bool isTransient)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(options);

        if (!isTransient)
        {
            throw new ArgumentException("A synthetic graph buffer record must represent a transient resource.", nameof(isTransient));
        }

        if ((buffer.DeviceEpoch != version.DeviceEpoch)
            || (buffer.ByteLength != version.Description.ByteLength)
            || (buffer.Description.Usage != version.Description.Usage)
            || (version.Range != new GraphBufferRange(0, version.Description.ByteLength)))
        {
            throw new ArgumentException("The transient GPU buffer does not match its graph resource description.", nameof(buffer));
        }

        Buffer = buffer;
        Version = version;
        _options = options;
        _isTransient = true;
        _initialState = options.InitialState;
        _initialContentsValid = options.ContentsValid;
        _initialLastUseSequence = 0;
        _initialIsIdle = true;
    }

    internal GraphBufferImportRecord(GpuBuffer buffer, GraphBufferVersion version, GraphImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(options);
        Buffer = buffer;
        Version = version;
        _options = options;
        _isTransient = false;
        (_initialState, _initialContentsValid, _initialLastUseSequence, _initialIsIdle) =
            buffer.ResourceOwner.CaptureGraphImportState(version.Range);
    }

    internal GraphBufferImportRecord(GpuBuffer buffer, GraphBufferVersion version, bool isTransient)
        : this(
            buffer,
            version,
            new GraphImportOptions(
                GraphResourceState.Undefined,
                contentsValid: false,
                completionPolicy: GraphImportCompletionPolicy.RequireIdle),
            isTransient)
    {
    }

    internal GpuBuffer Buffer { get; }

    internal bool HasOverlappingImport { get; set; }

    internal bool InitialContentsValid => _initialContentsValid;

    internal bool InitialIsIdle => _initialIsIdle;

    internal long InitialLastUseSequence => _initialLastUseSequence;

    internal GraphResourceState InitialState => _initialState;

    internal bool IsTransient => _isTransient;

    internal GraphBufferVersion Version { get; }

    internal GraphResourceImportValidation CaptureValidation()
    {
        if (_isTransient)
        {
            return new GraphResourceImportValidation(
                stateMatches: true,
                contentsMatch: true,
                isIdle: true,
                lastUseSequence: 0,
                completionPolicy: _options.CompletionPolicy,
                hasOverlappingImport: HasOverlappingImport);
        }

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
