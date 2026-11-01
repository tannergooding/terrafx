// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using NUnit.Framework;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed class WindowsRawInputRegistrationTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    [SupportedOSPlatform("windows6.1")]
    public void RegistersForegroundKeyboardAndMouseOnceAndReleasesAdmission()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            || (RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            Assert.Ignore("The Windows Platform owner targets Windows 11 x64.");
        }

        using var owner = WindowsPlatformOwnerInitialization.Initialize();
        var registration = WindowsRawInputRegistration.Register(owner);

        try
        {
            var exception = Assert.Throws<NativeHostAlreadyActiveException>(
                () => WindowsRawInputRegistration.Register(owner));

            Assert.That(exception?.Message, Does.Contain(NativeHostAlreadyActiveException.Code));
            _ = Assert.Throws<InvalidOperationException>(owner.Dispose);
        }
        finally
        {
            registration.Dispose();
        }

        using var replacement = WindowsRawInputRegistration.Register(owner);
    }
}
