// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldOptionsTests
{
    [Test]
    public void CpuWorkerCountDefaultsToTheDocumentedBoundedPoolSize()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var options = new WorldOptions(registration);
        var expected = Math.Min(64, Math.Max(1, Environment.ProcessorCount - 2));

        Assert.That(options.ValidateAndResolve().CpuWorkerCount, Is.EqualTo(expected));
    }

    [Test]
    public void CpuWorkerCountUsesTheExactRequestedValue()
    {
        var registration = new WorldRegistrationBuilder(new CompiledContentSchemaRegistry([])).Build();
        var inlineOptions = new WorldOptions(registration) { CpuWorkerCount = 0 };
        var explicitOptions = new WorldOptions(registration) { CpuWorkerCount = 4 };

        Assert.That(inlineOptions.ValidateAndResolve().CpuWorkerCount, Is.Zero);
        Assert.That(explicitOptions.ValidateAndResolve().CpuWorkerCount, Is.EqualTo(4));
    }
}
