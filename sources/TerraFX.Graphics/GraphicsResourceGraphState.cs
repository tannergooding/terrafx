// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Graphics;

internal sealed class GraphicsResourceGraphState
{
    private readonly GraphBufferRangeCoverage? _bufferContents;
    private readonly BufferDescription? _bufferDescription;
    private GraphResourceState _bufferState;
    private readonly Lock _gate = new();
    private readonly GraphTextureRangeCoverage? _textureContents;
    private readonly TextureDescription? _textureDescription;
    private readonly GraphTextureStateMap? _textureStates;

    internal GraphicsResourceGraphState(
        BufferDescription? bufferDescription,
        TextureDescription? textureDescription)
    {
        if ((bufferDescription is null) == (textureDescription is null))
        {
            throw new ArgumentException("Exactly one Graphics resource description is required.");
        }

        _bufferDescription = bufferDescription;
        _textureDescription = textureDescription;

        if (bufferDescription is not null)
        {
            _bufferContents = new GraphBufferRangeCoverage();
            _bufferState = bufferDescription.MemoryKind switch {
                GpuMemoryKind.DeviceLocal => GraphResourceState.Undefined,
                GpuMemoryKind.Upload => GraphResourceState.General,
                GpuMemoryKind.Readback => GraphResourceState.CopyDestination,
                _ => throw new ArgumentOutOfRangeException(nameof(bufferDescription)),
            };
        }
        else
        {
            _textureContents = new GraphTextureRangeCoverage();
            _textureStates = new GraphTextureStateMap();
        }
    }

    private static GraphTextureRange CreateTextureRange(TextureDescription description, TextureSubresource subresource)
    {
        _ = GraphicsDevice.GetTextureSubresourceExtent(description, subresource);
        return new GraphTextureRange(
            subresource.MipLevel,
            1,
            subresource.ArrayLayer,
            1,
            subresource.Aspect);
    }

    private static GraphBufferRange ValidateBufferRange(BufferDescription description, GraphBufferRange range)
    {
        if (!range.IsWithin(new GraphBufferRange(0, description.ByteLength)))
        {
            throw new ArgumentException("The graph buffer range must fit the resource description.", nameof(range));
        }

        return range;
    }

    private static GraphTextureRange ValidateTextureRange(TextureDescription description, GraphTextureRange range)
    {
        var aspect = (description.Format == TextureFormat.D32Float)
            ? TextureAspect.Depth
            : TextureAspect.Color;
        var fullRange = new GraphTextureRange(0, description.MipLevels, 0, description.ArrayLayers, aspect);

        if (!range.IsWithin(fullRange))
        {
            throw new ArgumentException("The graph texture range must fit the resource description and format aspect.", nameof(range));
        }

        return range;
    }

    internal (GraphResourceState State, bool ContentsValid) GetBufferState(GraphBufferRange range)
    {
        lock (_gate)
        {
            var description = _bufferDescription
                ?? throw new InvalidOperationException("The Graphics resource is not a buffer.");
            var contents = _bufferContents
                ?? throw new InvalidOperationException("The Graphics buffer has no content tracker.");
            return (_bufferState, contents.Covers(ValidateBufferRange(description, range)));
        }
    }

    internal (GraphResourceState State, bool ContentsValid) GetTextureState(GraphTextureRange range)
    {
        lock (_gate)
        {
            var description = _textureDescription
                ?? throw new InvalidOperationException("The Graphics resource is not a texture.");
            var contents = _textureContents
                ?? throw new InvalidOperationException("The Graphics texture has no content tracker.");
            var states = _textureStates
                ?? throw new InvalidOperationException("The Graphics texture has no state tracker.");
            range = ValidateTextureRange(description, range);
            return (states.GetUniformState(range), contents.Covers(range));
        }
    }

    internal void RecordBufferReadback()
    {
        lock (_gate)
        {
            if (_bufferDescription is null)
            {
                throw new InvalidOperationException("The Graphics resource is not a buffer.");
            }

            _bufferState = GraphResourceState.General;
        }
    }

    internal void RecordBufferUpload(long destinationOffset, nint byteLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(destinationOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        var range = new GraphBufferRange(destinationOffset, checked((long)byteLength));

        lock (_gate)
        {
            var description = _bufferDescription
                ?? throw new InvalidOperationException("The Graphics resource is not a buffer.");
            var contents = _bufferContents
                ?? throw new InvalidOperationException("The Graphics buffer has no content tracker.");
            contents.Add(ValidateBufferRange(description, range));
            _bufferState = GraphResourceState.General;
        }
    }

    internal void RecordTextureReadback(TextureSubresource subresource)
    {
        lock (_gate)
        {
            var description = _textureDescription
                ?? throw new InvalidOperationException("The Graphics resource is not a texture.");
            var states = _textureStates
                ?? throw new InvalidOperationException("The Graphics texture has no state tracker.");
            states.Set(CreateTextureRange(description, subresource), GraphResourceState.General);
        }
    }

    internal void RecordTextureUpload(TextureSubresource subresource)
    {
        lock (_gate)
        {
            var description = _textureDescription
                ?? throw new InvalidOperationException("The Graphics resource is not a texture.");
            var contents = _textureContents
                ?? throw new InvalidOperationException("The Graphics texture has no content tracker.");
            var states = _textureStates
                ?? throw new InvalidOperationException("The Graphics texture has no state tracker.");
            var range = CreateTextureRange(description, subresource);
            contents.Add(range);
            states.Set(range, GraphResourceState.General);
        }
    }
}
