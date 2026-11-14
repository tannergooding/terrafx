// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using TerraFX.Hosting;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class WorldPersistenceRecoveryReaderTests
{
    [Test]
    public void CandidateReadWorkspaceTreatsMissingAndOversizedFilesAsAbsent()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}");
        var targetPath = Path.Combine(directory, "world.state");
        var backupPath = string.Concat(targetPath, ".bak");

        try
        {
            _ = Directory.CreateDirectory(directory);
            File.WriteAllBytes(targetPath, new byte[128]);
            File.WriteAllBytes(backupPath, new byte[64]);

            var workspaceBytes = WorldPersistenceRecoveryReader.MeasureCandidateReadWorkspaceBytes(
                targetPath,
                backupPath,
                maximumSnapshotBytes: 1024);

            Assert.That(workspaceBytes, Is.EqualTo(128L + 64L + 128L + 65_536L + 4_096L));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public void CandidateReadWorkspaceUsesMeasuredCoexistingFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"terrafx-{Guid.NewGuid():N}");
        var targetPath = Path.Combine(directory, "world.state");
        var backupPath = string.Concat(targetPath, ".bak");

        try
        {
            _ = Directory.CreateDirectory(directory);
            File.WriteAllBytes(targetPath, new byte[32]);
            File.WriteAllBytes(backupPath, new byte[64]);

            var workspaceBytes = WorldPersistenceRecoveryReader.MeasureCandidateReadWorkspaceBytes(
                targetPath,
                backupPath,
                maximumSnapshotBytes: 48);

            Assert.That(workspaceBytes, Is.EqualTo(32L + 0L + 32L + 65_536L + 4_096L));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
