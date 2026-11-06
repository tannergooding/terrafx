// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX.Buffers;
using TerraFX.Graphics;
using TerraFX.Interop.Vulkan;

namespace TerraFX.Graphics.Vulkan.Tests;

[TestFixture]
internal sealed class SpirvResourceAccessTests
{
    private static void AppendInstruction(List<uint> words, ushort opcode, params uint[] operands)
    {
        words.Add((checked((uint)operands.Length + 1) << 16) | opcode);
        words.AddRange(operands);
    }

    private static void AssertMatches(
        ShaderResourceAccess access,
        uint? accessDecoration,
        uint? memberAccessDecoration = null)
    {
        var bytecode = CreateStorageBufferSpirv(accessDecoration, memberAccessDecoration);
        using var storage = CreateBytecodeStorage(bytecode);
        using var lease = storage.AcquireReadLease(0, bytecode.Length);
        var shader = new ShaderBytecodeInput(
            ShaderStage.Vertex,
            ShaderBinaryFormat.SpirV,
            lease,
            CreateReflection(access));

        Assert.DoesNotThrow(() => SpirvReflectionValidator.Validate(shader, lease, index: 0));
    }

    private static void AssertMismatch(ShaderResourceAccess access, uint? accessDecoration)
    {
        var bytecode = CreateStorageBufferSpirv(accessDecoration);
        using var storage = CreateBytecodeStorage(bytecode);
        using var lease = storage.AcquireReadLease(0, bytecode.Length);
        var shader = new ShaderBytecodeInput(
            ShaderStage.Vertex,
            ShaderBinaryFormat.SpirV,
            lease,
            CreateReflection(access));

        _ = Assert.Throws<ArgumentException>(() => SpirvReflectionValidator.Validate(shader, lease, index: 0));
    }

    private static OwnedBuffer<byte> CreateBytecodeStorage(byte[] bytecode)
    {
        var storage = UnmanagedBuffer<byte>.Allocate(
            bytecode.Length,
            new BufferAllocationOptions(bytecode.Length, alignment: 1));
        storage.CopyFrom(bytecode.AsSpan(), 0);
        return storage.TransferToOwnedBuffer();
    }

    private static ShaderReflection CreateReflection(ShaderResourceAccess access)
    {
        var digest = new byte[32];
        var compilerIdentity = new ShaderCompilerIdentity(
            "TerraFX.Graphics.Vulkan.Tests",
            "test",
            "1.0.0",
            "linux-x64",
            ImmutableCollectionsMarshal.AsImmutableArray(digest));

        return new ShaderReflection(
            ShaderStage.Vertex,
            "main",
            compilerIdentity,
            [],
            [],
            [new ShaderResourceSignature(
                ShaderBindingGroup.Frame,
                0,
                ShaderResourceKind.StorageBuffer,
                1,
                access)],
            [],
            [],
            bindingLayoutVersion: 1,
            groupSizeX: 0,
            groupSizeY: 0,
            groupSizeZ: 0);
    }

    private static byte[] CreateStorageBufferSpirv(uint? accessDecoration, uint? memberAccessDecoration = null)
    {
        var words = new List<uint> {
            0x07230203,
            0x00010000,
            0,
            11,
            0,
        };
        AppendInstruction(words, opcode: 17, 1);
        AppendInstruction(words, opcode: 15, 0, 10, 0x6E69616D, 0);
        AppendInstruction(words, opcode: 21, 2, 32, 0);
        AppendInstruction(words, opcode: 30, 3, 2);
        AppendInstruction(words, opcode: 32, 4, 12, 3);
        AppendInstruction(words, opcode: 59, 4, 5, 12);
        AppendInstruction(words, opcode: 71, 3, 2);
        AppendInstruction(words, opcode: 72, 3, 0, 35, 0);

        if (memberAccessDecoration is uint memberDecoration)
        {
            AppendInstruction(words, opcode: 72, 3, 0, memberDecoration);
        }

        AppendInstruction(words, opcode: 71, 5, 34, 0);
        AppendInstruction(words, opcode: 71, 5, 33, 32);

        if (accessDecoration is uint decoration)
        {
            AppendInstruction(words, opcode: 71, 5, decoration);
        }

        return MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(words)).ToArray();
    }

    [Test]
    public void StorageBufferAccessDecorationsMatchTypedReflection()
    {
        AssertMatches(ShaderResourceAccess.ReadWrite, accessDecoration: null);
        AssertMatches(ShaderResourceAccess.Read, accessDecoration: 24);
        AssertMatches(ShaderResourceAccess.Read, accessDecoration: null, memberAccessDecoration: 24);
        AssertMatches(ShaderResourceAccess.Write, accessDecoration: 25);

        AssertMismatch(ShaderResourceAccess.Read, accessDecoration: null);
        AssertMismatch(ShaderResourceAccess.Write, accessDecoration: null);
    }
}
