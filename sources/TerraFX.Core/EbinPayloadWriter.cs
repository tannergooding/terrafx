// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX.Numerics;

namespace TerraFX;

/// <summary>Writes individual EBIN payload representations.</summary>
public static partial class EbinPayloadWriter
{
    /// <summary>Writes one 128-bit identifier in RFC byte order.</summary>
    /// <param name="value">The identifier to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(Guid value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[16];
        _ = value.TryWriteBytes(payload, bigEndian: true, out _);
        return WriteScalar(11, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one 3-by-2 matrix in row order.</summary>
    /// <param name="value">The matrix to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(Matrix3x2 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[24];
        WriteSingle(payload, 0, value.Row1.X);
        WriteSingle(payload, 4, value.Row1.Y);
        WriteSingle(payload, 8, value.Row2.X);
        WriteSingle(payload, 12, value.Row2.Y);
        WriteSingle(payload, 16, value.Row3.X);
        WriteSingle(payload, 20, value.Row3.Y);
        return WriteScalar(18, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one 4-by-4 matrix in row order.</summary>
    /// <param name="value">The matrix to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(Matrix4x4 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[64];
        WriteSingle(payload, 0, value.Row1.X);
        WriteSingle(payload, 4, value.Row1.Y);
        WriteSingle(payload, 8, value.Row1.Z);
        WriteSingle(payload, 12, value.Row1.W);
        WriteSingle(payload, 16, value.Row2.X);
        WriteSingle(payload, 20, value.Row2.Y);
        WriteSingle(payload, 24, value.Row2.Z);
        WriteSingle(payload, 28, value.Row2.W);
        WriteSingle(payload, 32, value.Row3.X);
        WriteSingle(payload, 36, value.Row3.Y);
        WriteSingle(payload, 40, value.Row3.Z);
        WriteSingle(payload, 44, value.Row3.W);
        WriteSingle(payload, 48, value.Row4.X);
        WriteSingle(payload, 52, value.Row4.Y);
        WriteSingle(payload, 56, value.Row4.Z);
        WriteSingle(payload, 60, value.Row4.W);
        return WriteScalar(19, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one quaternion in component order.</summary>
    /// <param name="value">The quaternion to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(Quaternion value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[16];
        WriteSingle(payload, 0, value.X);
        WriteSingle(payload, 4, value.Y);
        WriteSingle(payload, 8, value.Z);
        WriteSingle(payload, 12, value.W);
        return WriteScalar(17, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one signed spatial position.</summary>
    /// <param name="value">The position to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(SpatialPosition2 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(payload, value.Chunk.X);
        BinaryPrimitives.WriteInt32LittleEndian(payload[4..], value.Chunk.Y);
        WriteSingle(payload, 8, value.Offset.X);
        WriteSingle(payload, 12, value.Offset.Y);
        return WriteScalar(20, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one signed spatial position.</summary>
    /// <param name="value">The position to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(SpatialPosition3 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(payload, value.Chunk.X);
        BinaryPrimitives.WriteInt32LittleEndian(payload[4..], value.Chunk.Y);
        BinaryPrimitives.WriteInt32LittleEndian(payload[8..], value.Chunk.Z);
        WriteSingle(payload, 12, value.Offset.X);
        WriteSingle(payload, 16, value.Offset.Y);
        WriteSingle(payload, 20, value.Offset.Z);
        return WriteScalar(21, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one 2D vector in component order.</summary>
    /// <param name="value">The vector to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(Vector2 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[8];
        WriteSingle(payload, 0, value.X);
        WriteSingle(payload, 4, value.Y);
        return WriteScalar(14, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one 3D vector in component order.</summary>
    /// <param name="value">The vector to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(Vector3 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[12];
        WriteSingle(payload, 0, value.X);
        WriteSingle(payload, 4, value.Y);
        WriteSingle(payload, 8, value.Z);
        return WriteScalar(15, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one 4D vector in component order.</summary>
    /// <param name="value">The vector to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(Vector4 value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[16];
        WriteSingle(payload, 0, value.X);
        WriteSingle(payload, 4, value.Y);
        WriteSingle(payload, 8, value.Z);
        WriteSingle(payload, 12, value.W);
        return WriteScalar(16, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one Boolean value.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(bool value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = [value ? (byte)1 : (byte)0];
        return WriteScalar(9, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one unsigned 8-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(byte value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = [value];
        return WriteScalar(1, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one binary32 value.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(float value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, BitConverter.SingleToInt32Bits(value));
        return WriteScalar(10, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one signed 32-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(int value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, value);
        return WriteScalar(7, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one signed 64-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(long value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(payload, value);
        return WriteScalar(8, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one signed 8-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(sbyte value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = [unchecked((byte)value)];
        return WriteScalar(5, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one signed 16-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(short value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(payload, value);
        return WriteScalar(6, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one unsigned 32-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(uint value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, value);
        return WriteScalar(3, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one unsigned 64-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(ulong value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(payload, value);
        return WriteScalar(4, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    /// <summary>Writes one unsigned 16-bit integer.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="payloadDestination">The payload destination.</param>
    /// <param name="payloadBytes">The payload length, or zero on failure.</param>
    /// <param name="diagnostic">The construction or destination failure.</param>
    /// <returns>The payload write status.</returns>
    public static EbinWriteStatus Write(ushort value, scoped Span<byte> payloadDestination, out int payloadBytes, out EbinStructuralDiagnostic? diagnostic)
    {
        Span<byte> payload = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(payload, value);
        return WriteScalar(2, payload, payloadDestination, out payloadBytes, out diagnostic);
    }

    private static EbinWriteStatus WriteScalar(
        ushort kind,
        scoped ReadOnlySpan<byte> payload,
        scoped Span<byte> payloadDestination,
        out int payloadBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var status = EbinCodec.WritePayload(kind, 1, payload, payloadDestination, out payloadBytes, out var writeDiagnostic);

        if (status == EbinStatus.Complete)
        {
            diagnostic = null;
            return EbinWriteStatus.Complete;
        }

        if (status == EbinStatus.DestinationTooSmall)
        {
            payloadBytes = 0;
            diagnostic = new EbinStructuralDiagnostic(
                Guid.Empty,
                0,
                "workspace-too-small",
                null,
                checked((ulong)payloadDestination.Length),
                checked((ulong)payload.Length),
                EbinStructuralDiagnosticSource.Construction);
            return EbinWriteStatus.WorkspaceTooSmall;
        }

        payloadBytes = 0;
        var category = writeDiagnostic.Category ?? throw new InvalidOperationException("The EBIN payload writer omitted a failure category.");
        diagnostic = new EbinStructuralDiagnostic(
            writeDiagnostic.SchemaId,
            writeDiagnostic.Tag,
            category,
            (writeDiagnostic.Offset == 0) ? null : writeDiagnostic.Offset,
            writeDiagnostic.Claim,
            writeDiagnostic.Limit,
            EbinStructuralDiagnosticSource.Construction);
        return status == EbinStatus.UnsupportedSchema
            ? EbinWriteStatus.UnsupportedSchema
            : EbinWriteStatus.InvalidRecord;
    }

    private static void WriteSingle(scoped Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], BitConverter.SingleToInt32Bits(value));
}
