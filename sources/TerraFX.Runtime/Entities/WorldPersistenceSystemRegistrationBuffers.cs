// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using TerraFX;

namespace TerraFX.Entities;

internal ref struct WorldPersistenceSystemRegistrationBuffers
{
    internal Span<SystemResourceAccess> AccessScratch;

    internal Span<EbinPayloadSlice> AccessSlices;

    internal Span<byte> AccessStorage;

    internal Span<byte> AccessCandidateStorage;

    internal Span<byte> AccessArrayPayloadStorage;

    internal Span<byte> AfterArrayPayloadStorage;

    internal Span<byte> BeforeArrayPayloadStorage;

    internal Span<byte> CandidateStorage;

    internal Span<byte> Destination;

    internal Span<Guid> DependencyScratch;

    internal Span<byte> RootPayloadStorage;

    internal Span<byte> SchemaCandidateStorage;

    internal Span<byte> SchemaRecordStorage;

    private static long AddBytes(long bytes, int count, int elementSize)
        => checked(bytes + (checked((long)count) * elementSize));

    internal readonly long GetWorkspaceBytes()
    {
        long bytes = 0;
        bytes = AddBytes(bytes, AccessScratch.Length, Unsafe.SizeOf<SystemResourceAccess>());
        bytes = AddBytes(bytes, AccessSlices.Length, Unsafe.SizeOf<EbinPayloadSlice>());
        bytes = checked(bytes + AccessStorage.Length);
        bytes = checked(bytes + AccessCandidateStorage.Length);
        bytes = checked(bytes + AccessArrayPayloadStorage.Length);
        bytes = checked(bytes + AfterArrayPayloadStorage.Length);
        bytes = checked(bytes + BeforeArrayPayloadStorage.Length);
        bytes = AddBytes(bytes, DependencyScratch.Length, Unsafe.SizeOf<Guid>());
        bytes = checked(bytes + RootPayloadStorage.Length);
        bytes = checked(bytes + SchemaCandidateStorage.Length);
        bytes = checked(bytes + SchemaRecordStorage.Length);
        return bytes;
    }
}
