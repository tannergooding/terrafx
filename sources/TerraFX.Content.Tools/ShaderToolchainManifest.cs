// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using TerraFX.Graphics;

namespace TerraFX.Content.Tools;

internal sealed class ShaderToolchainManifest
{
    private readonly ImmutableArray<ShaderToolBinary> _binaries;
    private readonly ImmutableArray<ShaderToolLicense> _licenses;

    private ShaderToolchainManifest(
        ShaderCompilerIdentity compilerIdentity,
        string dxcExecutable,
        string spirvCrossExecutable,
        string spirvCrossArgument,
        string spirvValidationExecutable,
        string vulkanSdkVersion,
        TimeSpan processTimeout,
        int maximumCapturedOutputBytes,
        ImmutableArray<ShaderToolBinary> binaries,
        ImmutableArray<ShaderToolLicense> licenses)
    {
        CompilerIdentity = compilerIdentity;
        DxcExecutable = dxcExecutable;
        SpirvCrossExecutable = spirvCrossExecutable;
        SpirvCrossArgument = spirvCrossArgument;
        SpirvValidationExecutable = spirvValidationExecutable;
        VulkanSdkVersion = vulkanSdkVersion;
        ProcessTimeout = processTimeout;
        MaximumCapturedOutputBytes = maximumCapturedOutputBytes;
        _binaries = binaries;
        _licenses = licenses;
    }

    /// <summary>Gets the pinned compiler identity.</summary>
    internal ShaderCompilerIdentity CompilerIdentity { get; }

    /// <summary>Gets the executable name copied beside Content.Tools.</summary>
    internal string DxcExecutable { get; }

    /// <summary>Gets the maximum combined captured stdout/stderr byte count.</summary>
    internal int MaximumCapturedOutputBytes { get; }

    /// <summary>Gets the timeout applied to each external tool process.</summary>
    internal TimeSpan ProcessTimeout { get; }

    /// <summary>Gets the SPIRV-Cross reflection argument.</summary>
    internal string SpirvCrossArgument { get; }

    /// <summary>Gets the SPIRV-Cross executable name in the selected Vulkan SDK.</summary>
    internal string SpirvCrossExecutable { get; }

    /// <summary>Gets the SPIR-V validator executable name in the selected Vulkan SDK.</summary>
    internal string SpirvValidationExecutable { get; }

    /// <summary>Gets the required Vulkan SDK version.</summary>
    internal string VulkanSdkVersion { get; }

