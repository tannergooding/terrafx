// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using NUnit.Framework;
using TerraFX.Numerics;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class GeometryLayoutTests
{
    private static void AssertBits<T>(T value, float[] components)
        where T : unmanaged
    {
        Assert.That(MemoryMarshal.AsBytes(new[] { value }.AsSpan()).ToArray(), Is.EqualTo(MemoryMarshal.AsBytes(components.AsSpan()).ToArray()));
    }

    private static void AssertLayout<T>(int size, Type[] backing, string[] names)
        where T : unmanaged
    {
        Assert.That(typeof(T).IsLayoutSequential, Is.True);
        Assert.That(typeof(T).StructLayoutAttribute?.Pack, Is.Zero);
        Assert.That(typeof(T).StructLayoutAttribute?.Size, Is.Zero);
        Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<T>(), Is.False);
        Assert.That(Marshal.SizeOf<T>(), Is.EqualTo(size));
        var array = new T[2];
        Assert.That(MemoryMarshal.AsBytes(array.AsSpan()).Length, Is.EqualTo(size * 2));
        var fields = typeof(T).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).OrderBy(field => field.MetadataToken).ToArray();
        Assert.That(fields.Select(field => field.FieldType), Is.EqualTo(backing));
        Assert.That(fields.Select(field => field.Name), Is.EqualTo(names));

        for (var index = 0; index < fields.Length; index++)
        {
            Assert.That(fields[index].IsInitOnly, Is.True);
            Assert.That(Marshal.OffsetOf<T>(fields[index].Name).ToInt32(), Is.EqualTo(index * (size / fields.Length)));
        }
    }

    private static void AssertSameBits<TLeft, TRight>(TLeft left, TRight right)
        where TLeft : unmanaged
        where TRight : unmanaged
    {
        Assert.That(MemoryMarshal.AsBytes(new[] { left }.AsSpan()).ToArray(), Is.EqualTo(MemoryMarshal.AsBytes(new[] { right }.AsSpan()).ToArray()));
    }

    private static float Single(int bits) => BitConverter.Int32BitsToSingle(bits);

    [TestCase(0x0000_0000)]
    [TestCase(unchecked((int)0x8000_0000))]
    [TestCase(0x0000_0001)]
    [TestCase(0x007F_FFFF)]
    [TestCase(0x0080_0000)]
    [TestCase(0x3F80_0001)]
    [TestCase(0x7F7F_FFFF)]
    [TestCase(0x7F80_0000)]
    [TestCase(unchecked((int)0xFF80_0000))]
    [TestCase(0x7FC1_2345)]
    [TestCase(0x7F81_2345)]
    [TestCase(unchecked((int)0xFFC5_4321))]
    [TestCase(unchecked((int)0xFF85_4321))]
    public void ConstructorsAndBothInteropCallFormsPreserveEveryBit(int bits)
    {
        float[] components = [Single(bits), Single(bits ^ int.MinValue), Single(0x3F80_0001), Single(0x7F81_2345)];
        var v2 = new Vector2(components[0], components[1]);
        var v3 = new Vector3(v2, components[2]);
        var v4 = new Vector4(v3, components[3]);
        var q = new Quaternion(v3, components[3]);
        var m32 = new Matrix3x2(v2, new Vector2(components[2], components[3]), new Vector2(components[1], components[0]));
        var m44 = new Matrix4x4(v4, new Vector4(components[3], components[2], components[1], components[0]), v4, v4);

        AssertBits(v2, components[..2]);
        AssertBits(v3, components[..3]);
        AssertBits(v4, components);
        AssertBits(q, components);
        AssertBits(m32, [components[0], components[1], components[2], components[3], components[1], components[0]]);
        AssertBits(m44, [.. components, components[3], components[2], components[1], components[0], .. components, .. components]);
        AssertSameBits(v2, v2.AsSystemVector2());
        AssertSameBits(v3, v3.AsSystemVector3());
        AssertSameBits(v4, v4.AsSystemVector4());
        AssertSameBits(q, q.AsSystemQuaternion());
        AssertSameBits(m32, m32.AsSystemMatrix3x2());
        AssertSameBits(m44, m44.AsSystemMatrix4x4());
        AssertSameBits(v2, v2.AsSystemVector2().AsTerraFXVector2());
        AssertSameBits(v3, v3.AsSystemVector3().AsTerraFXVector3());
        AssertSameBits(v4, v4.AsSystemVector4().AsTerraFXVector4());
        AssertSameBits(q, q.AsSystemQuaternion().AsTerraFXQuaternion());
        AssertSameBits(m32, m32.AsSystemMatrix3x2().AsTerraFXMatrix3x2());
        AssertSameBits(m44, m44.AsSystemMatrix4x4().AsTerraFXMatrix4x4());
        AssertSameBits(v2, SystemNumericsExtensions.AsTerraFXVector2(v2.AsSystemVector2()));
        AssertSameBits(v3, SystemNumericsExtensions.AsTerraFXVector3(v3.AsSystemVector3()));
        AssertSameBits(v4, SystemNumericsExtensions.AsTerraFXVector4(v4.AsSystemVector4()));
        AssertSameBits(q, SystemNumericsExtensions.AsTerraFXQuaternion(q.AsSystemQuaternion()));
        AssertSameBits(m32, SystemNumericsExtensions.AsTerraFXMatrix3x2(m32.AsSystemMatrix3x2()));
        AssertSameBits(m44, SystemNumericsExtensions.AsTerraFXMatrix4x4(m44.AsSystemMatrix4x4()));
        AssertBits(new Vector2(components[0]), [components[0], components[0]]);
        AssertBits(new Vector3(components[0]), [components[0], components[0], components[0]]);
        AssertBits(new Vector4(components[0]), [components[0], components[0], components[0], components[0]]);
    }

    [Test]
    public void ConventionalBclProjectionAndRightHandedViewAreOnlyCopied()
    {
        var projection = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView(1, 1.5f, 0.1f, 100);
        var view = System.Numerics.Matrix4x4.CreateLookAt(System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitZ, System.Numerics.Vector3.UnitY);

        AssertSameBits(projection, projection.AsTerraFXMatrix4x4());
        AssertSameBits(view, view.AsTerraFXMatrix4x4());
        Assert.That(projection.AsTerraFXMatrix4x4()[2, 3], Is.EqualTo(-1));
    }

    [Test]
    public void DefaultIdentityRowsAndTheFinalVector3ExtentAreExplicit()
    {
        Assert.That(default(Vector2).All(0), Is.True);
        Assert.That(default(Vector3).All(0), Is.True);
        Assert.That(default(Vector4).All(0), Is.True);
        Assert.That(default(Quaternion), Is.EqualTo(new Quaternion(0, 0, 0, 0)));
        Assert.That(default(Quaternion), Is.Not.EqualTo(Quaternion.Identity));
        Assert.That(default(Matrix3x2).All(0), Is.True);
        Assert.That(default(Matrix4x4).All(0), Is.True);
        Assert.That(Matrix3x2.Identity.Row1, Is.EqualTo(Vector2.UnitX));
        Assert.That(Matrix3x2.Identity.Row2, Is.EqualTo(Vector2.UnitY));
        Assert.That(Matrix3x2.Identity.Row3, Is.EqualTo(Vector2.Zero));
        Assert.That(Matrix4x4.Identity.Row4, Is.EqualTo(Vector4.UnitW));

        Vector3[] values = [Vector3.One, new Vector3(1, 2, Single(0x7F81_2345))];
        var bytes = MemoryMarshal.AsBytes(values.AsSpan());
        Assert.That(bytes.Length, Is.EqualTo(24));
        var final = MemoryMarshal.Read<Vector3>(bytes[12..]);
        AssertSameBits(final, values[1]);
        Assert.That(final.IsFinite().Z, Is.EqualTo(0));
        AssertSameBits(final, final.AsSystemVector3().AsTerraFXVector3());
    }

    [TestCase(-1, 0)]
    [TestCase(0, -1)]
    [TestCase(3, 0)]
    [TestCase(0, 2)]
    public void Matrix3x2IndexerRejectsInvalidIndices(int row, int column)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = Matrix3x2.Identity[row, column]);
    }

    [TestCase(-1, 0)]
    [TestCase(0, -1)]
    [TestCase(4, 0)]
    [TestCase(0, 4)]
    public void Matrix4x4IndexerRejectsInvalidIndices(int row, int column)
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = Matrix4x4.Identity[row, column]);
    }

    [Test]
    public void PublicSurfaceIsSafeAndHasNoIntrinsicOrImplicitInterchange()
    {
        Assert.That(GeometryPublicConsumer.Exercise(), Is.True);

        Type[] types = [typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion), typeof(Matrix3x2), typeof(Matrix4x4)];

        foreach (var type in types)
        {
            foreach (var property in type.GetProperties())
            {
                Assert.That(property.SetMethod, Is.Null);
            }

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Assert.That(methods.Any(method => method.Name is "op_Implicit" or "op_Explicit" || method.ReturnType == typeof(Vector128<float>)), Is.False);
        }
    }

    [Test]
    public void StorageUsesTheExactAcceptedBackingWithoutPropertyFields()
    {
        AssertLayout<Vector2>(8, [typeof(System.Numerics.Vector2)], ["_value"]);
        AssertLayout<Vector3>(12, [typeof(System.Numerics.Vector3)], ["_value"]);
        AssertLayout<Vector4>(16, [typeof(Vector128<float>)], ["_value"]);
        AssertLayout<Quaternion>(16, [typeof(Vector128<float>)], ["_value"]);
        AssertLayout<Matrix3x2>(24, [typeof(Vector2), typeof(Vector2), typeof(Vector2)], ["_row1", "_row2", "_row3"]);
        AssertLayout<Matrix4x4>(64, [typeof(Vector4), typeof(Vector4), typeof(Vector4), typeof(Vector4)], ["_row1", "_row2", "_row3", "_row4"]);
        Assert.That(Marshal.OffsetOf<System.Numerics.Vector2>(nameof(System.Numerics.Vector2.Y)).ToInt32(), Is.EqualTo(4));
        Assert.That(Marshal.OffsetOf<System.Numerics.Vector3>(nameof(System.Numerics.Vector3.Z)).ToInt32(), Is.EqualTo(8));
        Assert.That(Marshal.OffsetOf<System.Numerics.Quaternion>(nameof(System.Numerics.Quaternion.W)).ToInt32(), Is.EqualTo(12));
    }
}
