// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Runtime.Versioning;
using NUnit.Framework;

namespace TerraFX.Graphics.D3D12.Tests;

[TestFixture]
[SupportedOSPlatform("windows10.0.22000")]
internal sealed class D3D12StagingAdmissionTests
{
    [Test]
    public void ReusesExistingRingWhenRemainingBudgetFallsBelowItsCapacity()
    {
        Assert.That(
            D3D12StagingAdmission.ShouldUseDedicatedStaging(
                requestBytes: 1024,
                ringCapacityBytes: 4096,
                availableBudgetBytes: 512,
                ringResourceExists: true),
            Is.False);
    }

    [Test]
    public void UsesDedicatedAllocationWhenBudgetCannotAdmitRingButCoversRequest()
    {
        Assert.That(
            D3D12StagingAdmission.ShouldUseDedicatedStaging(
                requestBytes: 1024,
                ringCapacityBytes: 4096,
                availableBudgetBytes: 2048,
                ringResourceExists: false),
            Is.True);
    }

    [Test]
    public void UsesDedicatedAllocationWhenRequestExceedsRing()
    {
        Assert.That(
            D3D12StagingAdmission.ShouldUseDedicatedStaging(
                requestBytes: 8192,
                ringCapacityBytes: 4096,
                availableBudgetBytes: null,
                ringResourceExists: false),
            Is.True);
    }

    [Test]
    public void UsesRingWhenBudgetCoversItsCapacity()
    {
        Assert.That(
            D3D12StagingAdmission.ShouldUseDedicatedStaging(
                requestBytes: 1024,
                ringCapacityBytes: 4096,
                availableBudgetBytes: 4096,
                ringResourceExists: false),
            Is.False);
    }
}
