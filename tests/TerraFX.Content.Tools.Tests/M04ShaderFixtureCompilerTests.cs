// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Graphics;

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

    private static void AssertFrameDataRecord(ShaderReflection reflection)
    {
        Assert.That(reflection.RecordLayouts.Length, Is.EqualTo(1));
        var record = reflection.RecordLayouts[0];
        Assert.That(record.Name, Is.EqualTo("FrameData"));
        Assert.That(record.ByteLength, Is.EqualTo(16));
        Assert.That(record.Fields.Length, Is.EqualTo(1));
        Assert.That(
            record.Fields[0],
            Is.EqualTo(new ShaderFieldLayout(
                "color",
                ShaderValueType.Float4,
                byteOffset: 0,
                byteLength: 16,
                arrayCount: 1,
                arrayStride: 0)));
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

    private static void AssertTypedReflectionRejects(
        string dxilReflection,
        string spirvReflection,
        byte[] spirvBytecode,
        ShaderCompilerIdentity compilerIdentity)
    {
        Assert.That(
            () => M04ShaderReflectionReader.Read(
                dxilReflection,
                spirvReflection,
                spirvBytecode,
                compilerIdentity),
            Throws.TypeOf<InvalidDataException>());
    }

    private static ShaderReflection ReadSyntheticTypedReflection(
        string dxilReflection,
        string spirvReflection,
        params uint[] capabilities)
    {
        uint[] uniformBufferInstructions = [
            (3U << 16) | 22U, 1, 32,
            (4U << 16) | 23U, 2, 1, 4,
            (3U << 16) | 30U, 3, 2,
            (3U << 16) | 71U, 3, 2,
            (4U << 16) | 32U, 4, 2, 3,
            (4U << 16) | 59U, 4, 5, 2,
            (4U << 16) | 71U, 5, 34, 0,
            (4U << 16) | 71U, 5, 33, 0,
        ];
        var capabilityInstructionOffset = 5 + uniformBufferInstructions.Length;
        var spirvWords = new uint[capabilityInstructionOffset + (2 * capabilities.Length)];
        spirvWords[0] = 0x07230203;
        spirvWords[1] = 0x00010000;
        spirvWords[3] = 6;
        uniformBufferInstructions.CopyTo(spirvWords, 5);

        for (var index = 0; index < capabilities.Length; index++)
        {
            spirvWords[capabilityInstructionOffset + (2 * index)] = (2U << 16) | 17U;
            spirvWords[capabilityInstructionOffset + (2 * index) + 1] = capabilities[index];
        }

        var spirvBytecode = MemoryMarshal.AsBytes(spirvWords.AsSpan()).ToArray();
        var compilerIdentity = new ShaderCompilerIdentity(
            "TerraFX.Content.Tools.Tests",
            "test",
            "1.0.0",
            "win-x64",
            [.. new byte[32]]);

        return M04ShaderReflectionReader.Read(dxilReflection, spirvReflection, spirvBytecode, compilerIdentity);
    }

    private static string ReplaceDxilFieldOffset(string reflection, string fieldName, int offset)
    {
        var lines = reflection.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();

            if (!line.StartsWith(';', StringComparison.Ordinal))
            {
                continue;
            }

            var content = line[1..].Trim();
            var declarationEnd = content.IndexOf(';', StringComparison.Ordinal);
            var offsetMarker = content.IndexOf("Offset:", StringComparison.Ordinal);

            if ((declarationEnd < 0)
                || (offsetMarker < 0)
                || !content[..declarationEnd].Trim().EndsWith($" {fieldName}", StringComparison.Ordinal))
            {
                continue;
            }

            var valueStart = offsetMarker + "Offset:".Length;

            while ((valueStart < content.Length) && char.IsWhiteSpace(content[valueStart]))
            {
                valueStart++;
            }

            var valueEnd = valueStart;

            while ((valueEnd < content.Length) && (content[valueEnd] is >= '0' and <= '9'))
            {
                valueEnd++;
            }

            if (valueStart == valueEnd)
            {
                throw new InvalidDataException($"DXIL field '{fieldName}' omitted its offset value.");
            }

            lines[index] = $"; {content[..valueStart]}{offset}{content[valueEnd..]}";
            return string.Join('\n', lines);
        }

        throw new InvalidDataException($"DXIL reflection omitted the field '{fieldName}'.");
    }

    private static string ReplaceDxilResourceColumn(string reflection, string resourceName, int columnIndex, string replacement)
    {
        var lines = reflection.Split('\n');
        var prefix = $"; {resourceName}";

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();

            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var columns = line[1..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.That(columns.Length, Is.EqualTo(7));
            columns[columnIndex] = replacement;
            lines[index] = $"; {string.Join(' ', columns)}";
            return string.Join('\n', lines);
        }

        throw new InvalidDataException($"DXIL reflection omitted the resource binding for '{resourceName}'.");
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

            var typedVertexReflection = M04ShaderFixtureCompiler.ReadTypedReflection(outputDirectory, "vertex");
            var typedPixelReflection = M04ShaderFixtureCompiler.ReadTypedReflection(outputDirectory, "pixel");
            var typedSampledReflection = M04ShaderFixtureCompiler.ReadTypedReflection(outputDirectory, "pixel-sampled");
            var typedSampledFrameReflection = M04ShaderFixtureCompiler.ReadTypedReflection(outputDirectory, "pixel-sampled-frame");
            var typedStorageReflection = M04ShaderFixtureCompiler.ReadTypedReflection(outputDirectory, "pixel-sampled-frame-storage");

            Assert.That(typedVertexReflection.Stage, Is.EqualTo(ShaderStage.Vertex));
            Assert.That(typedVertexReflection.EntryPoint, Is.EqualTo("VSMain"));
            Assert.That(
                typedVertexReflection.Inputs,
                Is.EqualTo(ImmutableArray.Create(new ShaderIoSignature(
                    "SV_VertexID",
                    0,
                    location: null,
                    ShaderValueType.UInt1,
                    ShaderInterpolation.Flat))));
            Assert.That(
                typedVertexReflection.Outputs,
                Is.EqualTo(ImmutableArray.Create(new ShaderIoSignature(
                    "SV_Position",
                    0,
                    location: null,
                    ShaderValueType.Float4,
                    ShaderInterpolation.NoPerspective))));

            Assert.That(typedPixelReflection.Stage, Is.EqualTo(ShaderStage.Pixel));
            Assert.That(typedPixelReflection.EntryPoint, Is.EqualTo("PSMain"));
            Assert.That(typedPixelReflection.Resources, Does.Contain(
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.UniformBuffer, 1)));
            AssertFrameDataRecord(typedPixelReflection);

            Assert.That(typedSampledReflection.EntryPoint, Is.EqualTo("PSSampledMain"));
            Assert.That(typedSampledReflection.Resources, Does.Contain(
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.SampledImage, 1)));
            Assert.That(typedSampledReflection.Resources, Does.Contain(
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.Sampler, 1)));

            Assert.That(typedSampledFrameReflection.EntryPoint, Is.EqualTo("PSSampledFrameMain"));
            Assert.That(typedSampledFrameReflection.RecordLayouts.Length, Is.EqualTo(1));
            Assert.That(typedSampledFrameReflection.Resources.Length, Is.EqualTo(3));

            Assert.That(typedStorageReflection.EntryPoint, Is.EqualTo("PSSampledFrameStorageMain"));
            Assert.That(typedStorageReflection.Resources.Length, Is.EqualTo(5));
            Assert.That(typedStorageReflection.Resources, Does.Contain(
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.StorageBuffer, 1)));
            Assert.That(typedStorageReflection.Resources, Does.Contain(
                new ShaderResourceSignature(ShaderBindingGroup.Frame, 1, ShaderResourceKind.StorageImage, 1)));
            AssertFrameDataRecord(typedStorageReflection);

            var storageDxilReflection = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled-frame-storage.dxil-reflection.txt")).ConfigureAwait(false);
            var storageSpirvReflection = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled-frame-storage.reflection.json")).ConfigureAwait(false);
            var storageSpirvBytecode = await File.ReadAllBytesAsync(
                Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel-sampled-frame-storage.spv")).ConfigureAwait(false);

            AssertTypedReflectionRejects(
                ReplaceDxilResourceColumn(storageDxilReflection, "FrameTexture", 2, "u32"),
                storageSpirvReflection,
                storageSpirvBytecode,
                typedStorageReflection.CompilerIdentity);

            var mismatchedSpirvImageModeWords = MemoryMarshal.Cast<byte, uint>(storageSpirvBytecode).ToArray();
            var changedSpirvImageMode = false;

            for (var cursor = 5; cursor < mismatchedSpirvImageModeWords.Length;)
            {
                var instruction = mismatchedSpirvImageModeWords[cursor];
                var wordCount = checked((int)(instruction >> 16));
                var opcode = checked((ushort)(instruction & 0xFFFF));

                if ((opcode == 25) && (mismatchedSpirvImageModeWords[cursor + 7] == 1))
                {
                    mismatchedSpirvImageModeWords[cursor + 7] = 2;
                    mismatchedSpirvImageModeWords[cursor + 8] = 3;
                    changedSpirvImageMode = true;
                    break;
                }

                cursor += wordCount;
            }

            Assert.That(changedSpirvImageMode, Is.True);
            AssertTypedReflectionRejects(
                storageDxilReflection,
                storageSpirvReflection,
                MemoryMarshal.AsBytes(mismatchedSpirvImageModeWords.AsSpan()).ToArray(),
                typedStorageReflection.CompilerIdentity);

            var mismatchedSpirvStorageClassWords = MemoryMarshal.Cast<byte, uint>(storageSpirvBytecode).ToArray();
            var changedSpirvStorageClass = false;

            for (var cursor = 5; cursor < mismatchedSpirvStorageClassWords.Length;)
            {
                var instruction = mismatchedSpirvStorageClassWords[cursor];
                var wordCount = checked((int)(instruction >> 16));
                var opcode = checked((ushort)(instruction & 0xFFFF));

                if ((opcode == 59) && (mismatchedSpirvStorageClassWords[cursor + 3] == 12))
                {
                    mismatchedSpirvStorageClassWords[cursor + 3] = 2;
                    changedSpirvStorageClass = true;
                    break;
                }

                cursor += wordCount;
            }

            Assert.That(changedSpirvStorageClass, Is.True);
            AssertTypedReflectionRejects(
                storageDxilReflection,
                storageSpirvReflection,
                MemoryMarshal.AsBytes(mismatchedSpirvStorageClassWords.AsSpan()).ToArray(),
                typedStorageReflection.CompilerIdentity);

            var mismatchedStorageImageFormat = JsonNode.Parse(storageSpirvReflection) as JsonObject
                ?? throw new InvalidDataException("The storage SPIRV-Cross reflection root must be an object.");
            var mismatchedStorageImages = mismatchedStorageImageFormat["images"] as JsonArray
                ?? throw new InvalidDataException("The storage SPIRV-Cross image collection must be an array.");
            var storageImage = mismatchedStorageImages[0] as JsonObject
                ?? throw new InvalidDataException("The storage SPIRV-Cross image fixture must be an object.");
            storageImage["format"] = "r32f";
            AssertTypedReflectionRejects(
                storageDxilReflection,
                mismatchedStorageImageFormat.ToJsonString(),
                storageSpirvBytecode,
                typedStorageReflection.CompilerIdentity);

            var mismatchedStorageImageDimension = JsonNode.Parse(storageSpirvReflection) as JsonObject
                ?? throw new InvalidDataException("The storage SPIRV-Cross reflection root must be an object.");
            var dimensionMismatchImages = mismatchedStorageImageDimension["images"] as JsonArray
                ?? throw new InvalidDataException("The storage SPIRV-Cross image collection must be an array.");
            var dimensionMismatchImage = dimensionMismatchImages[0] as JsonObject
                ?? throw new InvalidDataException("The storage SPIRV-Cross image fixture must be an object.");
            dimensionMismatchImage["type"] = "uimage3D";
            AssertTypedReflectionRejects(
                storageDxilReflection,
                mismatchedStorageImageDimension.ToJsonString(),
                storageSpirvBytecode,
                typedStorageReflection.CompilerIdentity);

            var mismatchedSampledImageDimension = JsonNode.Parse(storageSpirvReflection) as JsonObject
                ?? throw new InvalidDataException("The sampled SPIRV-Cross reflection root must be an object.");
            var sampledImages = mismatchedSampledImageDimension["separate_images"] as JsonArray
                ?? throw new InvalidDataException("The sampled SPIRV-Cross image collection must be an array.");
            var sampledImage = sampledImages[0] as JsonObject
                ?? throw new InvalidDataException("The sampled SPIRV-Cross image fixture must be an object.");
            sampledImage["type"] = "texture3D";
            AssertTypedReflectionRejects(
                storageDxilReflection,
                mismatchedSampledImageDimension.ToJsonString(),
                storageSpirvBytecode,
                typedStorageReflection.CompilerIdentity);

            AssertTypedReflectionRejects(
                ReplaceDxilResourceColumn(storageDxilReflection, "FrameStorageBuffer", 3, "2d"),
                storageSpirvReflection,
                storageSpirvBytecode,
                typedStorageReflection.CompilerIdentity);

            var pixelDxilReflection = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel.dxil-reflection.txt")).ConfigureAwait(false);
            var pixelSpirvReflection = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel.reflection.json")).ConfigureAwait(false);
            var pixelSpirvBytecode = await File.ReadAllBytesAsync(
                Path.Combine(outputDirectory, "M04OffscreenTriangle.pixel.spv")).ConfigureAwait(false);
            var missingOutput = JsonNode.Parse(pixelSpirvReflection) as JsonObject
                ?? throw new InvalidDataException("The pixel SPIRV-Cross reflection root must be an object.");

            if (!missingOutput.Remove("outputs"))
            {
                throw new InvalidDataException("The pixel SPIRV-Cross reflection omitted its expected output fixture.");
            }

            AssertTypedReflectionRejects(
                pixelDxilReflection,
                missingOutput.ToJsonString(),
                pixelSpirvBytecode,
                typedPixelReflection.CompilerIdentity);

            var duplicateOutput = JsonNode.Parse(pixelSpirvReflection) as JsonObject
                ?? throw new InvalidDataException("The pixel SPIRV-Cross reflection root must be an object.");
            var outputSignatures = duplicateOutput["outputs"] as JsonArray
                ?? throw new InvalidDataException("The pixel SPIRV-Cross reflection outputs must be an array.");
            var firstOutput = outputSignatures[0]?.DeepClone()
                ?? throw new InvalidDataException("The pixel SPIRV-Cross reflection output fixture is missing.");
            outputSignatures.Add(firstOutput);
            AssertTypedReflectionRejects(
                pixelDxilReflection,
                duplicateOutput.ToJsonString(),
                pixelSpirvBytecode,
                typedPixelReflection.CompilerIdentity);
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

    [Test]
    public void ReadCrossChecksFixedDescriptorArrayCountAgainstSpirv()
    {
        uint[] spirvWords = [
            0x07230203,
            0x00010000,
            0,
            16,
            0,
            (3U << 16) | 22U, 1, 32,
            (4U << 16) | 23U, 2, 1, 4,
            (3U << 16) | 30U, 3, 2,
            (3U << 16) | 71U, 3, 2,
            (4U << 16) | 32U, 4, 2, 3,
            (4U << 16) | 59U, 4, 5, 2,
            (4U << 16) | 71U, 5, 34, 0,
            (4U << 16) | 71U, 5, 33, 0,
            (4U << 16) | 21U, 6, 32, 0,
            (4U << 16) | 43U, 6, 7, 2,
            (4U << 16) | 43U, 6, 12, 3,
            (9U << 16) | 25U, 8, 1, 1, 0, 0, 0, 1, 0,
            (4U << 16) | 28U, 9, 8, 7,
            (4U << 16) | 28U, 13, 9, 12,
            (4U << 16) | 32U, 14, 0, 13,
            (4U << 16) | 59U, 14, 15, 0,
            (4U << 16) | 71U, 15, 34, 0,
            (4U << 16) | 71U, 15, 33, 16,
        ];
        var spirvBytecode = MemoryMarshal.AsBytes(spirvWords.AsSpan()).ToArray();
        const string dxilReflection = """
            ; EntryFunctionName: PSMain
            ;
            ; Input signature:
            ;
            ; Name                 Index   Mask Register SysValue  Format   Used
            ; -------------------- ----- ------ -------- -------- ------- ------
            ;
            ; Output signature:
            ;
            ; Name                 Index   Mask Register SysValue  Format   Used
            ; -------------------- ----- ------ -------- -------- ------- ------
            ; SV_Target                0   xyzw        0   TARGET   float   xyzw
            ;
            ; Buffer Definitions:
            ;
            ; cbuffer FrameData
            ; {
            ;
            ;   struct FrameData
            ;   {
            ;
            ;       float4 color;                                 ; Offset:    0
            ;   } FrameData;                                      ; Offset:    0 Size:    16
            ;
            ; }
            ;
            ; Resource Bindings:
            ;
            ; Name                                 Type  Format         Dim      ID      HLSL Bind  Count
            ; ------------------------------ ---------- ------- ----------- ------- -------------- ------
            ; FrameData                         cbuffer      NA          NA     CB0            cb0     1
            ; FrameTexture                      texture     f32          2d      T0             t0     6
            """;
        const string spirvReflection = """
            {
              "entryPoints": [{ "name": "PSMain", "mode": "frag" }],
              "inputs": [],
              "outputs": [{ "name": "@entryPointOutput", "type": "vec4", "location": 0 }],
              "ubos": [{ "type": "_3", "name": "type.FrameData", "block_size": 16, "set": 0, "binding": 0 }],
              "separate_images": [{
                "type": "texture2D",
                "name": "FrameTexture",
                "set": 0,
                "binding": 16,
                "array": [2, 3],
                "array_size_is_literal": [true, true]
              }],
              "types": {
                "_3": {
                  "name": "type.FrameData",
                  "members": [{ "name": "color", "type": "vec4", "offset": 0 }]
                }
              }
            }
            """;
        var compilerIdentity = new ShaderCompilerIdentity(
            "TerraFX.Content.Tools.Tests",
            "test",
            "1.0.0",
            "win-x64",
            [.. new byte[32]]);
        var reflection = M04ShaderReflectionReader.Read(
            dxilReflection,
            spirvReflection,
            spirvBytecode,
            compilerIdentity);

        Assert.That(reflection.Resources, Does.Contain(
            new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.SampledImage, 6)));

        var mismatchedSpirvCross = JsonNode.Parse(spirvReflection) as JsonObject
            ?? throw new InvalidDataException("The sampled SPIRV-Cross reflection root must be an object.");
        var sampledImages = mismatchedSpirvCross["separate_images"] as JsonArray
            ?? throw new InvalidDataException("The sampled SPIRV-Cross image collection must be an array.");
        var sampledImage = sampledImages[0] as JsonObject
            ?? throw new InvalidDataException("The sampled SPIRV-Cross image fixture must be an object.");
        sampledImage["array"] = new JsonArray(JsonValue.Create(1), JsonValue.Create(7));
        var mismatchedDxilReflection = ReplaceDxilResourceColumn(dxilReflection, "FrameTexture", 6, "7");

        Assert.That(
            () => M04ShaderReflectionReader.Read(
                mismatchedDxilReflection,
                mismatchedSpirvCross.ToJsonString(),
                spirvBytecode,
                compilerIdentity),
            Throws.TypeOf<InvalidDataException>());

        const int firstArrayLengthConstantWordIndex = 41;
        const int secondArrayLengthConstantWordIndex = 45;
        var overflowingSpirvWords = (uint[])spirvWords.Clone();
        overflowingSpirvWords[firstArrayLengthConstantWordIndex] = 46_341;
        overflowingSpirvWords[secondArrayLengthConstantWordIndex] = 46_341;
        var overflowingSpirvBytecode = MemoryMarshal.AsBytes(overflowingSpirvWords.AsSpan()).ToArray();

        Assert.That(
            () => M04ShaderReflectionReader.Read(
                dxilReflection,
                spirvReflection,
                overflowingSpirvBytecode,
                compilerIdentity),
            Throws.TypeOf<InvalidDataException>());

        var overflowingSpirvCross = JsonNode.Parse(spirvReflection) as JsonObject
            ?? throw new InvalidDataException("The sampled SPIRV-Cross reflection root must be an object.");
        var overflowingSampledImages = overflowingSpirvCross["separate_images"] as JsonArray
            ?? throw new InvalidDataException("The sampled SPIRV-Cross image collection must be an array.");
        var overflowingSampledImage = overflowingSampledImages[0] as JsonObject
            ?? throw new InvalidDataException("The sampled SPIRV-Cross image fixture must be an object.");
        overflowingSampledImage["array"] = new JsonArray(JsonValue.Create(46_341), JsonValue.Create(46_341));

        Assert.That(
            () => M04ShaderReflectionReader.Read(
                dxilReflection,
                overflowingSpirvCross.ToJsonString(),
                spirvBytecode,
                compilerIdentity),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ReadValidatesUniformRecordFieldOffsetsArraysAndMatrixMajorAcrossBackends()
    {
        const string dxilReflection = """
            ; EntryFunctionName: PSMain
            ;
            ; Input signature:
            ;
            ; Name                 Index   Mask Register SysValue  Format   Used
            ; -------------------- ----- ------ -------- -------- ------- ------
            ;
            ; Output signature:
            ;
            ; Name                 Index   Mask Register SysValue  Format   Used
            ; -------------------- ----- ------ -------- -------- ------- ------
            ; SV_Target                0   xyzw        0   TARGET   float   xyzw
            ;
            ; Buffer Definitions:
            ;
            ; cbuffer FrameData
            ; {
            ;
            ;   struct FrameData
            ;   {
            ;
            ;       float4 color;                                 ; Offset:    0
            ;       float4 tint;                                  ; Offset:   16
            ;       row_major float4x4 transform;                 ; Offset:   32
            ;       float3 samples[2];                            ; Offset:   96
            ;   } FrameData;                                      ; Offset:    0 Size:   128
            ;
            ; }
            ;
            ; Resource Bindings:
            ;
            ; Name                                 Type  Format         Dim      ID      HLSL Bind  Count
            ; ------------------------------ ---------- ------- ----------- ------- -------------- ------
            ; FrameData                         cbuffer      NA          NA     CB0            cb0     1
            """;
        const string spirvReflection = """
            {
              "entryPoints": [{ "name": "PSMain", "mode": "frag" }],
              "inputs": [],
              "outputs": [{ "name": "@entryPointOutput", "type": "vec4", "location": 0 }],
              "ubos": [{ "type": "_1", "name": "type.FrameData", "block_size": 128, "set": 0, "binding": 0 }],
              "types": {
                "_1": {
                  "name": "type.FrameData",
                  "members": [
                    { "name": "color", "type": "vec4", "offset": 0 },
                    { "name": "tint", "type": "vec4", "offset": 16 },
                    { "name": "transform", "type": "mat4", "offset": 32, "matrix_stride": 16, "row_major": true },
                    { "name": "samples", "type": "vec3", "array": [2], "array_size_is_literal": [true], "offset": 96, "array_stride": 16 }
                  ]
                }
              }
            }
            """;

        var reflection = ReadSyntheticTypedReflection(
            dxilReflection,
            spirvReflection,
            2,
            3,
            9,
            10,
            28,
            31,
            61,
            64,
            5302,
            5303,
            5305,
            5306,
            5312);

        Assert.That(reflection.RecordLayouts.Length, Is.EqualTo(1));
        Assert.That(reflection.RecordLayouts[0].Name, Is.EqualTo("FrameData"));
        Assert.That(reflection.RecordLayouts[0].ByteLength, Is.EqualTo(128));
        Assert.That(reflection.RecordLayouts[0].Fields.Length, Is.EqualTo(4));
        Assert.That(reflection.RequiredCapabilities, Is.EqualTo([
            ShaderCapability.Float16,
            ShaderCapability.DoublePrecision,
            ShaderCapability.WaveOperations,
            ShaderCapability.DescriptorIndexing,
            ShaderCapability.GeometryShader,
            ShaderCapability.Tessellation,
        ]));
        Assert.That(
            reflection.RecordLayouts[0].Fields[2],
            Is.EqualTo(new ShaderFieldLayout(
                "transform",
                ShaderValueType.Float4x4,
                byteOffset: 32,
                byteLength: 64,
                arrayCount: 1,
                arrayStride: 0)));
        Assert.That(
            reflection.RecordLayouts[0].Fields[3],
            Is.EqualTo(new ShaderFieldLayout(
                "samples",
                ShaderValueType.Float3,
                byteOffset: 96,
                byteLength: 12,
                arrayCount: 2,
                arrayStride: 16)));

        var mismatchedDxilOffsets = ReplaceDxilFieldOffset(dxilReflection, "color", 16);
        mismatchedDxilOffsets = ReplaceDxilFieldOffset(mismatchedDxilOffsets, "tint", 0);
        Assert.That(
            () => ReadSyntheticTypedReflection(mismatchedDxilOffsets, spirvReflection),
            Throws.TypeOf<InvalidDataException>());

        var mismatchedRecordBase = dxilReflection.Replace(
            "Offset:    0 Size:   128",
            "Offset:    4 Size:   128",
            StringComparison.Ordinal);
        Assert.That(
            () => ReadSyntheticTypedReflection(mismatchedRecordBase, spirvReflection),
            Throws.TypeOf<InvalidDataException>());

        var mismatchedMatrixMajor = JsonNode.Parse(spirvReflection) as JsonObject
            ?? throw new InvalidDataException("The synthetic SPIRV-Cross reflection root must be an object.");
        var members = mismatchedMatrixMajor["types"]?["_1"]?["members"] as JsonArray
            ?? throw new InvalidDataException("The synthetic SPIRV-Cross record members must be an array.");
        var matrix = members[2] as JsonObject
            ?? throw new InvalidDataException("The synthetic SPIRV-Cross matrix member must be an object.");
        matrix["row_major"] = false;
        Assert.That(
            () => ReadSyntheticTypedReflection(dxilReflection, mismatchedMatrixMajor.ToJsonString()),
            Throws.TypeOf<InvalidDataException>());

        var mismatchedArrayStride = JsonNode.Parse(spirvReflection) as JsonObject
            ?? throw new InvalidDataException("The synthetic SPIRV-Cross reflection root must be an object.");
        var arrayMembers = mismatchedArrayStride["types"]?["_1"]?["members"] as JsonArray
            ?? throw new InvalidDataException("The synthetic SPIRV-Cross record members must be an array.");
        var arrayField = arrayMembers[3] as JsonObject
            ?? throw new InvalidDataException("The synthetic SPIRV-Cross array member must be an object.");
        arrayField["array_stride"] = 32;
        Assert.That(
            () => ReadSyntheticTypedReflection(dxilReflection, mismatchedArrayStride.ToJsonString()),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(
            () => ReadSyntheticTypedReflection(dxilReflection, spirvReflection, 8),
            Throws.TypeOf<NotSupportedException>());
        Assert.That(
            () => ReadSyntheticTypedReflection(dxilReflection, spirvReflection, 5297),
            Throws.TypeOf<NotSupportedException>());
    }
}
