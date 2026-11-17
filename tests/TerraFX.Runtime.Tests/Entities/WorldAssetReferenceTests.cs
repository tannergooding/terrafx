// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

internal sealed class WorldAssetReferenceTests
{
    [Test]
    public void AssetAndMajorRemainDistinctInThePackedInvalidDefaultValue()
    {
        var absent = default(WorldAssetReference);
        var major1 = new WorldAssetReference(assetId: 37, schemaMajor: 1);
        var major2 = new WorldAssetReference(assetId: 37, schemaMajor: 2);

        Assert.Multiple(() => {
            Assert.That(Unsafe.SizeOf<WorldAssetReference>(), Is.EqualTo(8));
            Assert.That(Marshal.OffsetOf<WorldAssetReference>("_assetId").ToInt32(), Is.Zero);
            Assert.That(Marshal.OffsetOf<WorldAssetReference>("_schemaMajor").ToInt32(), Is.EqualTo(4));
            Assert.That(absent.IsValid, Is.False);
            Assert.That(major1.IsValid, Is.True);
            Assert.That(major1.AssetId, Is.EqualTo(37));
            Assert.That(major1.SchemaMajor, Is.EqualTo(1));
            Assert.That(major2.AssetId, Is.EqualTo(37));
            Assert.That(major2.SchemaMajor, Is.EqualTo(2));
            _ = Assert.Throws<InvalidOperationException>(() => _ = absent.AssetId);
            _ = Assert.Throws<InvalidOperationException>(() => _ = absent.SchemaMajor);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new WorldAssetReference(0, 1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new WorldAssetReference(1, 0));
        });
    }
}
