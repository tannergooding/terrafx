// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

internal readonly struct GraphResourceStateTransition
{
    private readonly GraphBufferRange _bufferRange;
    private readonly GraphTextureRange _textureRange;

    internal GraphResourceStateTransition(
        RenderPassId passId,
        GraphBufferVersion version,
        GraphBufferRange range,
        GraphResourceState beforeState,
        GraphResourceState afterState)
    {
        Validate(passId, beforeState, afterState);
        ArgumentNullException.ThrowIfNull(version);

        if (!range.IsWithin(version.Range))
        {
            throw new ArgumentException("The state transition range must fit its graph version.", nameof(range));
        }

        DeviceEpoch = version.DeviceEpoch;
        IsTexture = false;
        PassId = passId;
        ResourceId = version.ResourceId;
        AfterState = afterState;
        BeforeState = beforeState;
        _bufferRange = range;
        _textureRange = default;
    }

    internal GraphResourceStateTransition(
        RenderPassId passId,
        GraphTextureVersion version,
        GraphTextureRange range,
        GraphResourceState beforeState,
        GraphResourceState afterState)
    {
        Validate(passId, beforeState, afterState);
        ArgumentNullException.ThrowIfNull(version);

        if (!range.IsWithin(version.Range))
        {
            throw new ArgumentException("The state transition range must fit its graph version.", nameof(range));
        }

        DeviceEpoch = version.DeviceEpoch;
        IsTexture = true;
        PassId = passId;
        ResourceId = version.ResourceId;
        AfterState = afterState;
        BeforeState = beforeState;
        _bufferRange = default;
        _textureRange = range;
    }

    internal GraphResourceState AfterState { get; }

    internal GraphResourceState BeforeState { get; }

    internal GraphBufferRange BufferRange => _bufferRange;

    internal uint DeviceEpoch { get; }

    internal bool IsTexture { get; }

    internal RenderPassId PassId { get; }

    internal ulong ResourceId { get; }

    internal GraphTextureRange TextureRange => _textureRange;

    private static void Validate(RenderPassId passId, GraphResourceState beforeState, GraphResourceState afterState)
    {
        if (!passId.IsValid)
        {
            throw new ArgumentException("The graph pass ID must be valid.", nameof(passId));
        }

        if (!Enum.IsDefined(beforeState))
        {
            throw new ArgumentOutOfRangeException(nameof(beforeState));
        }

        if (!Enum.IsDefined(afterState))
        {
            throw new ArgumentOutOfRangeException(nameof(afterState));
        }

        if (beforeState == afterState)
        {
            throw new ArgumentException("A resource state transition must change its state.", nameof(afterState));
        }
    }
}
