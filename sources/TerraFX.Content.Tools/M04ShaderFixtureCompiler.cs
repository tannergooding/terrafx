// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Content.Tools;

internal static class M04ShaderFixtureCompiler
{
    private const string ShaderEntryPointPixel = "PSMain";
    private const string ShaderEntryPointVertex = "VSMain";
    private const string ShaderFileName = "M04OffscreenTriangle.hlsl";
    private static readonly string[] s_versionArguments = ["--version"];

    internal static async Task CompileAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        EnsureSupportedPlatform();

        var applicationDirectory = AppContext.BaseDirectory;
        var manifest = LoadManifest(applicationDirectory);
        manifest.ValidateCompilerFiles(applicationDirectory);

        var dxcExecutable = manifest.ResolveDxcExecutable(applicationDirectory);
        await VerifyCompilerVersionAsync(manifest, dxcExecutable, cancellationToken).ConfigureAwait(false);

        var vulkanSdkRoot = Environment.GetEnvironmentVariable("VULKAN_SDK");

        if (string.IsNullOrWhiteSpace(vulkanSdkRoot))
        {
            throw new FileNotFoundException($"Set VULKAN_SDK to Vulkan SDK {manifest.VulkanSdkVersion} to validate SPIR-V reflection and binaries.");
        }

        var (spirvCrossExecutable, spirvValidationExecutable) = manifest.ResolveVulkanTools(vulkanSdkRoot);
        var shaderPath = Path.Combine(applicationDirectory, "Shaders", ShaderFileName);

        if (!File.Exists(shaderPath))
        {
            throw new FileNotFoundException("The selected M04 shader fixture is missing.", shaderPath);
        }

        var finalOutputDirectory = Path.GetFullPath(outputDirectory);

        if (Directory.Exists(finalOutputDirectory) || File.Exists(finalOutputDirectory))
        {
            throw new IOException("The shader fixture output directory must not already exist.");
        }

