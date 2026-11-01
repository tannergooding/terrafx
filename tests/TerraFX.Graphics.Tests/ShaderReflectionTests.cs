// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX;
using TerraFX.Buffers;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class ShaderReflectionTests
{
    private static ShaderCompilerIdentity CreateCompilerIdentity()
    {
        var digest = new byte[32];
        Array.Fill(digest, (byte)0xA5);

        return new ShaderCompilerIdentity(
            "Microsoft.Direct3D.DXC",
            "1.8.2505.1",
            "1.8.2505.32",
            ImmutableCollectionsMarshal.AsImmutableArray(digest));
    }

    private static ShaderReflection CreateReflection(
        ShaderStage stage = ShaderStage.Vertex,
        ImmutableArray<ShaderIoSignature>? inputs = null,
        ImmutableArray<ShaderIoSignature>? outputs = null,
        ImmutableArray<ShaderResourceSignature>? resources = null,
        ImmutableArray<ShaderRecordLayout>? recordLayouts = null,
        ImmutableArray<ShaderCapability>? requiredCapabilities = null,
        int bindingLayoutVersion = 1,
        int groupSizeX = 0,
        int groupSizeY = 0,
        int groupSizeZ = 0)
        => new(
            stage,
            "main",
            CreateCompilerIdentity(),
            inputs ?? [],
            outputs ?? [],
            resources ?? [],
            recordLayouts ?? [],
            requiredCapabilities ?? [],
            bindingLayoutVersion,
            groupSizeX,
            groupSizeY,
            groupSizeZ);

    [Test]
    public void ShaderBytecodeInputBorrowsNonemptyMatchingLease()
    {
        using var owner = UnmanagedBuffer<byte>.Allocate(4, new BufferAllocationOptions(64, 1));
        using var lease = owner.AcquireRead(0, 4);
        using var emptyLease = owner.AcquireRead(0, 0);
        var reflection = CreateReflection();
        var input = new ShaderBytecodeInput(ShaderStage.Vertex, ShaderBinaryFormat.Dxil, lease, reflection);

        Assert.That(input.Bytecode, Is.SameAs(lease));
        Assert.That(input.Format, Is.EqualTo(ShaderBinaryFormat.Dxil));
        Assert.That(input.Stage, Is.EqualTo(ShaderStage.Vertex));
        _ = Assert.Throws<ArgumentException>(() => new ShaderBytecodeInput(
            ShaderStage.Pixel,
            ShaderBinaryFormat.Dxil,
            lease,
            reflection));
        _ = Assert.Throws<ArgumentException>(() => new ShaderBytecodeInput(
            ShaderStage.Vertex,
            ShaderBinaryFormat.Dxil,
            emptyLease,
            reflection));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ShaderBytecodeInput(
            ShaderStage.None,
            ShaderBinaryFormat.Dxil,
            lease,
            reflection));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ShaderBytecodeInput(
            ShaderStage.Vertex,
            ShaderBinaryFormat.None,
            lease,
            reflection));

        lease.Dispose();
        _ = Assert.Throws<ObjectDisposedException>(() => new ShaderBytecodeInput(
            ShaderStage.Vertex,
            ShaderBinaryFormat.Dxil,
            lease,
            reflection));
    }

    [Test]
    public void ShaderCompilerIdentityCopiesAndValidatesDigest()
    {
        var digest = new byte[32];
        Array.Fill(digest, (byte)0xA5);
        var compiler = new ShaderCompilerIdentity(
            "Microsoft.Direct3D.DXC",
            "1.8.2505.1",
            "1.8.2505.32",
            ImmutableCollectionsMarshal.AsImmutableArray(digest));

        digest[0] = 0;

        Assert.That(compiler.IsValid, Is.True);
        Assert.That(compiler.PackageSha256[0], Is.EqualTo(0xA5));
        Assert.That(compiler, Is.EqualTo(CreateCompilerIdentity()));
        Assert.That(default(ShaderCompilerIdentity).IsValid, Is.False);
        _ = Assert.Throws<ArgumentException>(() => new ShaderCompilerIdentity(
            "Microsoft.Direct3D.DXC",
            "1.8.2505.1",
            "1.8.2505.32",
            default));
        _ = Assert.Throws<ArgumentException>(() => new ShaderCompilerIdentity(
            "Microsoft.Direct3D.DXC",
            "1.8.2505.1",
            "1.8.2505.32",
            [(byte)1]));
    }

    [Test]
    public void ShaderIoSignatureValidatesSystemValueLocationsAndEnums()
    {
        var position = new ShaderIoSignature("SV_Position", 0, null, ShaderValueType.Float4, ShaderInterpolation.Smooth);
        var texCoord = new ShaderIoSignature("TEXCOORD", 1, 0, ShaderValueType.Float2, ShaderInterpolation.NoPerspective);

        Assert.That(position.Location, Is.Null);
        Assert.That(texCoord.Location, Is.EqualTo(0));
        Assert.That(position, Is.EqualTo(new ShaderIoSignature("SV_Position", 0, null, ShaderValueType.Float4, ShaderInterpolation.Smooth)));
        _ = Assert.Throws<ArgumentException>(() => new ShaderIoSignature(
            "TEXCOORD",
            0,
            null,
            ShaderValueType.Float2,
            ShaderInterpolation.Smooth));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ShaderIoSignature(
            "TEXCOORD",
            0,
            -1,
            ShaderValueType.Float2,
            ShaderInterpolation.Smooth));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ShaderIoSignature(
            "TEXCOORD",
            0,
            0,
            ShaderValueType.None,
            ShaderInterpolation.Smooth));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ShaderIoSignature(
            "TEXCOORD",
            0,
            0,
            ShaderValueType.Float2,
            ShaderInterpolation.None));
    }

    [Test]
    public void ShaderRecordLayoutCopiesOrdersAndRejectsRanges()
    {
        var fields = new[] {
            new ShaderFieldLayout("Second", ShaderValueType.UInt4, 16, 16, 1, 0),
            new ShaderFieldLayout("First", ShaderValueType.Float4, 0, 16, 1, 0),
        };
        var layout = new ShaderRecordLayout("Constants", 32, ImmutableCollectionsMarshal.AsImmutableArray(fields));
        fields[0] = new ShaderFieldLayout("Changed", ShaderValueType.Float4, 0, 16, 1, 0);

        Assert.That(layout.Fields[0].Name, Is.EqualTo("First"));
        Assert.That(layout.Fields[1].Name, Is.EqualTo("Second"));
        Assert.That(layout.Fields[1].ByteOffset, Is.EqualTo(16));
        _ = Assert.Throws<ArgumentException>(() => new ShaderRecordLayout(
            "Overlap",
            32,
            [
                new ShaderFieldLayout("First", ShaderValueType.Float4, 0, 16, 1, 0),
                new ShaderFieldLayout("Second", ShaderValueType.Float4, 8, 16, 1, 0),
            ]));
        _ = Assert.Throws<ArgumentException>(() => new ShaderRecordLayout(
            "OutOfRange",
            16,
            [new ShaderFieldLayout("Data", ShaderValueType.Float4, 8, 16, 1, 0)]));
        _ = Assert.Throws<ArgumentException>(() => new ShaderRecordLayout(
            "BadStride",
            32,
            [new ShaderFieldLayout("Data", ShaderValueType.Float4, 0, 16, 2, 8)]));
    }

    [Test]
    public void ShaderReflectionCanonicalizesArraysAndRejectsDuplicateBindings()
    {
        var inputs = new[] {
            new ShaderIoSignature("TEXCOORD", 0, 1, ShaderValueType.Float2, ShaderInterpolation.Smooth),
            new ShaderIoSignature("POSITION", 0, 0, ShaderValueType.Float3, ShaderInterpolation.NoPerspective),
        };
        var resources = new[] {
            new ShaderResourceSignature(ShaderBindingGroup.Material, 2, ShaderResourceKind.SampledImage, 1),
            new ShaderResourceSignature(ShaderBindingGroup.Frame, 1, ShaderResourceKind.UniformBuffer, 1),
            new ShaderResourceSignature(ShaderBindingGroup.Pass, 3, ShaderResourceKind.StorageBuffer, 1),
        };
        var recordLayouts = new[] {
            new ShaderRecordLayout("ZRecord", 16, [new ShaderFieldLayout("Value", ShaderValueType.Float4, 0, 16, 1, 0)]),
            new ShaderRecordLayout("ARecord", 16, [new ShaderFieldLayout("Value", ShaderValueType.UInt4, 0, 16, 1, 0)]),
        };
        var reflection = CreateReflection(
            inputs: ImmutableCollectionsMarshal.AsImmutableArray(inputs),
            resources: ImmutableCollectionsMarshal.AsImmutableArray(resources),
            recordLayouts: ImmutableCollectionsMarshal.AsImmutableArray(recordLayouts),
            requiredCapabilities: ImmutableArray.Create(ShaderCapability.DoublePrecision, ShaderCapability.Float16));

        inputs[0] = new ShaderIoSignature("POSITION", 1, 8, ShaderValueType.Float3, ShaderInterpolation.Flat);
        resources[0] = default;
        recordLayouts[0] = new ShaderRecordLayout("Changed", 16, [new ShaderFieldLayout("Value", ShaderValueType.UInt4, 0, 16, 1, 0)]);

        Assert.That(reflection.Inputs[0].Location, Is.EqualTo(0));
        Assert.That(reflection.Inputs[1].Location, Is.EqualTo(1));
        Assert.That(reflection.Resources[0].Group, Is.EqualTo(ShaderBindingGroup.Frame));
        Assert.That(reflection.Resources[1].Group, Is.EqualTo(ShaderBindingGroup.Material));
        Assert.That(reflection.RecordLayouts[0].Name, Is.EqualTo("ARecord"));
        Assert.That(reflection.RequiredCapabilities, Is.EqualTo([
            ShaderCapability.Float16,
            ShaderCapability.DoublePrecision,
        ]));
        _ = Assert.Throws<ArgumentException>(() => CreateReflection(
            resources: ImmutableArray.Create(
                new ShaderResourceSignature(ShaderBindingGroup.Pass, 0, ShaderResourceKind.StorageImage, 1),
                new ShaderResourceSignature(ShaderBindingGroup.Pass, 0, ShaderResourceKind.StorageBuffer, 1))));
        _ = Assert.Throws<ArgumentException>(() => CreateReflection(
            inputs: ImmutableArray.Create(
                new ShaderIoSignature("POSITION", 0, 0, ShaderValueType.Float3, ShaderInterpolation.Smooth),
                new ShaderIoSignature("NORMAL", 0, 0, ShaderValueType.Float3, ShaderInterpolation.Smooth))));
    }

    [Test]
    public void ShaderReflectionRequiresStageConsistentGroupSizesAndInitializedArrays()
    {
        var compute = CreateReflection(
            stage: ShaderStage.Compute,
            groupSizeX: 4,
            groupSizeY: 4,
            groupSizeZ: 4);

        Assert.That(compute.GroupSizeX * compute.GroupSizeY * compute.GroupSizeZ, Is.EqualTo(64));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateReflection(
            stage: ShaderStage.Compute,
            groupSizeX: 256,
            groupSizeY: 2,
            groupSizeZ: 1));
        _ = Assert.Throws<ArgumentException>(() => CreateReflection(
            stage: ShaderStage.Compute,
            inputs: ImmutableArray.Create(new ShaderIoSignature(
                "SV_DispatchThreadID",
                0,
                null,
                ShaderValueType.UInt3,
                ShaderInterpolation.Flat)),
            groupSizeX: 1,
            groupSizeY: 1,
            groupSizeZ: 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateReflection(groupSizeX: 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => CreateReflection(bindingLayoutVersion: 0));
        _ = Assert.Throws<ArgumentException>(() => new ShaderReflection(
            ShaderStage.Vertex,
            "main",
            CreateCompilerIdentity(),
            default,
            [],
            [],
            [],
            [],
            1,
            0,
            0,
            0));
    }

    [Test]
    public void ShaderResourceSignatureEnforcesPortableBindingRanges()
    {
        Assert.That(new ShaderResourceSignature(
            ShaderBindingGroup.Frame,
            3,
            ShaderResourceKind.UniformBuffer,
            1).Binding, Is.EqualTo(3));
        Assert.That(new ShaderResourceSignature(
            ShaderBindingGroup.Material,
            15,
            ShaderResourceKind.SampledImage,
            1).Binding, Is.EqualTo(15));
        Assert.That(new ShaderResourceSignature(
            ShaderBindingGroup.Pass,
            7,
            ShaderResourceKind.StorageBuffer,
            1).Binding, Is.EqualTo(7));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ShaderResourceSignature(
            ShaderBindingGroup.Frame,
            4,
            ShaderResourceKind.UniformBuffer,
            1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ShaderResourceSignature(
            ShaderBindingGroup.Material,
            16,
            ShaderResourceKind.SampledImage,
            1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ShaderResourceSignature(
            ShaderBindingGroup.Frame,
            0,
            ShaderResourceKind.Sampler,
            0));
    }
}
