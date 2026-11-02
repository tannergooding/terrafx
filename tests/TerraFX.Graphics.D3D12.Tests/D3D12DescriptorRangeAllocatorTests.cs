// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.Versioning;
using NUnit.Framework;
using TerraFX.Graphics.D3D12;

namespace TerraFX.Graphics.D3D12.Tests;

[SupportedOSPlatform("windows10.0.22000.0")]
[TestFixture]
internal sealed class D3D12DescriptorRangeAllocatorTests
{
    [Test]
    public void AllocateRejectsInvalidCountsAndContiguousExhaustionWithoutMutation()
    {
        var allocator = new D3D12DescriptorRangeAllocator(capacity: 5);

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Allocate(0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Allocate(-1));
        _ = Assert.Throws<InvalidOperationException>(() => allocator.Allocate(6));
        Assert.That(allocator.AllocatedCount, Is.Zero);

        _ = allocator.Allocate(2);
        _ = allocator.Allocate(2);
        _ = Assert.Throws<InvalidOperationException>(() => allocator.Allocate(2));
        Assert.That(allocator.AllocatedCount, Is.EqualTo(4));
    }

    [Test]
    public void AllocateReturnsTheFirstAvailableContiguousRange()
    {
        var allocator = new D3D12DescriptorRangeAllocator(capacity: 8);

        Assert.That(allocator.Allocate(3), Is.EqualTo(0));
        Assert.That(allocator.Allocate(2), Is.EqualTo(3));
        Assert.That(allocator.Allocate(3), Is.EqualTo(5));
        Assert.That(allocator.AllocatedCount, Is.EqualTo(8));

        allocator.Release(3, 2);

        Assert.That(allocator.Allocate(2), Is.EqualTo(3));
        Assert.That(allocator.AllocatedCount, Is.EqualTo(8));

        allocator.Release(0, 3);
        allocator.Release(3, 2);
        allocator.Release(5, 3);

        Assert.That(allocator.AllocatedCount, Is.Zero);
    }

    [Test]
    public void ConstructorRejectsNonpositiveCapacity()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new D3D12DescriptorRangeAllocator(capacity: 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new D3D12DescriptorRangeAllocator(capacity: -1));
    }

    [Test]
    public void ReleaseRejectsOutOfRangeAndAlreadyFreeSlots()
    {
        var allocator = new D3D12DescriptorRangeAllocator(capacity: 4);
        _ = allocator.Allocate(2);

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Release(4, 1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => allocator.Release(3, 2));
        _ = Assert.Throws<InvalidOperationException>(() => allocator.Release(1, 2));
        Assert.That(allocator.AllocatedCount, Is.EqualTo(2));

        allocator.Release(0, 2);
        _ = Assert.Throws<InvalidOperationException>(() => allocator.Release(0, 2));
        Assert.That(allocator.AllocatedCount, Is.Zero);
    }

    [Test]
    public void ReleaseValidatesTheWholeRangeBeforeClearingAnySlot()
    {
        var allocator = new D3D12DescriptorRangeAllocator(capacity: 4);
        _ = allocator.Allocate(2);

        _ = Assert.Throws<InvalidOperationException>(() => allocator.Release(0, 3));
        Assert.That(allocator.AllocatedCount, Is.EqualTo(2));
        Assert.That(allocator.Allocate(2), Is.EqualTo(2));
        Assert.That(allocator.AllocatedCount, Is.EqualTo(4));
    }
}