        var parentDirectory = Path.GetDirectoryName(finalOutputDirectory);

        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new ArgumentException("The shader fixture output directory must have a parent directory.", nameof(outputDirectory));
        }

        _ = Directory.CreateDirectory(parentDirectory);

        var stagingDirectory = Path.Combine(parentDirectory, $".{Path.GetFileName(finalOutputDirectory)}-{Guid.NewGuid():N}.tmp");
        _ = Directory.CreateDirectory(stagingDirectory);

        try
        {
            await CompileStageAsync(
                manifest,
                dxcExecutable,
                spirvCrossExecutable,
                spirvValidationExecutable,
                shaderPath,
                stagingDirectory,
                ShaderEntryPointVertex,
                "vs_6_0",
                "vertex",
                cancellationToken).ConfigureAwait(false);
            await CompileStageAsync(
                manifest,
                dxcExecutable,
                spirvCrossExecutable,
                spirvValidationExecutable,
                shaderPath,
                stagingDirectory,
                ShaderEntryPointPixel,
                "ps_6_0",
                "pixel",
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stagingDirectory, finalOutputDirectory);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    internal static async Task CompileDxilAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        EnsureSupportedPlatform();

        var applicationDirectory = AppContext.BaseDirectory;
        var manifest = LoadManifest(applicationDirectory);
        manifest.ValidateCompilerFiles(applicationDirectory);

        var dxcExecutable = manifest.ResolveDxcExecutable(applicationDirectory);
        await VerifyCompilerVersionAsync(manifest, dxcExecutable, cancellationToken).ConfigureAwait(false);

        var shaderPath = Path.Combine(applicationDirectory, "Shaders", ShaderFileName);

        if (!File.Exists(shaderPath))
        {
            throw new FileNotFoundException("The selected M04 shader fixture is missing.", shaderPath);
        }

        var finalOutputDirectory = Path.GetFullPath(outputDirectory);

        if (Directory.Exists(finalOutputDirectory) || File.Exists(finalOutputDirectory))
        {
            throw new IOException("The DXIL fixture output directory must not already exist.");
        }

        var parentDirectory = Path.GetDirectoryName(finalOutputDirectory);

        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            throw new ArgumentException("The DXIL fixture output directory must have a parent directory.", nameof(outputDirectory));
        }

        _ = Directory.CreateDirectory(parentDirectory);

        var stagingDirectory = Path.Combine(parentDirectory, $".{Path.GetFileName(finalOutputDirectory)}-{Guid.NewGuid():N}.tmp");
        _ = Directory.CreateDirectory(stagingDirectory);

        try
        {
            await CompileDxilStageAsync(
                manifest,
                dxcExecutable,
                shaderPath,
                stagingDirectory,
                ShaderEntryPointVertex,
                "vs_6_0",
                "vertex",
                cancellationToken).ConfigureAwait(false);
            await CompileDxilStageAsync(
                manifest,
                dxcExecutable,
                shaderPath,
                stagingDirectory,
                ShaderEntryPointPixel,
                "ps_6_0",
                "pixel",
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stagingDirectory, finalOutputDirectory);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    internal static async Task VerifyDxcAsync(CancellationToken cancellationToken)
    {
        EnsureSupportedPlatform();

        var applicationDirectory = AppContext.BaseDirectory;
        var manifest = LoadManifest(applicationDirectory);
        manifest.ValidateCompilerFiles(applicationDirectory);

        var executablePath = manifest.ResolveDxcExecutable(applicationDirectory);
        await VerifyCompilerVersionAsync(manifest, executablePath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CompileDxilStageAsync(
        ShaderToolchainManifest manifest,
        string dxcExecutable,
        string shaderPath,
        string outputDirectory,
        string entryPoint,
        string targetProfile,
        string stageName,
        CancellationToken cancellationToken)
    {
        var dxilPath = Path.Combine(outputDirectory, $"M04OffscreenTriangle.{stageName}.dxil");
        var dxilReflectionPath = Path.Combine(outputDirectory, $"M04OffscreenTriangle.{stageName}.dxil-reflection.txt");

        await RunDxcAsync(
            manifest,
            dxcExecutable,
            shaderPath,
            dxilPath,
            entryPoint,
            targetProfile,
            useSpirv: false,
            cancellationToken).ConfigureAwait(false);
        EnsureOutputExists(dxilPath);

        var dxilReflectionArguments = new[] { "-dumpbin", dxilPath };
        var dxilReflection = await ToolProcessRunner.RunAsync(
            dxcExecutable,
            outputDirectory,
            dxilReflectionArguments,
            manifest.ProcessTimeout,
            manifest.MaximumCapturedOutputBytes,
            cancellationToken).ConfigureAwait(false);
        ValidateDxilReflection(dxilReflection.StandardOutput, entryPoint, stageName);
        await File.WriteAllTextAsync(dxilReflectionPath, dxilReflection.StandardOutput, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
        EnsureOutputExists(dxilReflectionPath);
    }

    private static async Task CompileStageAsync(
        ShaderToolchainManifest manifest,
        string dxcExecutable,
        string spirvCrossExecutable,
        string spirvValidationExecutable,
        string shaderPath,
        string outputDirectory,
        string entryPoint,
        string targetProfile,
        string stageName,
        CancellationToken cancellationToken)
    {
        var spirvPath = Path.Combine(outputDirectory, $"M04OffscreenTriangle.{stageName}.spv");
        var reflectionPath = Path.Combine(outputDirectory, $"M04OffscreenTriangle.{stageName}.reflection.json");

        await CompileDxilStageAsync(
            manifest,
            dxcExecutable,
            shaderPath,
            outputDirectory,
            entryPoint,
            targetProfile,
            stageName,
            cancellationToken).ConfigureAwait(false);

        await RunDxcAsync(
            manifest,
            dxcExecutable,
            shaderPath,
            spirvPath,
            entryPoint,
            targetProfile,
            useSpirv: true,
            cancellationToken).ConfigureAwait(false);
        EnsureOutputExists(spirvPath);

        var validationArguments = new[] { "--target-env", "vulkan1.3", spirvPath };
        _ = await ToolProcessRunner.RunAsync(
            spirvValidationExecutable,
            outputDirectory,
            validationArguments,
            manifest.ProcessTimeout,
            manifest.MaximumCapturedOutputBytes,
            cancellationToken).ConfigureAwait(false);

        var reflectionArguments = new[] { spirvPath, manifest.SpirvCrossArgument };
        var reflectionResult = await ToolProcessRunner.RunAsync(
            spirvCrossExecutable,
            outputDirectory,
            reflectionArguments,
            manifest.ProcessTimeout,
            manifest.MaximumCapturedOutputBytes,
            cancellationToken).ConfigureAwait(false);
        ValidateReflection(reflectionResult.StandardOutput, entryPoint, stageName);
        await File.WriteAllTextAsync(reflectionPath, reflectionResult.StandardOutput, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureOutputExists(string path)
    {
        var fileInfo = new FileInfo(path);

        if (!fileInfo.Exists || (fileInfo.Length <= 0))
        {
            throw new InvalidDataException($"The shader compiler did not produce a nonempty output file '{fileInfo.Name}'.");
        }
    }

    private static void EnsureSupportedPlatform()
    {
        if (!OperatingSystem.IsWindows() || (RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            throw new PlatformNotSupportedException("The pinned Microsoft DXC package is licensed for Windows x64 use only.");
        }
    }

    private static ShaderToolchainManifest LoadManifest(string applicationDirectory)
        => ShaderToolchainManifest.Load(Path.Combine(applicationDirectory, "ShaderToolchainManifest.json"));

    private static async Task RunDxcAsync(
        ShaderToolchainManifest manifest,
        string dxcExecutable,
        string shaderPath,
        string outputPath,
        string entryPoint,
        string targetProfile,
        bool useSpirv,
        CancellationToken cancellationToken)
    {
        var arguments = new System.Collections.Generic.List<string>(12);

        if (useSpirv)
        {
            arguments.Add("-spirv");
            arguments.Add("-fspv-reflect");
            arguments.Add("-fspv-target-env=vulkan1.3");
            arguments.Add("-fvk-use-gl-layout");
        }

        arguments.Add("-T");
        arguments.Add(targetProfile);
        arguments.Add("-E");
        arguments.Add(entryPoint);
        arguments.Add("-O3");
        arguments.Add("-Fo");
        arguments.Add(outputPath);
        arguments.Add(shaderPath);

        _ = await ToolProcessRunner.RunAsync(
            dxcExecutable,
            Path.GetDirectoryName(shaderPath) ?? AppContext.BaseDirectory,
            arguments.ToArray(),
            manifest.ProcessTimeout,
            manifest.MaximumCapturedOutputBytes,
            cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateDxilReflection(string text, string entryPoint, string stageName)
    {
        var expectedOutputSemantic = stageName switch {
            "vertex" => "SV_Position",
            "pixel" => "SV_Target",
            _ => throw new ArgumentOutOfRangeException(nameof(stageName)),
        };

        if (!text.Contains($"EntryFunctionName: {entryPoint}", StringComparison.Ordinal)
            || !text.Contains("Output signature:", StringComparison.Ordinal)
            || !text.Contains(expectedOutputSemantic, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"DXC reflection from the compiled DXIL does not contain the expected {stageName} entry-point output.");
        }
    }

    private static void ValidateReflection(string json, string entryPoint, string stageName)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("entryPoints", out var entryPoints)
            || (entryPoints.ValueKind != JsonValueKind.Array)
            || (entryPoints.GetArrayLength() != 1))
        {
            throw new InvalidDataException("SPIRV-Cross reflection must report exactly one entry point for the selected shader stage.");
        }

        var reflectedEntryPoint = entryPoints[0];

        if (!reflectedEntryPoint.TryGetProperty("name", out var nameElement)
            || (nameElement.ValueKind != JsonValueKind.String)
            || !StringComparer.Ordinal.Equals(nameElement.GetString(), entryPoint)
            || !reflectedEntryPoint.TryGetProperty("mode", out var modeElement)
            || (modeElement.ValueKind != JsonValueKind.String))
        {
            throw new InvalidDataException("SPIRV-Cross reflection does not identify the selected shader entry point and stage.");
        }

        var mode = modeElement.GetString();
        var expectedMode = stageName switch {
            "vertex" => mode is "vert" or "vertex",
            "pixel" => mode is "frag" or "fragment",
            _ => throw new ArgumentOutOfRangeException(nameof(stageName)),
        };

        if (!expectedMode)
        {
            throw new InvalidDataException($"SPIRV-Cross reported mode '{mode}' for the {stageName} shader stage.");
        }
    }

    private static async Task VerifyCompilerVersionAsync(
        ShaderToolchainManifest manifest,
        string executablePath,
        CancellationToken cancellationToken)
    {
        var result = await ToolProcessRunner.RunAsync(
            executablePath,
            AppContext.BaseDirectory,
            s_versionArguments,
            manifest.ProcessTimeout,
            manifest.MaximumCapturedOutputBytes,
            cancellationToken).ConfigureAwait(false);
        var expectedPackageVersion = manifest.CompilerIdentity.PackageVersion;
        var expectedReleaseVersion = manifest.CompilerIdentity.ReleaseVersion.TrimStart('v', 'V');

        if (!result.StandardOutput.Contains(expectedPackageVersion, StringComparison.Ordinal)
            && !result.StandardOutput.Contains(expectedReleaseVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The pinned DXC executable version does not match the shader toolchain manifest.");
        }
    }
}
