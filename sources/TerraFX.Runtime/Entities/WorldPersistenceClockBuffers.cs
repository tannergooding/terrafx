// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using TerraFX;

namespace TerraFX.Entities;

internal ref struct WorldPersistenceClockBuffers
{
    internal Span<byte> ClockCandidateStorage;

    internal Span<byte> ClockPayloadStorage;

    internal Span<byte> Destination;

    internal Span<byte> FrameCandidateStorage;

    internal Span<byte> FramePayloadStorage;

    internal Span<byte> FrameProfileCandidateStorage;

    internal Span<byte> FrameProfileStorage;

    internal Span<byte> InitialFrame2Storage;

    internal Span<byte> InitialFrame3Storage;

    internal Span<byte> SegmentArrayPayloadStorage;

    internal Span<byte> SegmentCandidateStorage;

    internal Span<EbinPayloadSlice> SegmentSlices;

    internal Span<byte> SegmentStorage;

    internal readonly long GetWorkspaceBytes()
    {
        long bytes = 0;
        bytes = checked(bytes + ClockCandidateStorage.Length);
        bytes = checked(bytes + ClockPayloadStorage.Length);
        bytes = checked(bytes + Destination.Length);
        bytes = checked(bytes + FrameCandidateStorage.Length);
        bytes = checked(bytes + FramePayloadStorage.Length);
        bytes = checked(bytes + FrameProfileCandidateStorage.Length);
        bytes = checked(bytes + FrameProfileStorage.Length);
        bytes = checked(bytes + InitialFrame2Storage.Length);
        bytes = checked(bytes + InitialFrame3Storage.Length);
        bytes = checked(bytes + SegmentArrayPayloadStorage.Length);
        bytes = checked(bytes + SegmentCandidateStorage.Length);
        bytes = checked(bytes + checked((long)SegmentSlices.Length * Unsafe.SizeOf<EbinPayloadSlice>()));
        bytes = checked(bytes + SegmentStorage.Length);
        return bytes;
    }
}
