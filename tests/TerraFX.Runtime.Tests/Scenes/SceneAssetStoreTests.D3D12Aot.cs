// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [TestCase("interactive")]
    [TestCase("offscreen")]
    public void PublishedD3D12ConsumerExecutesScenario(string mode)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The D3D12 Interactive consumer witness targets Windows.");
            return;
        }

        var consumerPath = Environment.GetEnvironmentVariable("TERRAFX_D3D12_NATIVEAOT_CONSUMER");

        if (string.IsNullOrWhiteSpace(consumerPath))
        {
            Assert.Ignore("Set TERRAFX_D3D12_NATIVEAOT_CONSUMER to the JIT or NativeAOT D3D12 consumer.");
            return;
        }

        if (!Path.IsPathFullyQualified(consumerPath) || !File.Exists(consumerPath))
        {
            throw new InvalidOperationException("TERRAFX_D3D12_NATIVEAOT_CONSUMER must name an existing absolute file.");
        }

        var sceneBytes = SceneAssetReaderTests.Fixtures.CreateScene(
            new AssetId(3),
            additionalEntityComponents: [CreateCameraRecord(order: 0)]);
        var packageBytes = Package.CreatePackage(
            new Package.Entry(3, Package.CreateEnvelope(3, sceneBytes)));
        var packagePath = Package.WritePackage(packageBytes);

        try
        {
            var isManagedAssembly = StringComparer.OrdinalIgnoreCase.Equals(Path.GetExtension(consumerPath), ".dll");
            var startInfo = new ProcessStartInfo(isManagedAssembly ? "dotnet" : consumerPath) {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };

            if (isManagedAssembly)
            {
                startInfo.ArgumentList.Add(consumerPath);
            }

            startInfo.ArgumentList.Add(mode);

            if (mode is "offscreen")
            {
                startInfo.ArgumentList.Add("headless");
            }

            startInfo.ArgumentList.Add(packagePath);
            startInfo.ArgumentList.Add(SceneAssetReaderTests.Fixtures.RootEntityId.ToString("N"));

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The D3D12 Interactive consumer process did not start.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(milliseconds: 120_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException("The D3D12 Interactive consumer did not exit within two minutes.");
            }

            var output = Task.WhenAll(standardOutput, standardError).GetAwaiter().GetResult();
            var processOutput = $"{output[0]}{Environment.NewLine}{output[1]}";
            Assert.That(process.ExitCode, Is.Zero, processOutput);
            var completionMarker = mode switch {
                "interactive" => "D3D12 Interactive graph presented",
                "offscreen" => "D3D12 Offscreen graph readback passed",
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            Assert.That(processOutput, Does.Contain(completionMarker));
        }
        finally
        {
            File.Delete(packagePath);
        }
    }
}
