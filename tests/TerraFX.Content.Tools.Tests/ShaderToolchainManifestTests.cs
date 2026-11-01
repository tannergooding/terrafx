// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;

namespace TerraFX.Content.Tools.Tests;

[TestFixture]
internal sealed class ShaderToolchainManifestTests
{
    [Test]
    public void LoadRejectsOmittedWindowsOnlyLicenseEntry()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "ShaderToolchainManifest.json");
        var contents = File.ReadAllText(sourcePath);
        var replacedContents = contents.Replace("\"file\": \"LICENSE-MS.txt\",", "\"file\": \"LICENSE-OMITTED.txt\",", StringComparison.Ordinal);

        Assert.That(replacedContents, Is.Not.EqualTo(contents));

        var temporaryPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(temporaryPath, replacedContents);

        try
        {
            _ = Assert.Throws<InvalidDataException>(() => ShaderToolchainManifest.Load(temporaryPath));
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    [Test]
    public void LoadReturnsPinnedShaderCompilerAndSdkVersions()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ShaderToolchainManifest.json");
        var manifest = ShaderToolchainManifest.Load(path);

        Assert.That(manifest.CompilerIdentity.PackageId, Is.EqualTo("Microsoft.Direct3D.DXC"));
        Assert.That(manifest.CompilerIdentity.ReleaseVersion, Is.EqualTo("v1.8.2505.1"));
        Assert.That(manifest.CompilerIdentity.PackageVersion, Is.EqualTo("1.8.2505.32"));
        Assert.That(manifest.CompilerIdentity.TargetRid, Is.EqualTo("win-x64"));
        Assert.That(manifest.CompilerIdentity.PackageSha256.Length, Is.EqualTo(32));
        Assert.That(manifest.VulkanSdkVersion, Is.EqualTo("1.4.328.1"));
        Assert.That(manifest.ProcessTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(manifest.MaximumCapturedOutputBytes, Is.EqualTo(4 * 1024 * 1024));
    }

    [Test]
    public void ResolveVulkanToolsRejectsAnUnpinnedSdkDirectory()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ShaderToolchainManifest.json");
        var manifest = ShaderToolchainManifest.Load(path);
        var unpinnedSdkRoot = Path.Combine(Path.GetTempPath(), "VulkanSDK", "1.4.328.0");

        _ = Assert.Throws<InvalidDataException>(() => manifest.ResolveVulkanTools(unpinnedSdkRoot));
    }
}
