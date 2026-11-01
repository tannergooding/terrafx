// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class D3D12BackendRegistrationTests
{
    [Test]
    public void UnsafeDisabledNonfriendConsumerRegistersD3D12WithoutNativeActivation()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 registration consumer targets Windows 10 version 21H2 or later.");
        }

        var testDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = testDirectory.Parent
            ?? throw new InvalidOperationException("Missing configuration directory.");
        var projectDirectory = configuration.Parent
            ?? throw new InvalidOperationException("Missing project directory.");
        var testsDirectory = projectDirectory.Parent
            ?? throw new InvalidOperationException("Missing tests output directory.");
        var consumer = Path.Combine(
            testsDirectory.FullName,
            "TerraFX.Graphics.D3D12.Consumer",
            configuration.Name,
            testDirectory.Name,
            "TerraFX.Graphics.D3D12.Consumer.dll");
        Assert.That(File.Exists(consumer), Is.True, "The project reference must build the separate nonfriend consumer.");

        var start = new ProcessStartInfo("dotnet") {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(consumer);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The D3D12 public consumer process did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(60000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The D3D12 public consumer exceeded its 60-second execution bound.");
        }

        Task.WaitAll(output, errors);
        Assert.That(process.ExitCode, Is.Zero, output.Result + errors.Result);
        Assert.That(output.Result, Does.Contain("D3D12 public registration consumer passed"));
    }
}
