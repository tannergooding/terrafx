// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX.Numerics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class ObjectGpuDataTests
{
    private static bool WriteToShortDestination(ObjectGpuData value)
    {
        Span<byte> destination = stackalloc byte[ObjectGpuData.SizeInBytes - 1];
        destination.Fill(0xCC);

        try
        {
            value.WriteTo(destination);
        }
        catch (ArgumentException)
        {
            foreach (var item in destination)
            {
                if (item != 0xCC)
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    [Test]
    public void FixedOffsetsWriteRowsNormalsMetadataAndPreserveRawFloatBits()
    {
        var world = new Matrix4x4(
            new Vector4(1.25f, 2.5f, 3.75f, 4.125f),
            new Vector4(5.25f, 6.5f, 7.75f, 8.125f),
            new Vector4(9.25f, 10.5f, 11.75f, 12.125f),
            new Vector4(13.25f, 14.5f, 15.75f, 16.125f));
        var previousWorld = new Matrix4x4(
            new Vector4(21.25f, 22.5f, 23.75f, 24.125f),
            new Vector4(25.25f, 26.5f, 27.75f, 28.125f),
            new Vector4(29.25f, 30.5f, 31.75f, 32.125f),
            new Vector4(33.25f, 34.5f, 35.75f, 36.125f));
        var value = new ObjectGpuData(
            world,
            previousWorld,
            new Vector4(-0.0f, 42.5f, 43.5f, 44.5f),
            new Vector4(51.5f, 52.5f, 53.5f, 54.5f),
            new Vector4(61.5f, 62.5f, 63.5f, 64.5f),
            0x80000000u,
            0xFFFFFFFFu,
            0x12345678u,
            0x87654321u);
        Span<byte> data = stackalloc byte[ObjectGpuData.SizeInBytes];
        data.Fill(0xCC);

        value.WriteTo(data);

        var expectedFloats = new float[]
        {
            1.25f, 2.5f, 3.75f, 4.125f, 5.25f, 6.5f, 7.75f, 8.125f,
            9.25f, 10.5f, 11.75f, 12.125f, 13.25f, 14.5f, 15.75f, 16.125f,
            21.25f, 22.5f, 23.75f, 24.125f, 25.25f, 26.5f, 27.75f, 28.125f,
            29.25f, 30.5f, 31.75f, 32.125f, 33.25f, 34.5f, 35.75f, 36.125f,
            -0.0f, 42.5f, 43.5f, 44.5f, 51.5f, 52.5f, 53.5f, 54.5f,
            61.5f, 62.5f, 63.5f, 64.5f
        };

        for (var index = 0; index < expectedFloats.Length; index++)
        {
            var actual = MemoryMarshal.Read<float>(data.Slice(index * sizeof(float), sizeof(float)));
            Assert.That(BitConverter.SingleToInt32Bits(actual), Is.EqualTo(BitConverter.SingleToInt32Bits(expectedFloats[index])), $"float component {index}");
        }

        var expectedMetadata = new uint[] { 0x80000000u, 0xFFFFFFFFu, 0x12345678u, 0x87654321u };

        for (var index = 0; index < expectedMetadata.Length; index++)
        {
            var actual = MemoryMarshal.Read<uint>(data.Slice(176 + (index * sizeof(uint)), sizeof(uint)));
            Assert.That(actual, Is.EqualTo(expectedMetadata[index]), $"metadata component {index}");
        }
    }

    [TestCase(1, 256)]
    [TestCase(256, 256)]
    [TestCase(512, 512)]
    [TestCase(768, 768)]
    [TestCase(300, 19200)]
    public void StrideUsesLeastCommonMultipleOfNativeAlignment(int vulkanAlignment, int expectedStride)
        => Assert.That(ObjectGpuData.GetUniformAllocationStride(vulkanAlignment), Is.EqualTo(expectedStride));

    [Test]
    public void ValidationRejectsShortDestinationAndInvalidAlignment()
    {
        var value = new ObjectGpuData(
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            Vector4.UnitX,
            Vector4.UnitY,
            Vector4.UnitZ,
            0,
            0,
            0,
            0);

        Assert.That(WriteToShortDestination(value), Is.True);
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ObjectGpuData.GetUniformAllocationStride(0));
        _ = Assert.Throws<OverflowException>(() => ObjectGpuData.GetUniformAllocationStride(int.MaxValue));
    }

    [Test]
    public void VectorsExpandWithDirectionAndPositionW()
    {
        var value = new Vector3(2.0f, -3.0f, 5.0f);
        Assert.That(ObjectGpuData.Direction(value), Is.EqualTo(new Vector4(2.0f, -3.0f, 5.0f, 0.0f)));
        Assert.That(ObjectGpuData.Position(value), Is.EqualTo(new Vector4(2.0f, -3.0f, 5.0f, 1.0f)));
    }
}
