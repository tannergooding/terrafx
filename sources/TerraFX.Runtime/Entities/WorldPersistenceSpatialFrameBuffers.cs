// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal ref struct WorldPersistenceSpatialFrameBuffers
{
    internal Span<byte> Destination;

    internal Span<byte> FrameCandidateStorage;

    internal Span<byte> FramePayloadStorage;

    internal Span<byte> ProfileCandidateStorage;

    internal Span<byte> ProfileStorage;

    internal readonly long GetWorkspaceBytes()
        => checked(
            (long)Destination.Length
            + FrameCandidateStorage.Length
            + FramePayloadStorage.Length
            + ProfileCandidateStorage.Length
            + ProfileStorage.Length);
}
