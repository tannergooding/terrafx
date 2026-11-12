// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.Content.Tools.Tests;

[TestFixture]
internal sealed class ShaderToolchainTests
{
    private static (string ExecutablePath, string[] Arguments) CreateShellCommand(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            var executablePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            return (executablePath, ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command]);
        }

        return ("/bin/sh", ["-c", command]);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), string.Concat("TerraFX.Content.Tools.Tests-", Guid.NewGuid().ToString("N")));
        _ = Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

        while (directory is not null)
        {
            var projectPath = Path.Combine(directory.FullName, "tests", "TerraFX.Content.Tools.Tests", "TerraFX.Content.Tools.Tests.csproj");

            if (File.Exists(projectPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The TerraFX.Content.Tools.Tests repository root could not be located.");
    }

    private static string GetArtifactPath(string fileName)
        => Path.Combine(TestContext.CurrentContext.TestDirectory, "ShaderArtifacts", "ObjectGpuData", fileName);

    private static string GetDxcPath()
        => typeof(ShaderToolchainTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static attribute => string.Equals(attribute.Key, "TerraFXDxcPath", StringComparison.Ordinal))
            .Value
            ?? throw new InvalidDataException("The test assembly does not identify its DXC path.");

    private static string GetVulkanSdkPath()
        => typeof(ShaderToolchainTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(static attribute => string.Equals(attribute.Key, "TerraFXVulkanSdkPath", StringComparison.Ordinal))
            .Value
            ?? throw new InvalidDataException("The test assembly does not identify its Vulkan SDK path.");

    [Test]
    public void BoundedToolProcessRejectsOutputAboveAggregateLimit()
    {
        var command = OperatingSystem.IsWindows()
            ? "[Console]::Out.Write('x' * 9000000)"
            : "while :; do printf 0123456789; done";
        var (executablePath, arguments) = CreateShellCommand(command);

        _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
            await BoundedToolProcess.RunAsync(
                executablePath,
                arguments,
                TestContext.CurrentContext.TestDirectory,
                TimeSpan.FromSeconds(10),
                CancellationToken.None).ConfigureAwait(false));
    }

    [Test]
    public void BoundedToolProcessStopsTimedOutProcess()
    {
        var command = OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 10" : "sleep 10";
        var (executablePath, arguments) = CreateShellCommand(command);

        _ = Assert.ThrowsAsync<TimeoutException>(async () =>
            await BoundedToolProcess.RunAsync(
                executablePath,
                arguments,
                TestContext.CurrentContext.TestDirectory,
                TimeSpan.FromMilliseconds(200),
                CancellationToken.None).ConfigureAwait(false));
    }

    [Test]
    public void CompiledFixtureMatchesBothBackendLayouts()
    {
        var dxilReflection = File.ReadAllText(GetArtifactPath("ObjectGpuData.dxil.reflection.txt"));
        var expectedNames = new[] { "World", "PreviousWorld", "NormalRow0", "NormalRow1", "NormalRow2", "Metadata" };
        var expectedOffsets = new[] { 0, 64, 128, 144, 160, 176 };

        Assert.That(dxilReflection, Does.Contain("SV_VertexID"));
        Assert.That(dxilReflection, Does.Contain("SV_Position"));
        Assert.That(dxilReflection, Does.Contain("row_major float4x4 World"));
        Assert.That(dxilReflection, Does.Contain("row_major float4x4 PreviousWorld"));

        for (var index = 0; index < expectedNames.Length; index++)
        {
            var expression = string.Concat(@"\b", Regex.Escape(expectedNames[index]), @";\s*;\s*Offset:\s*(\d+)");
            var match = Regex.Match(dxilReflection, expression, RegexOptions.CultureInvariant);
            Assert.That(match.Success, Is.True, $"DXIL reflection did not include {expectedNames[index]}.");
            Assert.That(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(expectedOffsets[index]));
        }

        using var spirvReflection = JsonDocument.Parse(File.ReadAllText(GetArtifactPath("ObjectGpuData.spirv.reflection.json")));
        var reflectedType = spirvReflection.RootElement
            .GetProperty("types")
            .EnumerateObject()
            .Select(static property => property.Value)
            .Single(static type => string.Equals(
                type.GetProperty("name").GetString(),
                "type.ObjectGpuData",
                StringComparison.Ordinal));
        var members = reflectedType.GetProperty("members").EnumerateArray().ToArray();

        Assert.That(
            members.Select(static member => member.GetProperty("name").GetString()).ToArray(),
            Is.EqualTo(expectedNames));
        Assert.That(
            members.Select(static member => member.GetProperty("offset").GetInt32()).ToArray(),
            Is.EqualTo(expectedOffsets));
        Assert.That(members[0].GetProperty("matrix_stride").GetInt32(), Is.EqualTo(16));
        Assert.That(members[1].GetProperty("matrix_stride").GetInt32(), Is.EqualTo(16));
        Assert.That(members[0].GetProperty("row_major").GetBoolean(), Is.True);
        Assert.That(members[1].GetProperty("row_major").GetBoolean(), Is.True);
        Assert.That(
            spirvReflection.RootElement.GetProperty("ubos")[0].GetProperty("block_size").GetInt32(),
            Is.EqualTo(192));
        Assert.That(spirvReflection.RootElement.GetProperty("ubos")[0].GetProperty("set").GetInt32(), Is.EqualTo(2));
        Assert.That(spirvReflection.RootElement.GetProperty("ubos")[0].GetProperty("binding").GetInt32(), Is.Zero);
        Assert.That(
            spirvReflection.RootElement.GetProperty("entryPoints")[0].GetProperty("name").GetString(),
            Is.EqualTo("VSMain"));
    }

    [Test]
    public async Task CompilerFailurePreservesPreviouslyUsableOutputs()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var sourcePath = Path.Combine(root, "ObjectGpuData.hlsl");
            var outputDirectory = Path.Combine(root, "compiled");
            var artifactNames = new[]
            {
                "ObjectGpuData.vertex.dxil",
                "ObjectGpuData.vertex.spv",
                "ObjectGpuData.dxil.reflection.txt",
                "ObjectGpuData.spirv.reflection.json",
                "ObjectGpuData.compile.sha256"
            };
            _ = Directory.CreateDirectory(outputDirectory);

            foreach (var artifactName in artifactNames)
            {
                File.Copy(GetArtifactPath(artifactName), Path.Combine(outputDirectory, artifactName));
            }

            await File.WriteAllTextAsync(sourcePath, "#error intentional shader compilation failure\n").ConfigureAwait(false);
            var request = new ShaderCompileRequest(
                sourcePath,
                outputDirectory,
                "VSMain",
                "vertex",
                GetDxcPath(),
                GetVulkanSdkPath(),
                [],
                [],
                debug: false,
                TimeSpan.FromSeconds(30));

            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await ShaderCompiler.CompileAsync(request, CancellationToken.None).ConfigureAwait(false));

            foreach (var artifactName in artifactNames)
            {
                Assert.That(
                    await File.ReadAllBytesAsync(Path.Combine(outputDirectory, artifactName)).ConfigureAwait(false),
                    Is.EqualTo(await File.ReadAllBytesAsync(GetArtifactPath(artifactName)).ConfigureAwait(false)));
            }

            Assert.That(Directory.EnumerateDirectories(root, ".compile-*").Any(), Is.False);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ShaderBuildTargetSkipsUpToDateOutputs()
    {
        var repositoryRoot = FindRepositoryRoot();
        var testDirectory = TestContext.CurrentContext.TestDirectory;
        var configuration = new DirectoryInfo(testDirectory).Parent?.Name
            ?? throw new DirectoryNotFoundException("The test configuration directory could not be identified.");
        var targetFramework = new DirectoryInfo(testDirectory).Name;
        var projectPath = Path.Combine(repositoryRoot, "tests", "TerraFX.Content.Tools.Tests", "TerraFX.Content.Tools.Tests.csproj");
        var shaderPath = Path.Combine(
            repositoryRoot,
            "artifacts",
            "obj",
            "tests",
            "TerraFX.Content.Tools.Tests",
            configuration,
            targetFramework,
            "TerraFXShaders",
            "ObjectGpuData",
            "ObjectGpuData.vertex.dxil");
        var initialWriteTime = File.GetLastWriteTimeUtc(shaderPath);
        var arguments = new[]
        {
            "msbuild",
            projectPath,
            "-target:TerraFXCompileShaders",
            string.Concat("-property:Configuration=", configuration),
            string.Concat("-property:TargetFramework=", targetFramework),
            string.Concat("-property:TerraFXVulkanSdkPath=", GetVulkanSdkPath()),
            "-verbosity:minimal"
        };
        var dotnetPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var result = await BoundedToolProcess.RunAsync(
            dotnetPath,
            arguments,
            repositoryRoot,
            TimeSpan.FromMinutes(2),
            CancellationToken.None).ConfigureAwait(false);

        Assert.That(result.ExitCode, Is.Zero, string.Concat(result.StandardOutput, result.StandardError));
        Assert.That(File.GetLastWriteTimeUtc(shaderPath), Is.EqualTo(initialWriteTime));
    }

    [TestCase("9999999999999999")]
    [TestCase("0.00000000001")]
    public void ShaderCompileCommandRejectsUnrepresentableTimeout(string timeout)
    {
        string[] arguments =
        [
            "--source", "unused.hlsl",
            "--output", "unused",
            "--entry", "Main",
            "--stage", "vertex",
            "--dxc", "unused-dxc",
            "--vulkan-sdk", "unused-sdk",
            "--timeout-seconds", timeout
        ];

        _ = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await ShaderCompileCommand.RunAsync(arguments, CancellationToken.None).ConfigureAwait(false));
    }

    [Test]
    public async Task ShaderCompilerFingerprintTracksIncludesAndNormalizesDefineOrder()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var sourcePath = Path.Combine(root, "main.hlsl");
            var includePath = Path.Combine(root, "shared.hlsli");
            var outputDirectory = Path.Combine(root, "compiled");
            await File.WriteAllTextAsync(
                sourcePath,
                "#include \"shared.hlsli\"\nfloat4 VSMain(uint id : SV_VertexID) : SV_Position { return float4((float)id, SHARED_VALUE + TERRAFX_BETA, 0.0f, 1.0f); }\n").ConfigureAwait(false);
            await File.WriteAllTextAsync(includePath, "#define SHARED_VALUE 1.0f\n").ConfigureAwait(false);

            var firstRequest = new ShaderCompileRequest(
                sourcePath,
                outputDirectory,
                "VSMain",
                "vertex",
                GetDxcPath(),
                GetVulkanSdkPath(),
                [],
                ["TERRAFX_ALPHA=1", "TERRAFX_BETA=2"],
                debug: false,
                TimeSpan.FromSeconds(30));
            var firstResult = await ShaderCompiler.CompileAsync(firstRequest, CancellationToken.None).ConfigureAwait(false);
            var reorderedRequest = new ShaderCompileRequest(
                sourcePath,
                outputDirectory,
                "VSMain",
                "vertex",
                GetDxcPath(),
                GetVulkanSdkPath(),
                [],
                ["TERRAFX_BETA=2", "TERRAFX_ALPHA=1"],
                debug: false,
                TimeSpan.FromSeconds(30));
            var reorderedResult = await ShaderCompiler.CompileAsync(reorderedRequest, CancellationToken.None).ConfigureAwait(false);

            Assert.That(reorderedResult.Fingerprint, Is.EqualTo(firstResult.Fingerprint));

            await File.WriteAllTextAsync(includePath, "#define SHARED_VALUE 2.0f\n").ConfigureAwait(false);
            var changedResult = await ShaderCompiler.CompileAsync(firstRequest, CancellationToken.None).ConfigureAwait(false);

            Assert.That(changedResult.Fingerprint, Is.Not.EqualTo(firstResult.Fingerprint));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ShaderCompilerProducesVertexPixelAndComputeProfiles()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(repositoryRoot, "tests", "TerraFX.Content.Tools.Tests", "Shaders", "AllStages.hlsl");
        var outputRoot = CreateTemporaryDirectory();
        (string entryPoint, string stage, string mode)[] cases =
        [
            ("VSMain", "vertex", "vert"),
            ("PSMain", "pixel", "frag"),
            ("CSMain", "compute", "comp")
        ];

        try
        {
            foreach (var (entryPointName, stageName, mode) in cases)
            {
                var outputDirectory = Path.Combine(outputRoot, stageName);
                var request = new ShaderCompileRequest(
                    sourcePath,
                    outputDirectory,
                    entryPointName,
                    stageName,
                    GetDxcPath(),
                    GetVulkanSdkPath(),
                    [],
                    [],
                    debug: false,
                    TimeSpan.FromSeconds(30));
                var result = await ShaderCompiler.CompileAsync(request, CancellationToken.None).ConfigureAwait(false);

                Assert.That(new FileInfo(result.DxilPath).Length, Is.GreaterThan(0));
                Assert.That(new FileInfo(result.SpirvPath).Length, Is.GreaterThan(0));

                using var reflection = JsonDocument.Parse(await File.ReadAllTextAsync(result.SpirvReflectionPath).ConfigureAwait(false));
                var entryPoint = reflection.RootElement.GetProperty("entryPoints")[0];
                Assert.That(entryPoint.GetProperty("name").GetString(), Is.EqualTo(entryPointName));
                Assert.That(entryPoint.GetProperty("mode").GetString(), Is.EqualTo(mode));

                if (string.Equals(stageName, "compute", StringComparison.Ordinal))
                {
                    int[] expectedWorkgroupSize = [8, 4, 1];
                    Assert.That(
                        entryPoint.GetProperty("workgroup_size").EnumerateArray().Select(static size => size.GetInt32()).ToArray(),
                        Is.EqualTo(expectedWorkgroupSize));
                }
            }
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Test]
    public void ShaderCompilerRejectsUnsupportedStageBeforeToolStartup()
    {
        var request = new ShaderCompileRequest(
            "unused.hlsl",
            "unused",
            "Main",
            "geometry",
            "unused-dxc",
            "unused-sdk",
            [],
            [],
            debug: false,
            TimeSpan.FromSeconds(1));

        _ = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await ShaderCompiler.CompileAsync(request, CancellationToken.None).ConfigureAwait(false));
    }

    [Test]
    public async Task ShaderCompilerRejectsWorkgroupAboveBaselineProfile()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(repositoryRoot, "tests", "TerraFX.Content.Tools.Tests", "Shaders", "AllStages.hlsl");
        var outputRoot = CreateTemporaryDirectory();
        var outputDirectory = Path.Combine(outputRoot, "compute");

        try
        {
            var request = new ShaderCompileRequest(
                sourcePath,
                outputDirectory,
                "OversizedCSMain",
                "compute",
                GetDxcPath(),
                GetVulkanSdkPath(),
                [],
                [],
                debug: false,
                TimeSpan.FromSeconds(30));

            _ = Assert.ThrowsAsync<InvalidDataException>(async () =>
                await ShaderCompiler.CompileAsync(request, CancellationToken.None).ConfigureAwait(false));

            Assert.That(Directory.Exists(outputDirectory), Is.False);
            Assert.That(Directory.EnumerateDirectories(outputRoot, ".compile-*").Any(), Is.False);
        }
        finally
        {
            Directory.Delete(outputRoot, recursive: true);
        }
    }

    [Test]
    public void ShaderSourceClosureRejectsAggregateSourceSizeOverLimit()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var sourcePath = Path.Combine(root, "main.hlsl");
            var includePath = Path.Combine(root, "include.hlsli");
            var halfSize = ShaderSourceClosure.MaximumSourceBytes / 2;
            File.WriteAllText(sourcePath, string.Concat("#include \"include.hlsli\"\n", new string('a', halfSize)));
            File.WriteAllText(includePath, new string('b', halfSize));

            _ = Assert.Throws<InvalidDataException>(() => ShaderSourceClosure.Load(sourcePath, []));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ShaderSourceClosureRejectsIncludeCycles()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var sourcePath = Path.Combine(root, "main.hlsl");
            var includePath = Path.Combine(root, "include.hlsli");
            File.WriteAllText(sourcePath, "#include \"include.hlsli\"\n");
            File.WriteAllText(includePath, "#include \"main.hlsl\"\n");

            _ = Assert.Throws<InvalidDataException>(() => ShaderSourceClosure.Load(sourcePath, []));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ShaderSourceClosureRejectsIncludeDepthOverLimit()
    {
        var root = CreateTemporaryDirectory();

        try
        {
            var sourcePath = Path.Combine(root, "main.hlsl");
            File.WriteAllText(sourcePath, "#include \"include-0.hlsli\"\n");

            for (var index = 0; index <= ShaderSourceClosure.MaximumIncludeDepth; index++)
            {
                var includePath = Path.Combine(root, string.Concat("include-", index, ".hlsli"));
                var contents = index == ShaderSourceClosure.MaximumIncludeDepth
                    ? "float4 Main() : SV_Position { return 0; }\n"
                    : string.Concat("#include \"include-", index + 1, ".hlsli\"\n");
                File.WriteAllText(includePath, contents);
            }

            _ = Assert.Throws<InvalidDataException>(() => ShaderSourceClosure.Load(sourcePath, []));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
