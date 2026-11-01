// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.Content.Tools.Tests;

[TestFixture]
internal sealed class ToolProcessRunnerTests
{
    private static readonly string[] s_dotnetVersionArguments = ["--version"];

    private static (string ExecutablePath, string[] Arguments) CreateLongRunningCommand()
        => OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe", ["/d", "/c", "ping -n 6 127.0.0.1 >NUL"])
            : ("/bin/sh", ["-c", "sleep 5"]);

    [Test]
    public async Task RunAsyncCapturesStandardOutputAndExitCode()
    {
        var result = await ToolProcessRunner.RunAsync(
            "dotnet",
            AppContext.BaseDirectory,
            s_dotnetVersionArguments,
            TimeSpan.FromSeconds(30),
            4096).ConfigureAwait(false);

        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.StandardOutput, Is.Not.Empty);
        Assert.That(result.StandardError, Is.Empty);
    }

    [Test]
    public void RunAsyncEnforcesCombinedOutputLimit()
    {
        _ = Assert.ThrowsAsync<IOException>(async () =>
            await ToolProcessRunner.RunAsync(
                "dotnet",
                AppContext.BaseDirectory,
                s_dotnetVersionArguments,
                TimeSpan.FromSeconds(30),
                1).ConfigureAwait(false));
    }

    [Test]
    public void RunAsyncTerminatesTimedOutProcessTree()
    {
        var (executablePath, arguments) = CreateLongRunningCommand();

        _ = Assert.ThrowsAsync<TimeoutException>(async () =>
            await ToolProcessRunner.RunAsync(
                executablePath,
                AppContext.BaseDirectory,
                arguments,
                TimeSpan.FromMilliseconds(50),
                4096).ConfigureAwait(false));
    }
}
