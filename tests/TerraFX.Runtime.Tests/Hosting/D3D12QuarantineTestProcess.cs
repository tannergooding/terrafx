// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using NUnit.Framework;

namespace TerraFX.Runtime.Tests.Hosting;

internal static class D3D12QuarantineTestProcess
{
    internal static void Run(string testFilter = "TestCategory=ProcessIsolatedQuarantine")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testFilter);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("The Runtime test configuration could not be resolved.");
        DirectoryInfo? repositoryDirectory = new(AppContext.BaseDirectory);

        while ((repositoryDirectory is not null)
            && !File.Exists(Path.Combine(repositoryDirectory.FullName, "TerraFX.slnx")))
        {
            repositoryDirectory = repositoryDirectory.Parent;
        }

        var repositoryRoot = repositoryDirectory?.FullName
            ?? throw new DirectoryNotFoundException("The repository root could not be resolved for the isolated D3D12 witness.");
        var projectPath = Path.Combine(
            repositoryRoot,
            "tests",
            "TerraFX.Runtime.Tests",
            "TerraFX.Runtime.Tests.csproj");
        var resultsDirectory = Path.Combine(
            Path.GetTempPath(),
            "TerraFX.Runtime.Tests",
            $"D3D12Quarantine-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(resultsDirectory);

        try
        {
            var dotnetPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            var startInfo = new ProcessStartInfo(string.IsNullOrWhiteSpace(dotnetPath) ? "dotnet" : dotnetPath) {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = repositoryRoot,
            };
            startInfo.ArgumentList.Add("test");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("--configuration");
            startInfo.ArgumentList.Add(configuration);
            startInfo.ArgumentList.Add("--no-build");
            startInfo.ArgumentList.Add("--no-restore");
            startInfo.ArgumentList.Add("--filter");
            startInfo.ArgumentList.Add(testFilter);
            startInfo.ArgumentList.Add("--logger");
            startInfo.ArgumentList.Add("trx;LogFileName=quarantine.trx");
            startInfo.ArgumentList.Add("--results-directory");
            startInfo.ArgumentList.Add(resultsDirectory);
            startInfo.Environment["TERRAFX_D3D12_QUARANTINE_CHILD"] = "1";

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The isolated D3D12 quarantine test process did not start.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(milliseconds: 120_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException("The isolated D3D12 quarantine test process did not exit within two minutes.");
            }

            var output = Task.WhenAll(standardOutput, standardError).GetAwaiter().GetResult();
            var processOutput = $"{output[0]}{Environment.NewLine}{output[1]}";

            Assert.That(process.ExitCode, Is.Zero, processOutput);

            var resultPath = Path.Combine(resultsDirectory, "quarantine.trx");

            if (!File.Exists(resultPath))
            {
                throw new InvalidOperationException($"The isolated D3D12 quarantine test produced no TRX file.{Environment.NewLine}{processOutput}");
            }

            var document = new XmlDocument();
            document.Load(resultPath);
            var counters = document.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
                ?? throw new InvalidOperationException($"The isolated D3D12 quarantine TRX has no result counters.{Environment.NewLine}{processOutput}");
            var total = ParseCounter("total");
            var passed = ParseCounter("passed");
            var failed = ParseCounter("failed");
            var testResults = document.SelectNodes("//*[local-name()='UnitTestResult']")
                ?? throw new InvalidOperationException($"The isolated D3D12 quarantine TRX has no test results.{Environment.NewLine}{processOutput}");
            var skipped = testResults.Cast<XmlNode>()
                .Count(static result => StringComparer.OrdinalIgnoreCase.Equals(result.Attributes?["outcome"]?.Value, "NotExecuted"));

            Assert.That(total, Is.EqualTo(1), processOutput);

            if (skipped != 0)
            {
                Assert.Ignore(processOutput);
                return;
            }

            Assert.That(passed, Is.EqualTo(1), processOutput);
            Assert.That(failed, Is.Zero, processOutput);

            int ParseCounter(string name)
            {
                var value = counters.Attributes?[name]?.Value
                    ?? throw new InvalidOperationException($"The isolated D3D12 quarantine TRX omitted the {name} counter.");
                return int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            if (Directory.Exists(resultsDirectory))
            {
                Directory.Delete(resultsDirectory, recursive: true);
            }
        }
    }
}
