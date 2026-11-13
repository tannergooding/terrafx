// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Content.Tools;

internal static class ShaderCompiler
{
    private const string CoordinateProfile = "LeftHandedYUpForwardZ1";
    private const string GeometryProfile = "EngineGeometryBinary32Rows2";
    private const string ToolchainManifestResourceName = "TerraFX.Content.Tools.ShaderCompilerToolchain.json";

    internal static async Task<ShaderCompileResult> CompileAsync(ShaderCompileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.EntryPoint);
        ArgumentException.ThrowIfNullOrEmpty(request.OutputDirectory);
        ArgumentException.ThrowIfNullOrEmpty(request.SourcePath);
        ArgumentException.ThrowIfNullOrEmpty(request.Stage);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(request.Timeout, TimeSpan.Zero);
        _ = GetTargetProfile(request.Stage);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "The pinned win-x64 DXC package may run only on Windows; Linux Vulkan consumers must use Windows-cooked SPIR-V.");
        }

        using var manifest = LoadToolchainManifest();
        var toolchain = await ValidateToolchainAsync(request, manifest.RootElement, cancellationToken).ConfigureAwait(false);
        var closure = ShaderSourceClosure.Load(request.SourcePath, request.IncludeRoots);
        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        var outputParentDirectory = Path.GetDirectoryName(outputDirectory)
            ?? throw new ArgumentException("The shader output directory has no parent.", nameof(request));
        _ = Directory.CreateDirectory(outputParentDirectory);
        var stagingDirectory = Path.Combine(outputParentDirectory, string.Concat(".compile-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));
        _ = Directory.CreateDirectory(stagingDirectory);

        try
        {
            var sourceDirectory = Path.Combine(stagingDirectory, "source");
            closure.CopyTo(sourceDirectory);
            var mainSource = closure.Files[0];
            var mainSourcePath = Path.GetFullPath(Path.Combine(
                sourceDirectory,
                mainSource.RootIndex.ToString(CultureInfo.InvariantCulture),
                mainSource.RelativePath));
            var includeRoots = new string[closure.Roots.Count];

            for (var index = 0; index < includeRoots.Length; index++)
            {
                includeRoots[index] = Path.Combine(sourceDirectory, index.ToString(CultureInfo.InvariantCulture));
            }

            var extension = GetStageExtension(request.Stage);
            var dxcOutputPath = Path.Combine(stagingDirectory, string.Concat("shader.", extension, ".dxil"));
            var dxilReflectionPath = Path.Combine(stagingDirectory, "shader.dxil.reflection.txt");
            var spirvOutputPath = Path.Combine(stagingDirectory, string.Concat("shader.", extension, ".spv"));
            var dxilArguments = CreateDxcArguments(request, includeRoots, mainSourcePath, dxcOutputPath, dxilReflectionPath, spirv: false);
            var dxilResult = await BoundedToolProcess.RunAsync(
                toolchain.DxcPath,
                dxilArguments,
                sourceDirectory,
                request.Timeout,
                cancellationToken).ConfigureAwait(false);
            EnsureToolSucceeded("DXIL compilation", toolchain.DxcPath, dxilResult);
            EnsureNonemptyFile(dxcOutputPath, "DXIL");
            EnsureNonemptyFile(dxilReflectionPath, "DXIL reflection");

            var spirvArguments = CreateDxcArguments(request, includeRoots, mainSourcePath, spirvOutputPath, null, spirv: true);
            var spirvResult = await BoundedToolProcess.RunAsync(
                toolchain.DxcPath,
                spirvArguments,
                sourceDirectory,
                request.Timeout,
                cancellationToken).ConfigureAwait(false);
            EnsureToolSucceeded("SPIR-V compilation", toolchain.DxcPath, spirvResult);
            EnsureNonemptyFile(spirvOutputPath, "SPIR-V");

            var validationResult = await BoundedToolProcess.RunAsync(
                toolchain.SpirvValidatorPath,
                ["--target-env", "vulkan1.3", spirvOutputPath],
                sourceDirectory,
                request.Timeout,
                cancellationToken).ConfigureAwait(false);
            EnsureToolSucceeded("SPIR-V validation", toolchain.SpirvValidatorPath, validationResult);

            var reflectionResult = await BoundedToolProcess.RunAsync(
                toolchain.SpirvCrossPath,
                [spirvOutputPath, "--reflect"],
                sourceDirectory,
                request.Timeout,
                cancellationToken).ConfigureAwait(false);
            EnsureToolSucceeded("SPIR-V reflection", toolchain.SpirvCrossPath, reflectionResult);
            using var spirvReflection = JsonDocument.Parse(reflectionResult.StandardOutput);
            ValidateSpirvReflection(spirvReflection.RootElement, request);

            var fingerprint = ComputeFingerprint(
                closure,
                request,
                manifest.RootElement,
                toolchain.DxcDigest,
                toolchain.SpirvValidatorDigest,
                toolchain.SpirvCrossDigest);
            var stagedFingerprintPath = Path.Combine(stagingDirectory, "shader.compile.sha256");
            var stagedSpirvReflectionPath = Path.Combine(stagingDirectory, "shader.spirv.reflection.json");
            await File.WriteAllTextAsync(
                stagedSpirvReflectionPath,
                reflectionResult.StandardOutput,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                stagedFingerprintPath,
                fingerprint,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);

            var baseName = Path.GetFileNameWithoutExtension(request.SourcePath);
            var finalPaths = new ShaderCompileResult(
                fingerprint,
                CollectWarnings(dxilResult, spirvResult, validationResult, reflectionResult),
                Path.Combine(outputDirectory, string.Concat(baseName, ".", extension, ".dxil")),
                Path.Combine(outputDirectory, string.Concat(baseName, ".", extension, ".spv")),
                Path.Combine(outputDirectory, string.Concat(baseName, ".dxil.reflection.txt")),
                Path.Combine(outputDirectory, string.Concat(baseName, ".spirv.reflection.json")));
            File.Move(dxcOutputPath, Path.Combine(stagingDirectory, Path.GetFileName(finalPaths.DxilPath)));
            File.Move(spirvOutputPath, Path.Combine(stagingDirectory, Path.GetFileName(finalPaths.SpirvPath)));
            File.Move(dxilReflectionPath, Path.Combine(stagingDirectory, Path.GetFileName(finalPaths.DxilReflectionPath)));
            File.Move(stagedSpirvReflectionPath, Path.Combine(stagingDirectory, Path.GetFileName(finalPaths.SpirvReflectionPath)));
            File.Move(stagedFingerprintPath, Path.Combine(stagingDirectory, string.Concat(baseName, ".compile.sha256")));
            PublishOutputDirectory(stagingDirectory, outputDirectory);

            return finalPaths;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static void AddOutput(List<string> warnings, string tool, string output)
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            warnings.Add(string.Concat(tool, ": ", output));
        }
    }

    private static List<string> CollectWarnings(
        ToolProcessResult dxilResult,
        ToolProcessResult spirvResult,
        ToolProcessResult validationResult,
        ToolProcessResult reflectionResult)
    {
        var warnings = new List<string>();
        AddOutput(warnings, "DXIL", dxilResult.StandardOutput);
        AddOutput(warnings, "DXIL", dxilResult.StandardError);
        AddOutput(warnings, "SPIR-V", spirvResult.StandardOutput);
        AddOutput(warnings, "SPIR-V", spirvResult.StandardError);
        AddOutput(warnings, "SPIR-V validation", validationResult.StandardError);
        AddOutput(warnings, "SPIR-V reflection", reflectionResult.StandardError);
        return warnings;
    }

    private static string ComputeFingerprint(
        ShaderSourceClosure closure,
        ShaderCompileRequest request,
        JsonElement manifest,
        string dxcDigest,
        string spirvValidatorDigest,
        string spirvCrossDigest)
    {
        List<string> values = [
            closure.ComputeFingerprint(),
            request.EntryPoint,
            request.Stage,
            request.Debug ? "debug" : "release",
            GeometryProfile,
            CoordinateProfile,
            GetToolAssemblyDigest(),
            manifest.GetProperty("dxc").GetProperty("release").GetString() ?? string.Empty,
            manifest.GetProperty("vulkanSdk").GetProperty("version").GetString() ?? string.Empty,
            "vulkan1.3",
            "gl-layout",
            dxcDigest,
            spirvValidatorDigest,
            spirvCrossDigest,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.GetRawText())))
        ];
        values.AddRange(request.Defines.OrderBy(static value => value, StringComparer.Ordinal));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', values))));
    }

    private static List<string> CreateDxcArguments(
        ShaderCompileRequest request,
        IReadOnlyList<string> includeRoots,
        string sourcePath,
        string objectPath,
        string? dxilReflectionPath,
        bool spirv)
    {
        var arguments = new List<string> {
            "-E",
            request.EntryPoint,
            "-T",
            GetTargetProfile(request.Stage),
            "-HV",
            "2021",
            "-encoding",
            "utf8",
            request.Debug ? "-Od" : "-O3",
            "-Fo",
            objectPath
        };

        if (dxilReflectionPath is not null)
        {
            arguments.Add("-Fc");
            arguments.Add(dxilReflectionPath);
        }

        foreach (var includeRoot in includeRoots)
        {
            arguments.Add("-I");
            arguments.Add(includeRoot);
        }

        foreach (var define in request.Defines.OrderBy(static value => value, StringComparer.Ordinal))
        {
            arguments.Add("-D");
            arguments.Add(define);
        }

        if (spirv)
        {
            arguments.Add("-spirv");
            arguments.Add("-fspv-target-env=vulkan1.3");
            arguments.Add("-fvk-use-gl-layout");
            arguments.Add("-fspv-reflect");
            arguments.Add("-D");
            arguments.Add("TERRAFX_SPIRV=1");
        }

        arguments.Add(sourcePath);
        return arguments;
    }

    private static void EnsureNonemptyFile(string path, string kind)
    {
        if (!File.Exists(path) || (new FileInfo(path).Length == 0))
        {
            throw new InvalidDataException($"The shader compiler exited successfully without producing the required {kind} output.");
        }
    }

    private static void EnsureToolSucceeded(string operation, string executable, ToolProcessResult result)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException(
                $"{operation} failed with exit code {result.ExitCode} using '{executable}'.{Environment.NewLine}{result.StandardError}{result.StandardOutput}");
        }
    }

    private static string GetFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string GetStageExtension(string stage)
        => stage switch {
            "vertex" => "vertex",
            "pixel" => "pixel",
            "compute" => "compute",
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Expected vertex, pixel, or compute.")
        };

    private static string GetTargetProfile(string stage)
        => stage switch {
            "vertex" => "vs_6_0",
            "pixel" => "ps_6_0",
            "compute" => "cs_6_0",
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Expected vertex, pixel, or compute.")
        };

    private static string GetToolAssemblyDigest()
    {
        var assemblyName = Assembly.GetExecutingAssembly().GetName().Name
            ?? throw new InvalidDataException("The content-tool assembly name is unavailable for the compiler key.");
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, string.Concat(assemblyName, ".dll"));

        if (File.Exists(assemblyPath))
        {
            return GetFileSha256(assemblyPath);
        }

        var processPath = Environment.ProcessPath
            ?? throw new InvalidDataException("The content-tool executable path is unavailable for the compiler key.");
        return GetFileSha256(processPath);
    }

    private static JsonDocument LoadToolchainManifest()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ToolchainManifestResourceName)
            ?? throw new InvalidDataException($"The embedded shader toolchain manifest '{ToolchainManifestResourceName}' is missing.");
        return JsonDocument.Parse(stream);
    }

    private static void PublishOutputDirectory(string stagingDirectory, string outputDirectory)
    {
        var outputParentDirectory = Path.GetDirectoryName(outputDirectory)
            ?? throw new ArgumentException("The shader output directory has no parent.", nameof(outputDirectory));
        var backupDirectory = Path.Combine(outputParentDirectory, string.Concat(".previous-", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)));
        var hadPreviousOutput = Directory.Exists(outputDirectory);

        if (hadPreviousOutput)
        {
            Directory.Move(outputDirectory, backupDirectory);
        }

        try
        {
            Directory.Move(stagingDirectory, outputDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (hadPreviousOutput && !Directory.Exists(outputDirectory))
            {
                Directory.Move(backupDirectory, outputDirectory);
            }

            throw;
        }

        if (hadPreviousOutput)
        {
            Directory.Delete(backupDirectory, recursive: true);
        }
    }

    private static void ValidateSpirvReflection(JsonElement reflection, ShaderCompileRequest request)
    {
        var expectedMode = request.Stage switch {
            "vertex" => "vert",
            "pixel" => "frag",
            "compute" => "comp",
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Stage, "Expected vertex, pixel, or compute.")
        };
        var entryPoints = reflection.GetProperty("entryPoints").EnumerateArray()
            .Where(entryPoint => string.Equals(entryPoint.GetProperty("name").GetString(), request.EntryPoint, StringComparison.Ordinal))
            .ToArray();

        if (entryPoints.Length != 1)
        {
            throw new InvalidDataException($"SPIR-V reflection must contain exactly one '{request.EntryPoint}' entry point.");
        }

        var entryPoint = entryPoints[0];
        var mode = entryPoint.GetProperty("mode").GetString();

        if (!string.Equals(mode, expectedMode, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The reflected SPIR-V entry point mode '{mode}' does not match the requested {request.Stage} stage.");
        }

        if (request.Stage == "compute")
        {
            if (!entryPoint.TryGetProperty("workgroup_size", out var workgroupSizeElement)
                || (workgroupSizeElement.ValueKind != JsonValueKind.Array))
            {
                throw new InvalidDataException("The reflected compute entry point is missing its workgroup size.");
            }

            var workgroupSize = workgroupSizeElement.EnumerateArray()
                .Select(static component => component.GetInt32())
                .ToArray();

            if ((workgroupSize.Length != 3) || workgroupSize.Any(static component => component <= 0))
            {
                throw new InvalidDataException("The reflected compute workgroup size must contain three positive axis dimensions.");
            }

            var invocationCount = checked((long)workgroupSize[0] * workgroupSize[1] * workgroupSize[2]);

            if (invocationCount > 256)
            {
                throw new InvalidDataException("The reflected compute workgroup exceeds the baseline 256-invocation profile.");
            }
        }
    }

    private static async Task<ToolchainPaths> ValidateToolchainAsync(
        ShaderCompileRequest request,
        JsonElement manifest,
        CancellationToken cancellationToken)
    {
        var dxcManifest = manifest.GetProperty("dxc");
        var sdkManifest = manifest.GetProperty("vulkanSdk");
        var version = sdkManifest.GetProperty("version").GetString()
            ?? throw new InvalidDataException("The Vulkan SDK version is missing from the shader toolchain manifest.");
        var sdkRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.VulkanSdkPath));

        if (!string.Equals(Path.GetFileName(sdkRoot), version, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected Vulkan SDK {version}, but '{sdkRoot}' is not under the matching SDK directory.");
        }

        var spirvDirectory = Path.Combine(sdkRoot, "Bin");
        var spirvValidatorPath = Path.Combine(spirvDirectory, "spirv-val.exe");
        var spirvCrossPath = Path.Combine(spirvDirectory, "spirv-cross.exe");
        var dxcManifestPlatform = dxcManifest.GetProperty("windows");
        var licensePath = sdkManifest.GetProperty("licensePath").GetString();
        var sdkLicensePath = Path.Combine(
            sdkRoot,
            (licensePath
                ?? throw new InvalidDataException("The Vulkan SDK license path is missing from the shader toolchain manifest."))
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar));
        var dxcPath = Path.GetFullPath(request.DxcPath);
        VerifyFileSha256(dxcPath, dxcManifestPlatform.GetProperty("dxcSha256").GetString());
        var dxcDirectory = Path.GetDirectoryName(dxcPath)
            ?? throw new InvalidDataException("The DXC executable has no containing directory.");
        VerifyFileSha256(Path.Combine(dxcDirectory, "dxcompiler.dll"), dxcManifestPlatform.GetProperty("dxcompilerSha256").GetString());
        VerifyFileSha256(Path.Combine(dxcDirectory, "dxil.dll"), dxcManifestPlatform.GetProperty("dxilSha256").GetString());
        var sdkManifestPlatform = sdkManifest.GetProperty("windowsX64");
        VerifyFileSha256(spirvValidatorPath, sdkManifestPlatform.GetProperty("spirvValSha256").GetString());
        VerifyFileSha256(spirvCrossPath, sdkManifestPlatform.GetProperty("spirvCrossSha256").GetString());

        VerifyFileExists(spirvValidatorPath);
        VerifyFileExists(spirvCrossPath);
        VerifyFileExists(sdkLicensePath);
        var validatorDigest = GetFileSha256(spirvValidatorPath);
        var crossDigest = GetFileSha256(spirvCrossPath);
        var timeout = request.Timeout;
        var dxcVersionResult = await BoundedToolProcess.RunAsync(
            dxcPath,
            ["--version"],
            dxcDirectory,
            timeout,
            cancellationToken).ConfigureAwait(false);
        EnsureToolSucceeded("DXC version query", dxcPath, dxcVersionResult);
        var binaryVersion = dxcManifest.GetProperty("binaryVersion").GetString()
            ?? throw new InvalidDataException("The DXC binary version is missing from the shader toolchain manifest.");

        if (!string.Concat(dxcVersionResult.StandardOutput, dxcVersionResult.StandardError).Contains(binaryVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The DXC executable does not report the pinned version {binaryVersion}.");
        }

        var validatorResult = await BoundedToolProcess.RunAsync(
            spirvValidatorPath,
            ["--version"],
            spirvDirectory,
            timeout,
            cancellationToken).ConfigureAwait(false);
        EnsureToolSucceeded("SPIR-V validator version query", spirvValidatorPath, validatorResult);
        var validatorVersion = sdkManifest.GetProperty("spirvToolsReportedVersion").GetString()
            ?? throw new InvalidDataException("The SPIR-V toolchain version is missing from the shader toolchain manifest.");

        if (!string.Concat(validatorResult.StandardOutput, validatorResult.StandardError).Contains(validatorVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The SPIR-V validator does not report the pinned version {validatorVersion}.");
        }

        var crossResult = await BoundedToolProcess.RunAsync(
            spirvCrossPath,
            ["--revision"],
            spirvDirectory,
            timeout,
            cancellationToken).ConfigureAwait(false);
        EnsureToolSucceeded("SPIR-V reflection-tool version query", spirvCrossPath, crossResult);

        var crossRevision = sdkManifest.GetProperty("spirvCrossRevision").GetString()
            ?? throw new InvalidDataException("The SPIR-V reflection-tool revision is missing from the shader toolchain manifest.");

        if (!string.Concat(crossResult.StandardOutput, crossResult.StandardError).Contains(crossRevision, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The SPIR-V reflection tool does not report the pinned revision {crossRevision}.");
        }

        string[] dxcLibraries = ["dxcompiler.dll", "dxil.dll"];
        List<string> dxcHashes = [GetFileSha256(dxcPath)];

        foreach (var library in dxcLibraries)
        {
            dxcHashes.Add(GetFileSha256(Path.GetFullPath(Path.Combine(dxcDirectory, library))));
        }

        var dxcDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', dxcHashes))));
        return new ToolchainPaths(dxcPath, spirvValidatorPath, spirvCrossPath, dxcDigest, validatorDigest, crossDigest);
    }

    private static void VerifyFileExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The pinned shader tool was not found: '{path}'.", path);
        }
    }

    private static void VerifyFileSha256(string path, string? expectedHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(expectedHash);
        VerifyFileExists(path);
        var actualHash = GetFileSha256(path);

        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The shader tool binary hash does not match the pinned toolchain manifest: '{path}'.");
        }
    }

    private readonly record struct ToolchainPaths(
        string DxcPath,
        string SpirvValidatorPath,
        string SpirvCrossPath,
        string DxcDigest,
        string SpirvValidatorDigest,
        string SpirvCrossDigest);
}
