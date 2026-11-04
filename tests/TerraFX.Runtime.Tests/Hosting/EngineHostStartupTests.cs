// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Hosting;
using TerraFX.Input;
using TerraFX.Platform;
using TerraFX.Runtime.Tests.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [Test]
    public async Task DropsHeadlessSimulationDebtAndContinuesTicksAsync()
    {
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var assetStoreOptions = new AssetStoreOptions(
            packagePath,
            SHA256.HashData(packageBytes),
            CreateAssetStoreOptions().Registration);
        var options = CreateHeadlessOptions(
            assetStoreOptions,
            cpuWorkerCount: 0,
            overrunPolicy: SimulationOverrunPolicy.DropDebt);
        EngineHost? host = null;
        World? world = null;

        try
        {
            host = await EngineHost.StartAsync(options).ConfigureAwait(false);
            var worldRegistration = new WorldRegistrationBuilder(assetStoreOptions.Registration.Schemas).Build();
            await host.InvokeAsync(engineContext => world = engineContext.CreateWorld(new WorldOptions(worldRegistration) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = Guid.NewGuid(),
            })).ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            var priorNextTick = -1L;
            await host.InvokeAsync(_ => priorNextTick = world!.NextTickIndex).ConfigureAwait(false);
            await host.InvokeAsync(static _ => Thread.Sleep(400)).ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            var resumedNextTick = -1L;
            await host.InvokeAsync(_ => resumedNextTick = world!.NextTickIndex).ConfigureAwait(false);
            var diagnostics = host.Diagnostics.GetSnapshot(
                new DiagnosticQuery(128, DiagnosticSeverity.Warning, world!.WorldGuid, minimumTick: null));

            Assert.Multiple(() => {
                Assert.That(priorNextTick, Is.GreaterThan(0));
                Assert.That(resumedNextTick, Is.GreaterThan(priorNextTick));
                Assert.That(diagnostics.Records.Any(static diagnostic => diagnostic.Code == "SimulationDebtDropped"), Is.True);
            });

            await host.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(packagePath);
        }
    }

    [Test]
    public async Task StartsHeadlessHostAndRunsOwnerCallbacksWithHostedWorldsAsync()
    {
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var assetStoreOptions = new AssetStoreOptions(
            packagePath,
            SHA256.HashData(packageBytes),
            CreateAssetStoreOptions().Registration);
        var options = CreateHeadlessOptions(assetStoreOptions, cpuWorkerCount: 0);
        EngineHost? host = null;

        try
        {
            host = await EngineHost.StartAsync(options).ConfigureAwait(false);
            var callingThreadId = Environment.CurrentManagedThreadId;
            var ownerThreadId = -1;
            var clipboardStatus = ClipboardServiceStatus.Unsupported;
            EngineContext? context = null;
            World? world = null;
            World? secondWorld = null;
            InputRouter? firstRouter = null;
            InputRouter? secondRouter = null;
            InputBindingReceipt? firstBinding = null;
            InputBindingReceipt? secondBinding = null;
            ImmutableArray<InputRoutingReceipt> inputReceipts = [];
            var worldRegistration = new WorldRegistrationBuilder(assetStoreOptions.Registration.Schemas).Build();

            await host.InvokeAsync(engineContext => {
                ownerThreadId = Environment.CurrentManagedThreadId;
                context = engineContext;
                clipboardStatus = host.Platform.ClipboardStatus;
                world = engineContext.CreateWorld(new WorldOptions(worldRegistration) {
                    CpuWorkerCount = 0,
                    InitialReplayLineageId = Guid.NewGuid(),
                    WorldGuid = Guid.NewGuid(),
                });
                secondWorld = engineContext.CreateWorld(new WorldOptions(worldRegistration) {
                    CpuWorkerCount = 0,
                    InitialReplayLineageId = Guid.NewGuid(),
                    WorldGuid = Guid.NewGuid(),
                });
                firstRouter = engineContext.CreateInputRouter(InputRecipientKind.SimulationTimeline);
                secondRouter = engineContext.CreateInputRouter(InputRecipientKind.SimulationTimeline);
                firstBinding = engineContext.BindHeadlessGameplayInput(firstRouter, world);
                secondBinding = engineContext.BindHeadlessGameplayInput(secondRouter, secondWorld);
                inputReceipts = engineContext.InjectHeadlessInput(
                    [firstRouter, secondRouter],
                    new InputInjection(new KeyboardControl(7, 4), InputKeyEventKind.Down));
            }).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(ownerThreadId, Is.Not.EqualTo(callingThreadId));
                Assert.That(host.Completion.IsCompleted, Is.False);
                Assert.That(host.Diagnostics, Is.Not.Null);
                Assert.That(clipboardStatus, Is.EqualTo(ClipboardServiceStatus.NotRequested));
                Assert.That(world, Is.Not.Null);
                Assert.That(secondWorld, Is.Not.Null);
                Assert.That(firstBinding!.Status, Is.EqualTo(InputBindingStatus.Bound));
                Assert.That(firstBinding.Router, Is.SameAs(firstRouter));
                Assert.That(secondBinding!.Status, Is.EqualTo(InputBindingStatus.Bound));
                Assert.That(inputReceipts, Has.Length.EqualTo(2));
                Assert.That(inputReceipts[0].Status, Is.EqualTo(InputRoutingStatus.Applied));
                Assert.That(inputReceipts[1].Status, Is.EqualTo(InputRoutingStatus.Applied));
                Assert.That(inputReceipts[0].HostSequence, Is.GreaterThan(0));
                Assert.That(inputReceipts[1].HostSequence, Is.EqualTo(inputReceipts[0].HostSequence));
                Assert.That(inputReceipts[0].RecipientId, Is.EqualTo(firstRouter!.RecipientId));
                Assert.That(inputReceipts[1].RecipientId, Is.EqualTo(secondRouter!.RecipientId));
            });

            _ = Assert.Throws<ObjectDisposedException>(
                () => _ = context!.CreateWorld(new WorldOptions(worldRegistration)));

            _ = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await host.InvokeAsync(_ => throw new InvalidOperationException("Expected owner callback failure."))
                    .ConfigureAwait(false));
            await host.InvokeAsync(static _ => { }).ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            var firstNextTick = -1L;
            var secondNextTick = -1L;
            InputActionSnapshot? firstSnapshot = null;
            InputActionSnapshot? secondSnapshot = null;

            await host.InvokeAsync(_ => {
                firstNextTick = world!.NextTickIndex;
                secondNextTick = secondWorld!.NextTickIndex;

                if (firstNextTick > 0)
                {
                    firstSnapshot = firstRouter!.GetSimulationActionSnapshot(firstNextTick - 1);
                }

                if (secondNextTick > 0)
                {
                    secondSnapshot = secondRouter!.GetSimulationActionSnapshot(secondNextTick - 1);
                }
            }).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(firstNextTick, Is.GreaterThan(0));
                Assert.That(secondNextTick, Is.GreaterThan(0));
                Assert.That(firstSnapshot!.AssignedTick, Is.EqualTo(firstNextTick - 1));
                Assert.That(secondSnapshot!.AssignedTick, Is.EqualTo(secondNextTick - 1));
                Assert.That(inputReceipts[0].AssignedTick, Is.EqualTo(0));
                Assert.That(inputReceipts[1].AssignedTick, Is.EqualTo(0));
            });

            await host.InvokeAsync(static _ => Thread.Sleep(400)).ConfigureAwait(false);
            var pausedNextTick = -1L;
            await host.InvokeAsync(_ => pausedNextTick = world!.NextTickIndex).ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            var stillPausedNextTick = -1L;
            await host.InvokeAsync(_ => stillPausedNextTick = world!.NextTickIndex).ConfigureAwait(false);
            var overrunDiagnostics = host.Diagnostics.GetSnapshot(
                new DiagnosticQuery(128, DiagnosticSeverity.Warning, world!.WorldGuid, minimumTick: null));

            Assert.Multiple(() => {
                Assert.That(stillPausedNextTick, Is.EqualTo(pausedNextTick));
                Assert.That(overrunDiagnostics.Records.Any(static diagnostic => diagnostic.Code == "SimulationOverrun"), Is.True);
            });

            await host.InvokeAsync(_ => world!.SetTimeScale(2)).ConfigureAwait(false);
            await Task.Delay(100).ConfigureAwait(false);
            var resumedNextTick = -1L;
            await host.InvokeAsync(_ => resumedNextTick = world!.NextTickIndex).ConfigureAwait(false);
            Assert.That(resumedNextTick, Is.GreaterThan(stillPausedNextTick));

            await host.StopAsync().ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(host.Completion.IsCompletedSuccessfully, Is.True);
                Assert.That(() => _ = world!.LastFault, Throws.TypeOf<ObjectDisposedException>());
                Assert.That(() => _ = secondWorld!.LastFault, Throws.TypeOf<ObjectDisposedException>());
                Assert.That(() => _ = host.InvokeAsync(static _ => { }), Throws.TypeOf<ObjectDisposedException>());
            });
        }
        finally
        {
            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }

            File.Delete(packagePath);
        }
    }
}
