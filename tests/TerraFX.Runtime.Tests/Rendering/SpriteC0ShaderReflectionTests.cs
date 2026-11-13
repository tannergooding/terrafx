// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using TerraFX.Content.Tools;
using TerraFX.Graphics;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class SpriteC0ShaderReflectionTests
{
    private static void AssertEquivalent(ShaderReflection expected, ShaderReflection actual)
    {
        Assert.Multiple(() => {
            Assert.That(actual.Stage, Is.EqualTo(expected.Stage));
            Assert.That(actual.EntryPoint, Is.EqualTo(expected.EntryPoint));
            Assert.That(actual.CompilerIdentity, Is.EqualTo(expected.CompilerIdentity));
            Assert.That(actual.Inputs, Is.EqualTo(expected.Inputs));
            Assert.That(actual.Outputs, Is.EqualTo(expected.Outputs));
            Assert.That(actual.Resources, Is.EqualTo(expected.Resources));
            Assert.That(actual.RequiredCapabilities, Is.EqualTo(expected.RequiredCapabilities));
            Assert.That(actual.RecordLayouts.Length, Is.EqualTo(expected.RecordLayouts.Length));

            for (var index = 0; index < expected.RecordLayouts.Length; index++)
            {
                Assert.That(actual.RecordLayouts[index].Name, Is.EqualTo(expected.RecordLayouts[index].Name));
                Assert.That(actual.RecordLayouts[index].ByteLength, Is.EqualTo(expected.RecordLayouts[index].ByteLength));
                Assert.That(actual.RecordLayouts[index].Fields, Is.EqualTo(expected.RecordLayouts[index].Fields));
            }
        });
    }

    [Test]
    public void CompiledShaderArtifactsMatchTheFixedSpriteC0Reflection()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("The pinned Windows-only DXC toolchain produces the Sprite C0 reflection artifacts.");
            return;
        }

        var shaderDirectory = Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "ShaderArtifacts",
            "ObjectGpuDataPipeline");
        var manifest = ShaderToolchainManifest.Load(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "ShaderToolchainManifest.json"));
        var vertex = M04ShaderReflectionReader.Read(
            File.ReadAllText(Path.Combine(shaderDirectory, "SpriteC0Vertex.dxil.reflection.txt")),
            File.ReadAllText(Path.Combine(shaderDirectory, "SpriteC0Vertex.spirv.reflection.json")),
            File.ReadAllBytes(Path.Combine(shaderDirectory, "SpriteC0Vertex.vertex.spv")),
            manifest.CompilerIdentity);
        var pixel = M04ShaderReflectionReader.Read(
            File.ReadAllText(Path.Combine(shaderDirectory, "SpriteC0Pixel.dxil.reflection.txt")),
            File.ReadAllText(Path.Combine(shaderDirectory, "SpriteC0Pixel.spirv.reflection.json")),
            File.ReadAllBytes(Path.Combine(shaderDirectory, "SpriteC0Pixel.pixel.spv")),
            manifest.CompilerIdentity);

        AssertEquivalent(SpriteC0ShaderReflection.Vertex, vertex);
        AssertEquivalent(SpriteC0ShaderReflection.Pixel, pixel);
    }

    [Test]
    public void RuntimeAssemblyContainsShaderBytecodeForItsBackend()
    {
        string[] resources = OperatingSystem.IsWindows()
            ? [
                "TerraFX.Rendering.Shaders.SpriteC0Vertex.vertex.dxil",
                "TerraFX.Rendering.Shaders.SpriteC0Pixel.pixel.dxil",
                "TerraFX.Rendering.Shaders.SpriteC0Vertex.vertex.spv",
                "TerraFX.Rendering.Shaders.SpriteC0Pixel.pixel.spv",
            ]
            : [
                "TerraFX.Rendering.Shaders.SpriteC0Vertex.vertex.spv",
                "TerraFX.Rendering.Shaders.SpriteC0Pixel.pixel.spv",
            ];
        var assembly = typeof(Renderer).Assembly;

        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource);
            Assert.That(stream, Is.Not.Null, resource);
            Assert.That(stream!.Length, Is.GreaterThan(0), resource);
        }
    }
}
