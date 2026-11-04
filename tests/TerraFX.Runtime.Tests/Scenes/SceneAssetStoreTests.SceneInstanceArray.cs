// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

internal sealed partial class SceneAssetStoreTests
{
    [Test]
    [SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "ActivateAsync detaches each expansion plan before returning; the awaited requests own them independently of PreparedScene.")]
    public async Task SceneInstanceArrayWriterOrdersActiveSceneRecordsByRfcIdentity()
    {
        const int sceneAssetIdValue = 3;
        var schemas = SceneAssetReaderTests.Fixtures.CreateRegistry();
        var scene = SceneAssetReaderTests.Fixtures.CreateScene(new AssetId(sceneAssetIdValue));
        var package = Package.CreatePackage(new Package.Entry(
            sceneAssetIdValue,
            Package.CreateEnvelope(sceneAssetIdValue, scene)));
        var packagePath = Package.WritePackage(package);

        try
        {
            var storeRegistration = SceneSupport.RegisterAssetCodecs(new AssetStoreRegistrationBuilder(schemas)).Build();
            var assets = await AssetStore.CreateAsync(
                new AssetStoreOptions(packagePath, SHA256.HashData(package), storeRegistration)).ConfigureAwait(false);

            try
            {
                var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
                var sceneSupport = SceneSupport.CreateRegistration();
                _ = worldRegistrationBuilder.RegisterSceneSupport(sceneSupport);
                using var world = World.Create(new WorldOptions(worldRegistrationBuilder.Build()));
                var loader = new SceneLoader(world, assets);

                using (var firstPrepared = loader
                    .PrepareAsync(new AssetId(sceneAssetIdValue), new SceneLoadOptions())
                    .GetAwaiter()
                    .GetResult())
                {
                    var activation = loader.ActivateAsync(firstPrepared);
                    world.PumpControlBoundary();
                    var receipt = await activation.ConfigureAwait(false);
                    Assert.That(receipt.Status, Is.EqualTo(SceneActivationStatus.Activated));
                }

                using (var secondPrepared = loader
                    .PrepareAsync(new AssetId(sceneAssetIdValue), new SceneLoadOptions())
                    .GetAwaiter()
                    .GetResult())
                {
                    var activation = loader.ActivateAsync(secondPrepared);
                    world.PumpControlBoundary();
                    var receipt = await activation.ConfigureAwait(false);
                    Assert.That(receipt.Status, Is.EqualTo(SceneActivationStatus.Activated));
                }

                _ = world.Step();
                using var lease = world.AcquirePersistenceCaptureLease();
                var registry = sceneSupport.GetParticipant(world) as SceneWorldRegistry
                    ?? throw new InvalidOperationException("The active Scene participant is not a Scene registry.");
                Assert.That(registry.GetPersistenceInstanceCount(lease), Is.EqualTo(2));
                var activeInstances = new SceneInstance[2];
                registry.CopyPersistenceInstances(lease, activeInstances);
                Assert.That(activeInstances[0].EntityCount, Is.EqualTo(1));
                Assert.That(activeInstances[1].EntityCount, Is.EqualTo(1));
                var firstInstanceIdBytes = activeInstances[0].SceneInstanceId.ToByteArray(bigEndian: true);
                var secondInstanceIdBytes = activeInstances[1].SceneInstanceId.ToByteArray(bigEndian: true);
                var comparison = firstInstanceIdBytes.AsSpan().SequenceCompareTo(secondInstanceIdBytes);
                Assert.That(comparison, Is.Not.Zero);
                var firstInputIndex = (comparison < 0) ? 1 : 0;
                var secondInputIndex = 1 - firstInputIndex;
                SceneInstance[] instances = [activeInstances[firstInputIndex], activeInstances[secondInputIndex]];
                byte[][] records = [
                    SceneInstanceRecordFixtureBuilder.Create(instances[0], lease),
                    SceneInstanceRecordFixtureBuilder.Create(instances[1], lease),
                ];
                var recordStorageBytes = checked(records[0].Length + records[1].Length);
                var recordStorage = new byte[recordStorageBytes];
                EbinPayloadSlice[] recordSlicesByInstance = [
                    new(0, records[0].Length),
                    new(records[0].Length, records[1].Length),
                ];
                records[0].CopyTo(recordStorage, 0);
                records[1].CopyTo(recordStorage, records[0].Length);
                var orderScratch = new int[instances.Length];
                var orderedRecords = new EbinPayloadSlice[instances.Length];
                Assert.That(
                    ScenePersistenceSceneInstanceArrayWriter.Measure(
                        instances,
                        recordSlicesByInstance,
                        recordStorage,
                        EbinReadLimits.Foundation,
                        orderScratch,
                        orderedRecords,
                        out var workspace,
                        out var diagnostic),
                    Is.EqualTo(EbinWriteStatus.Complete),
                    diagnostic?.Category);
                Assert.That(diagnostic, Is.Null);
                int[] expectedOrder = [1, 0];
                Assert.That(orderScratch, Is.EqualTo(expectedOrder));
                Assert.That(orderedRecords[0], Is.EqualTo(recordSlicesByInstance[1]));
                Assert.That(orderedRecords[1], Is.EqualTo(recordSlicesByInstance[0]));
                var arrayPayload = new byte[workspace.ArrayPayloadBytes];
                Assert.That(
                    EbinPayloadWriter.WriteRecordArray(
                        orderedRecords,
                        recordStorage,
                        EbinReadLimits.Foundation,
                        arrayPayload,
                        out var expectedArrayBytesWritten,
                        out diagnostic),
                    Is.EqualTo(EbinWriteStatus.Complete),
                    diagnostic?.Category);
                Assert.That(diagnostic, Is.Null);
                Assert.That(expectedArrayBytesWritten, Is.EqualTo(arrayPayload.Length));
                Guid[] expectedSceneInstanceIds = [
                    instances[orderScratch[0]].SceneInstanceId,
                    instances[orderScratch[1]].SceneInstanceId,
                ];
                var hierarchyRecord = SceneInstanceRecordFixtureBuilder.CreateWorldHierarchyRecord(world, lease);
                Assert.That(
                    ScenePersistenceCaptureCoordinator.Capture(
                        world,
                        lease,
                        out var productionScenesRecord,
                        out var productionScenesReservation,
                        out diagnostic,
                        CancellationToken.None),
                    Is.EqualTo(EbinWriteStatus.Complete),
                    diagnostic?.Category);
                Assert.That(diagnostic, Is.Null);
                var capturedScenesRecord = productionScenesRecord
                    ?? throw new InvalidOperationException("The production Scene capture did not produce its record.");
                using (productionScenesReservation
                    ?? throw new InvalidOperationException("The production Scene capture did not retain its record reservation."))
                {
                    var productionScenesAssertions = new ScenePersistenceScenesRecordAssertions(
                        arrayPayload,
                        expectedSceneInstanceIds,
                        hierarchyRecord);
                    Assert.That(
                        EbinStructuralReader.Read(
                            capturedScenesRecord,
                            EbinReadLimits.Foundation,
                            EbinReadPolicy.Canonical,
                            ref productionScenesAssertions,
                            out var productionScenesBytesConsumed,
                            out var productionScenesReadDiagnostic),
                        Is.EqualTo(EbinReadStatus.Complete));
                    Assert.That(productionScenesReadDiagnostic, Is.Null);
                    Assert.That(productionScenesAssertions.IsMatch, Is.True);
                    Assert.That(productionScenesBytesConsumed, Is.EqualTo(capturedScenesRecord.Length));
                }

                var stagingRecordBytes = checked(records[0].Length + records[1].Length);
                Assert.That(
                    lease.TryReserveAdmissionBytes(checked(workspace.WorkspaceBytes + stagingRecordBytes), out var reservation),
                    Is.True);
                Assert.That(reservation, Is.Not.Null);

                using (reservation)
                {
                    Assert.That(
                        ScenePersistenceSceneInstanceArrayWriter.Write(
                            instances,
                            recordSlicesByInstance,
                            recordStorage,
                            EbinReadLimits.Foundation,
                            workspace,
                            orderScratch,
                            orderedRecords,
                            arrayPayload,
                            out var payloadBytesWritten,
                            out diagnostic),
                        Is.EqualTo(EbinWriteStatus.Complete),
                        diagnostic?.Category);
                    Assert.That(diagnostic, Is.Null);
                    Assert.That(payloadBytesWritten, Is.EqualTo(arrayPayload.Length));
                    Assert.That(BinaryPrimitives.ReadUInt64LittleEndian(arrayPayload), Is.EqualTo(2));

                    var scenesRootPayload = new byte[checked(arrayPayload.Length + hierarchyRecord.Length)];
                    Assert.That(
                        ScenePersistenceScenesWriter.Measure(
                            instances.Length,
                            arrayPayload,
                            hierarchyRecord,
                            EbinReadLimits.Foundation,
                            scenesRootPayload,
                            out var scenesWorkspace,
                            out diagnostic),
                        Is.EqualTo(EbinWriteStatus.Complete),
                        diagnostic?.Category);
                    Assert.That(diagnostic, Is.Null);
                    Assert.That(
                        lease.TryReserveAdmissionBytes(
                            checked(scenesWorkspace.WorkspaceBytes - arrayPayload.Length),
                            out var scenesReservation),
                        Is.True);
                    Assert.That(scenesReservation, Is.Not.Null);

                    using (scenesReservation)
                    {
                        var scenesCandidateStorage = new byte[scenesWorkspace.RecordBytes];
                        var scenesRecord = new byte[scenesWorkspace.RecordBytes];
                        Assert.That(
                            ScenePersistenceScenesWriter.Write(
                                scenesWorkspace,
                                instances.Length,
                                arrayPayload,
                                hierarchyRecord,
                                EbinReadLimits.Foundation,
                                scenesRootPayload,
                                scenesCandidateStorage,
                                scenesRecord,
                                out var scenesBytesWritten,
                                out diagnostic),
                            Is.EqualTo(EbinWriteStatus.Complete),
                            diagnostic?.Category);
                        Assert.That(diagnostic, Is.Null);
                        Assert.That(scenesBytesWritten, Is.EqualTo(scenesRecord.Length));
                        var scenesAssertions = new ScenePersistenceScenesRecordAssertions(
                            arrayPayload,
                            expectedSceneInstanceIds,
                            hierarchyRecord);
                        Assert.That(
                            EbinStructuralReader.Read(
                                scenesRecord,
                                EbinReadLimits.Foundation,
                                EbinReadPolicy.Canonical,
                                ref scenesAssertions,
                                out var scenesBytesConsumed,
                                out var scenesReadDiagnostic),
                            Is.EqualTo(EbinReadStatus.Complete));
                        Assert.That(scenesReadDiagnostic, Is.Null);
                        Assert.That(scenesAssertions.IsMatch, Is.True);
                        Assert.That(scenesBytesConsumed, Is.EqualTo(scenesRecord.Length));
                    }
                }
            }
            finally
            {
                await assets.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            File.Delete(packagePath);
        }
    }
}
