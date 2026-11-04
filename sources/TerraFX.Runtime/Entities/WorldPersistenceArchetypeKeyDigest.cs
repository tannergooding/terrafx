// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace TerraFX.Entities;

internal static class WorldPersistenceArchetypeKeyDigest
{
    private const int GuidBytes = 16;
    private const int MaximumSchemaCount = 256;
    private const int PrefixLength = 20;

    internal static void Compute(scoped ReadOnlySpan<Guid> schemaIds, scoped Span<byte> destination)
    {
        var digestInputLength = checked(PrefixLength + 1 + sizeof(uint) + (schemaIds.Length * GuidBytes));
        Span<byte> digestInput = stackalloc byte[PrefixLength + 1 + sizeof(uint) + (MaximumSchemaCount * GuidBytes)];
        var prefix = "TerraFX.Archetype.v1"u8;

        if ((schemaIds.Length > MaximumSchemaCount) || (destination.Length != SHA256.HashSizeInBytes))
        {
            throw new ArgumentException("The archetype digest extent does not match the canonical schema set.", nameof(schemaIds));
        }

        prefix.CopyTo(digestInput);
        digestInput[PrefixLength] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(digestInput[(PrefixLength + 1)..], checked((uint)schemaIds.Length));
        var offset = PrefixLength + 1 + sizeof(uint);

        for (var index = 0; index < schemaIds.Length; index++)
        {
            _ = schemaIds[index].TryWriteBytes(digestInput[offset..], bigEndian: true, out var guidBytes);
            ArgumentOutOfRangeException.ThrowIfNotEqual(guidBytes, GuidBytes);
            offset = checked(offset + GuidBytes);
        }

        _ = SHA256.HashData(digestInput[..digestInputLength], destination);
    }
}
