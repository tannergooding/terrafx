// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

internal readonly struct RenderSpriteGpuData
{
    internal const int SizeInBytes = 96;

    private const int FlagsOffset = 80;
    private const uint PixelSnapFlag = 1;
    private const int TintOffset = 64;

    private readonly uint _flags;
    private readonly Vector4 _tint;
    private readonly Matrix4x4 _world;

    private RenderSpriteGpuData(Matrix4x4 world, Vector4 tint, uint flags)
    {
        _world = world;
        _tint = tint;
        _flags = flags;
    }

    internal static RenderSpriteGpuData Create(RenderSpriteSnapshot sprite)
    {
        if (!SpatialMath.TryToFrameRelative(sprite.Position, sprite.Frame3, out var position))
        {
            throw new InvalidOperationException("The Sprite position is outside its captured spatial frame.");
        }

        var world = sprite.LinearTransform * Matrix4x4.CreateTranslation(position);

        if (!world.IsFinite().All())
        {
            throw new InvalidOperationException("The Sprite world transform is not finite in its captured spatial frame.");
        }

        return new RenderSpriteGpuData(
            world,
            sprite.Tint,
            sprite.PixelSnap ? PixelSnapFlag : 0);
    }

    internal static int GetUniformAllocationStride(int vulkanAlignment)
        => ObjectGpuData.GetUniformAllocationStride(vulkanAlignment);

    private static void WriteSingle(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(
            destination.Slice(offset, sizeof(float)),
            BitConverter.SingleToInt32Bits(value));

    private static void WriteUInt32(Span<byte> destination, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, sizeof(uint)), value);

    private static void WriteVector4(Span<byte> destination, int offset, Vector4 value)
    {
        WriteSingle(destination, offset, value.X);
        WriteSingle(destination, offset + sizeof(float), value.Y);
        WriteSingle(destination, offset + (2 * sizeof(float)), value.Z);
        WriteSingle(destination, offset + (3 * sizeof(float)), value.W);
    }

    internal void WriteTo(Span<byte> destination)
    {
        if (destination.Length < SizeInBytes)
        {
            throw new ArgumentException("The destination is smaller than the Sprite GPU record.", nameof(destination));
        }

        WriteVector4(destination, 0, _world.Row1);
        WriteVector4(destination, 16, _world.Row2);
        WriteVector4(destination, 32, _world.Row3);
        WriteVector4(destination, 48, _world.Row4);
        WriteVector4(destination, TintOffset, _tint);
        WriteUInt32(destination, FlagsOffset, _flags);
        WriteUInt32(destination, FlagsOffset + sizeof(uint), 0);
        WriteUInt32(destination, FlagsOffset + (2 * sizeof(uint)), 0);
        WriteUInt32(destination, FlagsOffset + (3 * sizeof(uint)), 0);
    }
}
