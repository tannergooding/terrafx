// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;

namespace TerraFX.UnitTests;

/// <summary>Provides a set of tests covering the <see cref="UnmanagedArray{T}" /> struct.</summary>
internal static class UnmanagedArrayTests
{
    /// <summary>Provides validation of the <see cref="UnmanagedArray{T}" /> constructor.</summary>
    [Test]
    public static void CtorNUIntNUIntBooleanTest()
    {
        Assert.That(() => new UnmanagedArray<int>(nuint.MaxValue),
            Throws.InstanceOf<OverflowException>()
        );
    }
}
