// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class LinuxEvdevInputEventReaderTests
{
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
    public void RejectsPartialNativeEventBuffer()
    {
        _ = Assert.Throws<InvalidDataException>(
            () => LinuxEvdevInputEventReader.DecodeBuffer(new byte[23]));
    }
}