    internal static ShaderToolchainManifest Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions {
            MaxDepth = 16,
        });
        var root = document.RootElement;

        if (GetRequiredInt32(root, "schemaVersion") != 1)
        {
            throw new InvalidDataException("The shader toolchain manifest has an unsupported schema version.");
        }

        var process = GetRequiredObject(root, "process");
        var timeoutSeconds = GetRequiredInt32(process, "timeoutSeconds");
        var maximumCapturedOutputBytes = GetRequiredInt32(process, "maximumCapturedOutputBytes");

        if ((timeoutSeconds <= 0) || (maximumCapturedOutputBytes <= 0))
        {
            throw new InvalidDataException("Shader tool process bounds must be positive.");
        }

        var dxc = GetRequiredObject(root, "dxCompiler");
        var packageId = GetRequiredString(dxc, "packageId");
        var releaseVersion = GetRequiredString(dxc, "releaseTag");
        var packageVersion = GetRequiredString(dxc, "packageVersion");
        var targetRid = GetRequiredString(dxc, "targetRid");
        var packageSha256 = ReadSha256(dxc, "packageSha256");
        var compilerIdentity = new ShaderCompilerIdentity(packageId, releaseVersion, packageVersion, targetRid, packageSha256);
        var windowsOnlyLicense = GetRequiredString(dxc, "windowsOnlyLicense");

        if (!StringComparer.Ordinal.Equals(packageId, "Microsoft.Direct3D.DXC")
            || !StringComparer.Ordinal.Equals(targetRid, "win-x64")
            || !StringComparer.Ordinal.Equals(windowsOnlyLicense, "LICENSE-MS.txt"))
        {
            throw new InvalidDataException("The selected DXC package must be the pinned Windows x64 artifact.");
        }

        var dxcExecutable = GetRequiredString(dxc, "executable");
        var binaries = ReadBinaries(dxc, dxcExecutable);
        var licenses = ReadLicenses(dxc);

        if (!licenses.Any(license => StringComparer.OrdinalIgnoreCase.Equals(license.File, windowsOnlyLicense)))
        {
            throw new InvalidDataException("The DXC manifest must preserve the Windows-only package license.");
        }

        var spirvCross = GetRequiredObject(root, "spirvCross");
        var spirvTools = GetRequiredObject(root, "spirvTools");
        var vulkanSdkVersion = GetRequiredString(spirvCross, "vulkanSdkVersion");

        if (!StringComparer.Ordinal.Equals(vulkanSdkVersion, GetRequiredString(spirvTools, "vulkanSdkVersion")))
        {
            throw new InvalidDataException("SPIRV-Cross and SPIRV-Tools must use the same pinned Vulkan SDK version.");
        }

        return new ShaderToolchainManifest(
            compilerIdentity,
            dxcExecutable,
            GetRequiredString(spirvCross, "executable"),
            GetRequiredString(spirvCross, "reflectionArgument"),
            GetRequiredString(spirvTools, "validationExecutable"),
            vulkanSdkVersion,
            TimeSpan.FromSeconds(timeoutSeconds),
            maximumCapturedOutputBytes,
            binaries,
            licenses);
    }

    private static void EnsureFileExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("A pinned Vulkan SDK shader tool is missing.", path);
        }
    }

    private static string GetContainedPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Shader toolchain paths must be relative.");
        }

        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var rootPrefix = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Shader toolchain paths must remain under their declared directory.");
        }

        return fullPath;
    }

    private static int GetRequiredInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || (value.ValueKind != JsonValueKind.Number))
        {
            throw new InvalidDataException($"Shader toolchain manifest property '{propertyName}' must be an integer.");
        }

        return value.GetInt32();
    }

    private static JsonElement GetRequiredObject(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || (value.ValueKind != JsonValueKind.Object))
        {
            throw new InvalidDataException($"Shader toolchain manifest property '{propertyName}' must be an object.");
        }

        return value;
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || (value.ValueKind != JsonValueKind.String))
        {
            throw new InvalidDataException($"Shader toolchain manifest property '{propertyName}' must be a nonempty string.");
        }

        var result = value.GetString();

        if (string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidDataException($"Shader toolchain manifest property '{propertyName}' must be a nonempty string.");
        }

        return result;
    }

    private static ImmutableArray<ShaderToolBinary> ReadBinaries(JsonElement dxc, string executable)
    {
        if (!dxc.TryGetProperty("binaries", out var binariesElement) || (binariesElement.ValueKind != JsonValueKind.Array))
        {
            throw new InvalidDataException("The shader toolchain manifest must declare DXC binaries.");
        }

        var binaries = ImmutableArray.CreateBuilder<ShaderToolBinary>(binariesElement.GetArrayLength());
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var binaryElement in binariesElement.EnumerateArray())
        {
            var outputPath = GetRequiredString(binaryElement, "outputPath");

            if (Path.IsPathRooted(outputPath) || !paths.Add(outputPath))
            {
                throw new InvalidDataException("DXC output paths must be unique and relative.");
            }

            binaries.Add(new ShaderToolBinary(outputPath, ReadSha256(binaryElement, "sha256")));
        }

        var immutableBinaries = binaries.MoveToImmutable();

        if (!immutableBinaries.Any(binary => StringComparer.OrdinalIgnoreCase.Equals(binary.OutputPath, executable)))
        {
            throw new InvalidDataException("The pinned DXC executable must appear in the binary digest list.");
        }

        return immutableBinaries;
    }

    private static ImmutableArray<ShaderToolLicense> ReadLicenses(JsonElement dxc)
    {
        if (!dxc.TryGetProperty("licenses", out var licensesElement) || (licensesElement.ValueKind != JsonValueKind.Array))
        {
            throw new InvalidDataException("The shader toolchain manifest must list DXC license files.");
        }

        var licenses = ImmutableArray.CreateBuilder<ShaderToolLicense>(licensesElement.GetArrayLength());
        var outputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var licenseElement in licensesElement.EnumerateArray())
        {
            var outputPath = GetRequiredString(licenseElement, "outputPath");

            if (Path.IsPathRooted(outputPath) || !outputPaths.Add(outputPath))
            {
                throw new InvalidDataException("DXC license output paths must be unique and relative.");
            }

            licenses.Add(new ShaderToolLicense(
                GetRequiredString(licenseElement, "file"),
                outputPath,
                GetRequiredString(licenseElement, "appliesTo")));
        }

        return licenses.MoveToImmutable();
    }

    private static ImmutableArray<byte> ReadSha256(JsonElement element, string propertyName)
    {
        var encoded = GetRequiredString(element, propertyName);

        try
        {
            var digest = Convert.FromHexString(encoded);

            if (digest.Length != 32)
            {
                throw new InvalidDataException($"Shader toolchain manifest property '{propertyName}' is not a 32-byte SHA-256 digest.");
            }

            return [.. digest];
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"Shader toolchain manifest property '{propertyName}' is not hexadecimal SHA-256.", exception);
        }
    }

    internal string ResolveDxcExecutable(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        var executablePath = GetContainedPath(applicationDirectory, DxcExecutable);

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("The pinned DXC executable is missing.", executablePath);
        }

        return executablePath;
    }

    internal (string SpirvCrossPath, string SpirvValidationPath) ResolveVulkanTools(string vulkanSdkRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vulkanSdkRoot);
        var fullRoot = Path.GetFullPath(vulkanSdkRoot);
        var actualVersion = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullRoot));

        if (!StringComparer.Ordinal.Equals(actualVersion, VulkanSdkVersion))
        {
            throw new InvalidDataException($"VULKAN_SDK must identify version {VulkanSdkVersion}; found '{actualVersion}'.");
        }

        var binDirectory = Path.Combine(fullRoot, "Bin");
        var spirvCrossPath = GetContainedPath(binDirectory, SpirvCrossExecutable);
        var spirvValidationPath = GetContainedPath(binDirectory, SpirvValidationExecutable);
        EnsureFileExists(spirvCrossPath);
        EnsureFileExists(spirvValidationPath);
        return (spirvCrossPath, spirvValidationPath);
    }

    internal void ValidateCompilerFiles(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);

        for (var index = 0; index < _binaries.Length; index++)
        {
            var binary = _binaries[index];
            var path = GetContainedPath(applicationDirectory, binary.OutputPath);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Pinned DXC package file '{binary.OutputPath}' is missing.", path);
            }

            using var stream = File.OpenRead(path);
            var actualSha256 = SHA256.HashData(stream);

            if (!actualSha256.AsSpan().SequenceEqual(binary.Sha256.AsSpan()))
            {
                throw new InvalidDataException($"Pinned DXC package file '{binary.OutputPath}' has an unexpected SHA-256 digest.");
            }
        }

        for (var index = 0; index < _licenses.Length; index++)
        {
            var license = _licenses[index];
            var path = GetContainedPath(applicationDirectory, license.OutputPath);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Pinned DXC license file '{license.OutputPath}' is missing.", path);
            }
        }
    }

    private readonly record struct ShaderToolBinary(string OutputPath, ImmutableArray<byte> Sha256);

    private readonly record struct ShaderToolLicense(string File, string OutputPath, string AppliesTo);
}
