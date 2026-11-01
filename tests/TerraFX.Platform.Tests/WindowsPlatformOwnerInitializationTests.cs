// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class WindowsPlatformOwnerInitializationTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void InitializesPerMonitorV2StaOwnerPrerequisites()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            || (RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            Assert.Ignore("The Windows Platform owner targets Windows 11 x64.");
        }

        using var owner = WindowsPlatformOwnerInitialization.Initialize();

        Assert.That(owner.OwnerThreadId, Is.EqualTo(Environment.CurrentManagedThreadId));
        Assert.That(Thread.CurrentThread.GetApartmentState(), Is.EqualTo(ApartmentState.STA));
        _ = Assert.Throws<InvalidOperationException>(() => Task.Run(owner.Dispose).GetAwaiter().GetResult());
    }
}
