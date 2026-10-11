// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace TerraFX;

internal static class FoundationNameUuid
{
    internal const string Recipe = "TerraFXNameUuidSha256V1";

    internal static Guid Create(Guid namespaceId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length is 0 or > 1024)
        {
            ThrowHelper.ThrowArgumentException("A name UUID requires 1 through 1024 exact ASCII characters.", nameof(name));
        }

        foreach (var character in name)
        {
            if (character > 127)
            {
                ThrowHelper.ThrowArgumentException("A name UUID requires exact ASCII.", nameof(name));
            }
        }

        var domain = "TerraFX.NameUuid.SHA256.v1\0"u8;
        var input = RentedBuffer.Create<byte>(domain.Length + 16 + 4 + name.Length, out var rentedInput);

        try
        {
            domain.CopyTo(input);
            _ = namespaceId.TryWriteBytes(input.Slice(domain.Length, 16), bigEndian: true, out _);
            BinaryPrimitives.WriteUInt32LittleEndian(input[(domain.Length + 16)..], checked((uint)name.Length));
            _ = Encoding.ASCII.GetBytes(name, input[(domain.Length + 20)..]);

            Unsafe.SkipInit(out InlineBuffer32<byte> digestStorage); // HashData fills all 32 bytes before they are read.
            Span<byte> digest = digestStorage;

            _ = SHA256.HashData(input, digest);
            digest[6] = (byte)((digest[6] & 15) | 128);
            digest[8] = (byte)((digest[8] & 63) | 128);
            return new Guid(digest[..16], bigEndian: true);
        }
        finally
        {
            rentedInput.Dispose();
        }
    }

    internal static void ValidateAssignments(scoped ReadOnlySpan<FoundationNameAssignment> assignments)
    {
        var ids = new Dictionary<Guid, FoundationNameAssignment>();

        foreach (var assignment in assignments)
        {
            if (assignment.Recipe != Recipe || Create(assignment.NamespaceId, assignment.Name) != assignment.Id)
            {
                ThrowHelper.ThrowArgumentException("The name UUID assignment does not match its locked recipe/input.", nameof(assignments));
            }

            if (ids.TryGetValue(assignment.Id, out var existing) && existing != assignment)
            {
                ThrowHelper.ThrowArgumentException("Different name UUID assignments emitted the same identity.", nameof(assignments));
            }

            ids[assignment.Id] = assignment;
        }
    }
}
