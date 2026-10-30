// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using TerraFX.Numerics;

namespace TerraFX;

public static partial class EbinPayloadWriter
{
    /// <summary>Writes a fixed array payload from already encoded elements.</summary>
    /// <param name="elementKind">The fixed-size element wire kind.</param>
    /// <param name="count">The element count.</param>
    /// <param name="canonicalElements">The tightly packed encoded element bytes.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction, encoded-input, or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(
        ushort elementKind,
        ulong count,
        scoped ReadOnlySpan<byte> canonicalElements,
        EbinReadLimits limits,
        scoped Span<byte> payloadDestination,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
        => WriteRawFixedArray(elementKind, count, canonicalElements, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes an RFC-order GUID fixed array payload.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Guid> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<Guid, GuidElementWriter>(values, 11, 16, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes a 3-by-2 matrix fixed array payload in row order.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Matrix3x2> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<Matrix3x2, Matrix3x2ElementWriter>(values, 18, 24, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes a 4-by-4 matrix fixed array payload in row order.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Matrix4x4> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<Matrix4x4, Matrix4x4ElementWriter>(values, 19, 64, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes a quaternion fixed array payload in component order.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Quaternion> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<Quaternion, QuaternionElementWriter>(values, 17, 16, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes a 2D spatial-position fixed array payload.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<SpatialPosition2> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<SpatialPosition2, SpatialPosition2ElementWriter>(values, 20, 16, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes a 3D spatial-position fixed array payload.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<SpatialPosition3> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<SpatialPosition3, SpatialPosition3ElementWriter>(values, 21, 24, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes a 2D vector fixed array payload in component order.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Vector2> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<Vector2, Vector2ElementWriter>(values, 14, 8, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes a 3D vector fixed array payload in component order.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Vector3> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<Vector3, Vector3ElementWriter>(values, 15, 12, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes a 4D vector fixed array payload in component order.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<Vector4> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<Vector4, Vector4ElementWriter>(values, 16, 16, limits, payloadDestination, out payloadBytes, out diagnostic);
    /// <summary>Writes a Boolean fixed array payload.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<bool> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<bool, BooleanElementWriter>(values, 9, 1, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes an unsigned 8-bit fixed array payload.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<byte> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<byte, ByteElementWriter>(values, 1, 1, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes a binary32 fixed array payload.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<float> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<float, FloatElementWriter>(values, 10, 4, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes a signed 32-bit fixed array payload.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<int> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<int, Int32ElementWriter>(values, 7, 4, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes a signed 64-bit fixed array payload.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<long> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<long, Int64ElementWriter>(values, 8, 8, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes a signed 8-bit fixed array payload.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<sbyte> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<sbyte, Int8ElementWriter>(values, 5, 1, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes a signed 16-bit fixed array payload.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<short> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<short, Int16ElementWriter>(values, 6, 2, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes an unsigned 32-bit fixed array payload.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<uint> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<uint, UInt32ElementWriter>(values, 3, 4, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes an unsigned 64-bit fixed array payload.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<ulong> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<ulong, UInt64ElementWriter>(values, 4, 8, limits, payloadDestination, out payloadBytes, out diagnostic);

    /// <summary>Writes an unsigned 16-bit fixed array payload.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="limits">The structural limits for the payload.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or workspace failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus WriteFixedArray(scoped ReadOnlySpan<ushort> values, EbinReadLimits limits, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
        => WriteTypedFixedArray<ushort, UInt16ElementWriter>(values, 2, 2, limits, payloadDestination, out payloadBytes, out diagnostic);

    private static EbinStructuralDiagnostic CreateArrayCapacityDiagnostic(int capacity, int required)
        => CreateArrayDiagnostic("workspace-too-small", checked((ulong)capacity), checked((ulong)required), EbinStructuralDiagnosticSource.Construction);

    private static EbinStructuralDiagnostic CreateArrayDiagnostic(
        string category,
        ulong claim,
        ulong limit,
        EbinStructuralDiagnosticSource source)
        => new EbinStructuralDiagnostic(Guid.Empty, 0, category, null, claim, limit, source);

    private static EbinWriteStatus GetFixedArrayExtent(
        ulong count,
        int stride,
        EbinReadLimits limits,
        out ulong extent,
        out EbinStructuralDiagnostic? diagnostic)
    {
        extent = 0;
        diagnostic = null;

        if (count > (ulong)limits.MaximumArrayElements)
        {
            diagnostic = CreateArrayDiagnostic("array-count-budget", count, checked((ulong)limits.MaximumArrayElements), EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.InvalidRecord;
        }

        if (count > checked((ulong)limits.MaximumAggregateArrayElements))
        {
            diagnostic = CreateArrayDiagnostic("aggregate-array-budget", count, checked((ulong)limits.MaximumAggregateArrayElements), EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.InvalidRecord;
        }

        if (!EbinCodec.TryMultiply(count, checked((ulong)stride), out var elementBytes)
            || !EbinCodec.TryAdd(32, elementBytes, out extent))
        {
            diagnostic = CreateArrayDiagnostic("array-product-overflow", count, 0, EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.InvalidRecord;
        }

        if (extent > (ulong)limits.MaximumRecordBytes)
        {
            diagnostic = CreateArrayDiagnostic("record-byte-budget", extent, checked((ulong)limits.MaximumRecordBytes), EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.InvalidRecord;
        }

        return EbinWriteStatus.Complete;
    }

    private static void WriteArrayHeader(ushort elementKind, int stride, int extent, scoped Span<byte> destination)
    {
        destination[..extent].Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(destination[16..], elementKind);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[20..], checked((uint)stride));
    }

    private static EbinWriteStatus WriteRawFixedArray(
        ushort elementKind,
        ulong count,
        scoped ReadOnlySpan<byte> canonicalElements,
        EbinReadLimits limits,
        scoped Span<byte> payloadDestination,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(limits);

        payloadBytes = 0;
        diagnostic = null;
        var stride = EbinCodec.Stride(elementKind);

        if (stride == 0)
        {
            diagnostic = CreateArrayDiagnostic("fixedarray-stride", 0, 0, EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.InvalidRecord;
        }

        var preflight = GetFixedArrayExtent(count, stride, limits, out var extent, out diagnostic);

        if (preflight != EbinWriteStatus.Complete)
        {
            return preflight;
        }

        var elementBytes = extent - 32;

        if (elementBytes != (ulong)canonicalElements.Length)
        {
            diagnostic = CreateArrayDiagnostic(
                "fixedarray-extent",
                checked((ulong)canonicalElements.Length),
                elementBytes,
                EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.InvalidRecord;
        }

        if (elementKind == 9)
        {
            for (var index = 0; index < canonicalElements.Length; index++)
            {
                if (canonicalElements[index] > 1)
                {
                    diagnostic = CreateArrayDiagnostic("boolean-domain", canonicalElements[index], 1, EbinStructuralDiagnosticSource.EncodedInput);
                    return EbinWriteStatus.InvalidRecord;
                }
            }
        }

        if (payloadDestination.Overlaps(canonicalElements))
        {
            throw new ArgumentException("The payload destination must not overlap the element input.", nameof(payloadDestination));
        }

        if (payloadDestination.Length < checked((int)extent))
        {
            diagnostic = CreateArrayCapacityDiagnostic(payloadDestination.Length, checked((int)extent));
            return EbinWriteStatus.WorkspaceTooSmall;
        }

        WriteArrayHeader(elementKind, stride, checked((int)extent), payloadDestination);
        canonicalElements.CopyTo(payloadDestination[32..]);
        payloadBytes = checked((int)extent);
        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteTypedFixedArray<T, TWriter>(
        scoped ReadOnlySpan<T> values,
        ushort elementKind,
        int stride,
        EbinReadLimits limits,
        scoped Span<byte> payloadDestination,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
        where T : unmanaged
        where TWriter : struct, IFixedArrayElementWriter<T>
    {
        ArgumentNullException.ThrowIfNull(limits);

        payloadBytes = 0;
        diagnostic = null;
        var count = checked((ulong)values.Length);
        var preflight = GetFixedArrayExtent(count, stride, limits, out var extent, out diagnostic);

        if (preflight != EbinWriteStatus.Complete)
        {
            return preflight;
        }

        if (payloadDestination.Overlaps(MemoryMarshal.AsBytes(values)))
        {
            throw new ArgumentException("The payload destination must not overlap the element input.", nameof(payloadDestination));
        }

        if (payloadDestination.Length < checked((int)extent))
        {
            diagnostic = CreateArrayCapacityDiagnostic(payloadDestination.Length, checked((int)extent));
            return EbinWriteStatus.WorkspaceTooSmall;
        }

        var requiredBytes = checked((int)extent);
        WriteArrayHeader(elementKind, stride, requiredBytes, payloadDestination);

        for (var index = 0; index < values.Length; index++)
        {
            var destination = payloadDestination.Slice(32 + (index * stride), stride);
            TWriter.Write(values[index], destination);
        }

        payloadBytes = requiredBytes;
        return EbinWriteStatus.Complete;
    }

    private readonly struct BooleanElementWriter : IFixedArrayElementWriter<bool>
    {
        public static void Write(bool value, scoped Span<byte> destination) => destination[0] = value ? (byte)1 : (byte)0;
    }

    private readonly struct ByteElementWriter : IFixedArrayElementWriter<byte>
    {
        public static void Write(byte value, scoped Span<byte> destination) => destination[0] = value;
    }

    private readonly struct FloatElementWriter : IFixedArrayElementWriter<float>
    {
        public static void Write(float value, scoped Span<byte> destination) => WriteSingle(destination, 0, value);
    }

    private readonly struct GuidElementWriter : IFixedArrayElementWriter<Guid>
    {
        public static void Write(Guid value, scoped Span<byte> destination) => _ = value.TryWriteBytes(destination, bigEndian: true, out _);
    }

    private interface IFixedArrayElementWriter<T>
        where T : unmanaged
    {
        static abstract void Write(T value, scoped Span<byte> destination);
    }

    private readonly struct Int16ElementWriter : IFixedArrayElementWriter<short>
    {
        public static void Write(short value, scoped Span<byte> destination) => BinaryPrimitives.WriteInt16LittleEndian(destination, value);
    }

    private readonly struct Int32ElementWriter : IFixedArrayElementWriter<int>
    {
        public static void Write(int value, scoped Span<byte> destination) => BinaryPrimitives.WriteInt32LittleEndian(destination, value);
    }

    private readonly struct Int64ElementWriter : IFixedArrayElementWriter<long>
    {
        public static void Write(long value, scoped Span<byte> destination) => BinaryPrimitives.WriteInt64LittleEndian(destination, value);
    }

    private readonly struct Int8ElementWriter : IFixedArrayElementWriter<sbyte>
    {
        public static void Write(sbyte value, scoped Span<byte> destination) => destination[0] = unchecked((byte)value);
    }

    private readonly struct Matrix3x2ElementWriter : IFixedArrayElementWriter<Matrix3x2>
    {
        public static void Write(Matrix3x2 value, scoped Span<byte> destination)
        {
            WriteSingle(destination, 0, value.Row1.X);
            WriteSingle(destination, 4, value.Row1.Y);
            WriteSingle(destination, 8, value.Row2.X);
            WriteSingle(destination, 12, value.Row2.Y);
            WriteSingle(destination, 16, value.Row3.X);
            WriteSingle(destination, 20, value.Row3.Y);
        }
    }

    private readonly struct Matrix4x4ElementWriter : IFixedArrayElementWriter<Matrix4x4>
    {
        public static void Write(Matrix4x4 value, scoped Span<byte> destination)
        {
            WriteSingle(destination, 0, value.Row1.X);
            WriteSingle(destination, 4, value.Row1.Y);
            WriteSingle(destination, 8, value.Row1.Z);
            WriteSingle(destination, 12, value.Row1.W);
            WriteSingle(destination, 16, value.Row2.X);
            WriteSingle(destination, 20, value.Row2.Y);
            WriteSingle(destination, 24, value.Row2.Z);
            WriteSingle(destination, 28, value.Row2.W);
            WriteSingle(destination, 32, value.Row3.X);
            WriteSingle(destination, 36, value.Row3.Y);
            WriteSingle(destination, 40, value.Row3.Z);
            WriteSingle(destination, 44, value.Row3.W);
            WriteSingle(destination, 48, value.Row4.X);
            WriteSingle(destination, 52, value.Row4.Y);
            WriteSingle(destination, 56, value.Row4.Z);
            WriteSingle(destination, 60, value.Row4.W);
        }
    }

    private readonly struct QuaternionElementWriter : IFixedArrayElementWriter<Quaternion>
    {
        public static void Write(Quaternion value, scoped Span<byte> destination)
        {
            WriteSingle(destination, 0, value.X);
            WriteSingle(destination, 4, value.Y);
            WriteSingle(destination, 8, value.Z);
            WriteSingle(destination, 12, value.W);
        }
    }

    private readonly struct SpatialPosition2ElementWriter : IFixedArrayElementWriter<SpatialPosition2>
    {
        public static void Write(SpatialPosition2 value, scoped Span<byte> destination)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, value.Chunk.X);
            BinaryPrimitives.WriteInt32LittleEndian(destination[4..], value.Chunk.Y);
            WriteSingle(destination, 8, value.Offset.X);
            WriteSingle(destination, 12, value.Offset.Y);
        }
    }

    private readonly struct SpatialPosition3ElementWriter : IFixedArrayElementWriter<SpatialPosition3>
    {
        public static void Write(SpatialPosition3 value, scoped Span<byte> destination)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, value.Chunk.X);
            BinaryPrimitives.WriteInt32LittleEndian(destination[4..], value.Chunk.Y);
            BinaryPrimitives.WriteInt32LittleEndian(destination[8..], value.Chunk.Z);
            WriteSingle(destination, 12, value.Offset.X);
            WriteSingle(destination, 16, value.Offset.Y);
            WriteSingle(destination, 20, value.Offset.Z);
        }
    }

    private readonly struct UInt16ElementWriter : IFixedArrayElementWriter<ushort>
    {
        public static void Write(ushort value, scoped Span<byte> destination) => BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
    }

    private readonly struct UInt32ElementWriter : IFixedArrayElementWriter<uint>
    {
        public static void Write(uint value, scoped Span<byte> destination) => BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
    }

    private readonly struct UInt64ElementWriter : IFixedArrayElementWriter<ulong>
    {
        public static void Write(ulong value, scoped Span<byte> destination) => BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
    }

    private readonly struct Vector2ElementWriter : IFixedArrayElementWriter<Vector2>
    {
        public static void Write(Vector2 value, scoped Span<byte> destination)
        {
            WriteSingle(destination, 0, value.X);
            WriteSingle(destination, 4, value.Y);
        }
    }

    private readonly struct Vector3ElementWriter : IFixedArrayElementWriter<Vector3>
    {
        public static void Write(Vector3 value, scoped Span<byte> destination)
        {
            WriteSingle(destination, 0, value.X);
            WriteSingle(destination, 4, value.Y);
            WriteSingle(destination, 8, value.Z);
        }
    }

    private readonly struct Vector4ElementWriter : IFixedArrayElementWriter<Vector4>
    {
        public static void Write(Vector4 value, scoped Span<byte> destination)
        {
            WriteSingle(destination, 0, value.X);
            WriteSingle(destination, 4, value.Y);
            WriteSingle(destination, 8, value.Z);
            WriteSingle(destination, 12, value.W);
        }
    }
}
