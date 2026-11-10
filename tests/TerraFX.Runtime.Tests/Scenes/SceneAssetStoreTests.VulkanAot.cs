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
    [TestCase("interactive", "wayland")]
    [TestCase("interactive", "x11")]
    [TestCase("interactive", "win32")]
    [TestCase("offscreen", "headless")]
    public void PublishedVulkanConsumerExecutesScenario(string mode, string platform)
    {
        var supportedPlatform = platform switch {
            "win32" => OperatingSystem.IsWindows(),
            "wayland" or "x11" or "headless" => OperatingSystem.IsLinux(),
            _ => false,
        };

        if (!supportedPlatform)
        {
            Assert.Ignore($"The Vulkan {platform} consumer witness does not target this operating system.");
            return;
        }

        var consumerPath = Environment.GetEnvironmentVariable("TERRAFX_VULKAN_NATIVEAOT_CONSUMER");

        if (string.IsNullOrWhiteSpace(consumerPath))
        {
            Assert.Ignore("Set TERRAFX_VULKAN_NATIVEAOT_CONSUMER to the JIT or NativeAOT Vulkan consumer.");
            return;
        }

        if (!Path.IsPathFullyQualified(consumerPath) || !File.Exists(consumerPath))
        {
            throw new InvalidOperationException("TERRAFX_VULKAN_NATIVEAOT_CONSUMER must name an existing absolute file.");
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
            startInfo.ArgumentList.Add(platform);
            startInfo.ArgumentList.Add(packagePath);
            startInfo.ArgumentList.Add(SceneAssetReaderTests.Fixtures.RootEntityId.ToString("N"));

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The Vulkan Interactive consumer process did not start.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(milliseconds: 120_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException("The Vulkan Interactive consumer did not exit within two minutes.");
            }

            var output = Task.WhenAll(standardOutput, standardError).GetAwaiter().GetResult();
            var processOutput = $"{output[0]}{Environment.NewLine}{output[1]}";
            Assert.That(process.ExitCode, Is.Zero, processOutput);
            var completionMarker = mode switch {
                "interactive" => $"Vulkan {platform} Interactive graph presented",
                "offscreen" => "Vulkan offscreen graph readback passed",
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
