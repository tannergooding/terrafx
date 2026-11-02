// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.Content.Tools.Tests;

[TestFixture]
internal sealed class M04ShaderFixtureCompilerTests
{
    private static void AssertReflectedEntryPoint(JsonDocument reflection, string name, string stage)
    {
        var entryPoints = reflection.RootElement.GetProperty("entryPoints");
        Assert.That(entryPoints.GetArrayLength(), Is.EqualTo(1));
        Assert.That(entryPoints[0].GetProperty("name").GetString(), Is.EqualTo(name));
        Assert.That(entryPoints[0].GetProperty("mode").GetString(), Is.EqualTo(stage));
    }

    [Test]
    public async Task CompileAsyncProducesValidatedDxilSpirvAndReflectionWithPinnedSdk()
    {
        if (!OperatingSystem.IsWindows() || (RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            Assert.Ignore("The pinned DXC package is licensed for Windows x64 use only.");
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VULKAN_SDK")))
        {
            Assert.Ignore("The pinned Vulkan SDK is not selected through VULKAN_SDK.");
        }

        var outputDirectory = Path.Combine(Path.GetTempPath(), $"TerraFX.Content.Tools.Tests-{Guid.NewGuid():N}");

        try
        {
            await M04ShaderFixtureCompiler.CompileAsync(outputDirectory, CancellationToken.None).ConfigureAwait(false);

            var files = Directory.GetFiles(outputDirectory);
            Assert.That(files.Length, Is.EqualTo(8));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.vertex.dxil")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel.dxil")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.vertex.spv")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel.spv")).Length, Is.GreaterThan(0));

            using var vertexReflection = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(outputDirectory, "M04OffscreenTriangle.vertex.reflection.json")).ConfigureAwait(false));
            using var pixelReflection = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel.reflection.json")).ConfigureAwait(false));
            AssertReflectedEntryPoint(vertexReflection, "VSMain", "vert");
            AssertReflectedEntryPoint(pixelReflection, "PSMain", "frag");
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Test]
    public async Task CompileDxilAsyncProducesValidatedStageArtifactsWithoutVulkanSdk()
    {
        if (!OperatingSystem.IsWindows() || (RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            Assert.Ignore("The pinned DXC package is licensed for Windows x64 use only.");
        }

        var outputDirectory = Path.Combine(Path.GetTempPath(), $"TerraFX.Content.Tools.Tests-{Guid.NewGuid():N}");

        try
        {
            await M04ShaderFixtureCompiler.CompileDxilAsync(outputDirectory, CancellationToken.None).ConfigureAwait(false);

            var files = Directory.GetFiles(outputDirectory);
            Assert.That(files.Length, Is.EqualTo(4));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.vertex.dxil")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel.dxil")).Length, Is.GreaterThan(0));

            var vertexReflection = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "M04OffscreenTriangle.vertex.dxil-reflection.txt")).ConfigureAwait(false);
            var pixelReflection = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel.dxil-reflection.txt")).ConfigureAwait(false);

            Assert.That(vertexReflection.Contains("EntryFunctionName: VSMain", StringComparison.Ordinal), Is.True);
            Assert.That(vertexReflection.Contains("SV_Position", StringComparison.Ordinal), Is.True);
            Assert.That(pixelReflection.Contains("EntryFunctionName: PSMain", StringComparison.Ordinal), Is.True);
            Assert.That(pixelReflection.Contains("SV_Target", StringComparison.Ordinal), Is.True);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }
}
