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
    private static void AssertDxilResourceBinding(
        string reflection,
        string resourceName,
        string expectedType,
        string expectedFormat,
        string expectedDimension,
        string expectedBinding)
    {
        foreach (var line in reflection.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith($"; {resourceName}", StringComparison.Ordinal))
            {
                continue;
            }

            var columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.That(columns.Length, Is.EqualTo(8));
            Assert.That(columns[1], Is.EqualTo(resourceName));
            Assert.That(columns[2], Is.EqualTo(expectedType));
            Assert.That(columns[3], Is.EqualTo(expectedFormat));
            Assert.That(columns[4], Is.EqualTo(expectedDimension));
            Assert.That(columns[6], Is.EqualTo(expectedBinding));
            return;
        }

        throw new InvalidDataException($"DXIL reflection omitted the resource binding for '{resourceName}'.");
    }

    private static void AssertReflectedEntryPoint(JsonDocument reflection, string name, string stage)
    {
        var entryPoints = reflection.RootElement.GetProperty("entryPoints");
        Assert.That(entryPoints.GetArrayLength(), Is.EqualTo(1));
        Assert.That(entryPoints[0].GetProperty("name").GetString(), Is.EqualTo(name));
        Assert.That(entryPoints[0].GetProperty("mode").GetString(), Is.EqualTo(stage));
    }

    private static void AssertReflectedFrameUniformBuffer(JsonDocument reflection)
    {
        var root = reflection.RootElement;
        var uniformBuffers = root.GetProperty("ubos");
        Assert.That(uniformBuffers.GetArrayLength(), Is.EqualTo(1));
        Assert.That(uniformBuffers[0].GetProperty("name").GetString(), Is.EqualTo("type.FrameData"));
        Assert.That(uniformBuffers[0].GetProperty("block_size").GetInt32(), Is.EqualTo(16));
        Assert.That(uniformBuffers[0].GetProperty("set").GetInt32(), Is.EqualTo(0));
        Assert.That(uniformBuffers[0].GetProperty("binding").GetInt32(), Is.EqualTo(0));

        var typeIdentifier = uniformBuffers[0].GetProperty("type").GetString()
            ?? throw new InvalidDataException("SPIRV-Cross omitted the FrameData type identifier.");
        var frameDataType = root.GetProperty("types").GetProperty(typeIdentifier);
        Assert.That(frameDataType.GetProperty("members").GetArrayLength(), Is.EqualTo(1));
        Assert.That(frameDataType.GetProperty("members")[0].GetProperty("name").GetString(), Is.EqualTo("color"));
        Assert.That(frameDataType.GetProperty("members")[0].GetProperty("type").GetString(), Is.EqualTo("vec4"));
        Assert.That(frameDataType.GetProperty("members")[0].GetProperty("offset").GetInt32(), Is.EqualTo(0));
    }

    private static void AssertReflectedResourceBinding(
        JsonElement root,
        string collectionName,
        string expectedName,
        int expectedBinding)
    {
        var resources = root.GetProperty(collectionName);
        Assert.That(resources.GetArrayLength(), Is.EqualTo(1));
        Assert.That(resources[0].GetProperty("name").GetString(), Is.EqualTo(expectedName));
        Assert.That(resources[0].GetProperty("set").GetInt32(), Is.EqualTo(0));
        Assert.That(resources[0].GetProperty("binding").GetInt32(), Is.EqualTo(expectedBinding));
    }

    private static void AssertReflectedSampledResources(JsonDocument reflection)
    {
        AssertReflectedResourceBinding(reflection.RootElement, "separate_images", "FrameTexture", 16);
        AssertReflectedResourceBinding(reflection.RootElement, "separate_samplers", "FrameSampler", 40);
    }

    private static void AssertReflectedStorageResources(JsonDocument reflection)
    {
        var root = reflection.RootElement;
        AssertReflectedResourceBinding(root, "ssbos", "FrameStorageBuffer", 32);
        AssertReflectedResourceBinding(root, "images", "FrameStorageImage", 33);
        Assert.That(root.GetProperty("images")[0].GetProperty("format").GetString(), Is.EqualTo("r32ui"));
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
            Assert.That(files.Length, Is.EqualTo(20));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.vertex.dxil")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel.dxil")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled.dxil")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled-frame.dxil")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled-frame-storage.dxil")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.vertex.spv")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel.spv")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled.spv")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled-frame.spv")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled-frame-storage.spv")).Length, Is.GreaterThan(0));

            using var vertexReflection = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(outputDirectory, "M04OffscreenTriangle.vertex.reflection.json")).ConfigureAwait(false));
            using var pixelReflection = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel.reflection.json")).ConfigureAwait(false));
            using var sampledPixelReflection = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled.reflection.json")).ConfigureAwait(false));
            using var sampledFramePixelReflection = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled-frame.reflection.json")).ConfigureAwait(false));
            using var sampledFrameStoragePixelReflection = JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled-frame-storage.reflection.json")).ConfigureAwait(false));
            AssertReflectedEntryPoint(vertexReflection, "VSMain", "vert");
            AssertReflectedEntryPoint(pixelReflection, "PSMain", "frag");
            AssertReflectedFrameUniformBuffer(pixelReflection);
            AssertReflectedEntryPoint(sampledPixelReflection, "PSSampledMain", "frag");
            AssertReflectedSampledResources(sampledPixelReflection);
            AssertReflectedEntryPoint(sampledFramePixelReflection, "PSSampledFrameMain", "frag");
            AssertReflectedFrameUniformBuffer(sampledFramePixelReflection);
            AssertReflectedSampledResources(sampledFramePixelReflection);
            AssertReflectedEntryPoint(sampledFrameStoragePixelReflection, "PSSampledFrameStorageMain", "frag");
            AssertReflectedFrameUniformBuffer(sampledFrameStoragePixelReflection);
            AssertReflectedSampledResources(sampledFrameStoragePixelReflection);
            AssertReflectedStorageResources(sampledFrameStoragePixelReflection);
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
            Assert.That(files.Length, Is.EqualTo(10));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.vertex.dxil")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel.dxil")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled.dxil")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled-frame.dxil")).Length, Is.GreaterThan(0));
            Assert.That(new FileInfo(Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled-frame-storage.dxil")).Length, Is.GreaterThan(0));

            var vertexReflection = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "M04OffscreenTriangle.vertex.dxil-reflection.txt")).ConfigureAwait(false);
            var pixelReflection = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel.dxil-reflection.txt")).ConfigureAwait(false);
            var sampledPixelReflection = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled.dxil-reflection.txt")).ConfigureAwait(false);
            var sampledFramePixelReflection = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled-frame.dxil-reflection.txt")).ConfigureAwait(false);
            var sampledFrameStoragePixelReflection = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled-frame-storage.dxil-reflection.txt")).ConfigureAwait(false);

            Assert.That(vertexReflection.Contains("EntryFunctionName: VSMain", StringComparison.Ordinal), Is.True);
            Assert.That(vertexReflection.Contains("SV_Position", StringComparison.Ordinal), Is.True);
            Assert.That(pixelReflection.Contains("EntryFunctionName: PSMain", StringComparison.Ordinal), Is.True);
            Assert.That(pixelReflection.Contains("SV_Target", StringComparison.Ordinal), Is.True);
            Assert.That(pixelReflection.Contains("cbuffer FrameData", StringComparison.Ordinal), Is.True);
            Assert.That(pixelReflection.Contains("float4 color", StringComparison.Ordinal), Is.True);
            Assert.That(pixelReflection.Contains("FrameData                         cbuffer", StringComparison.Ordinal), Is.True);
            Assert.That(sampledPixelReflection.Contains("EntryFunctionName: PSSampledMain", StringComparison.Ordinal), Is.True);
            Assert.That(sampledPixelReflection.Contains("FrameTexture", StringComparison.Ordinal), Is.True);
            Assert.That(sampledPixelReflection.Contains("FrameSampler", StringComparison.Ordinal), Is.True);
            Assert.That(sampledFramePixelReflection.Contains("EntryFunctionName: PSSampledFrameMain", StringComparison.Ordinal), Is.True);
            Assert.That(sampledFramePixelReflection.Contains("FrameTexture", StringComparison.Ordinal), Is.True);
            Assert.That(sampledFramePixelReflection.Contains("FrameSampler", StringComparison.Ordinal), Is.True);
            Assert.That(sampledFramePixelReflection.Contains("cbuffer FrameData", StringComparison.Ordinal), Is.True);
            Assert.That(sampledFrameStoragePixelReflection.Contains("EntryFunctionName: PSSampledFrameStorageMain", StringComparison.Ordinal), Is.True);
            Assert.That(sampledFrameStoragePixelReflection.Contains("FrameTexture", StringComparison.Ordinal), Is.True);
            Assert.That(sampledFrameStoragePixelReflection.Contains("FrameSampler", StringComparison.Ordinal), Is.True);
            Assert.That(sampledFrameStoragePixelReflection.Contains("FrameStorageBuffer", StringComparison.Ordinal), Is.True);
            Assert.That(sampledFrameStoragePixelReflection.Contains("FrameStorageImage", StringComparison.Ordinal), Is.True);
            Assert.That(sampledFrameStoragePixelReflection.Contains("cbuffer FrameData", StringComparison.Ordinal), Is.True);
            AssertDxilResourceBinding(sampledFrameStoragePixelReflection, "FrameData", "cbuffer", "NA", "NA", "cb0");
            AssertDxilResourceBinding(sampledFrameStoragePixelReflection, "FrameSampler", "sampler", "NA", "NA", "s0");
            AssertDxilResourceBinding(sampledFrameStoragePixelReflection, "FrameTexture", "texture", "f32", "2d", "t0");
            AssertDxilResourceBinding(sampledFrameStoragePixelReflection, "FrameStorageBuffer", "UAV", "struct", "r/w", "u0");
            AssertDxilResourceBinding(sampledFrameStoragePixelReflection, "FrameStorageImage", "UAV", "u32", "2d", "u1");
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
