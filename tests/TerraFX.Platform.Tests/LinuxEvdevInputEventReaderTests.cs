// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class LinuxEvdevInputEventReaderTests
{
    private static void WriteAbsoluteInfo(
        Span<byte> destination,
        int value,
        int minimum,
        int maximum,
        int fuzz,
        int flat,
        int resolution)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        BinaryPrimitives.WriteInt32LittleEndian(destination[4..], minimum);
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], maximum);
        BinaryPrimitives.WriteInt32LittleEndian(destination[12..], fuzz);
        BinaryPrimitives.WriteInt32LittleEndian(destination[16..], flat);
        BinaryPrimitives.WriteInt32LittleEndian(destination[20..], resolution);
    }

    private static void WriteEvent(
        Span<byte> destination,
        long seconds,
        long microseconds,
        ushort type,
        ushort code,
        int value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(destination, seconds);
        BinaryPrimitives.WriteInt64LittleEndian(destination[8..], microseconds);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[16..], type);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[18..], code);
        BinaryPrimitives.WriteInt32LittleEndian(destination[20..], value);
    }

    [Test]
    public void BuildsReadRequestsWithLinuxIoctlEncoding()
    {
        Assert.That(
            LinuxEvdevInputEventReader.BuildReadRequest(0x18, 96),
            Is.EqualTo(checked((nuint)0x80604518u)));
        Assert.That(
            LinuxEvdevInputEventReader.BuildReadRequest(0x40, 24),
            Is.EqualTo(checked((nuint)0x80184540u)));
    }

    [Test]
    public void DecodesNativeAbsoluteAxisInfo()
    {
        var buffer = new byte[24];
        WriteAbsoluteInfo(buffer, value: 17, minimum: -1000, maximum: 2000, fuzz: 5, flat: 100, resolution: 2);

        var (info, value) = LinuxEvdevInputEventReader.DecodeAbsoluteInfo(buffer);

        Assert.That(value, Is.EqualTo(17));
        Assert.That(info, Is.EqualTo(new LinuxEvdevAxisInfo(-1000, 2000, 5, 100)));
    }

    [Test]
    public void DecodesNativeEventBufferInOrder()
    {
        var buffer = new byte[48];
        WriteEvent(buffer.AsSpan(0, 24), seconds: 123, microseconds: 456_789, type: 1, code: 304, value: 1);
        WriteEvent(buffer.AsSpan(24, 24), seconds: 124, microseconds: 1, type: 3, code: 0, value: -17);

        Assert.That(
            LinuxEvdevInputEventReader.DecodeBuffer(buffer),
            Is.EqualTo([
                new LinuxEvdevInputEvent(123, 456_789, 1, 304, 1),
                new LinuxEvdevInputEvent(124, 1, 3, 0, -17),
            ]));
    }

    [TestCase("/dev/input/event0", true)]
    [TestCase("/dev/input/event42", true)]
    [TestCase("/dev/input/event", false)]
    [TestCase("/dev/input/event0/../event1", false)]
    [TestCase("/dev/input/event-1", false)]
    [TestCase("/dev/input/event3-extra", false)]
    [TestCase("/etc/input/event0", false)]
    public void RecognizesOnlyNamedEventNodePaths(string path, bool expected)
    {
        Assert.That(LinuxEvdevInputEventReader.IsEventNodePath(path), Is.EqualTo(expected));
    }

    [Test]
    public void RejectsInvalidAbsoluteInfoBufferSize()
    {
        _ = Assert.Throws<InvalidDataException>(
            () => LinuxEvdevInputEventReader.DecodeAbsoluteInfo(new byte[23]));
    }

    [Test]
    public void RejectsPartialNativeEventBuffer()
    {
        _ = Assert.Throws<InvalidDataException>(
            () => LinuxEvdevInputEventReader.DecodeBuffer(new byte[23]));
    }
}
