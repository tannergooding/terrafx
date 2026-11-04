// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Hosting;
using TerraFX.Platform;
using TerraFX.Runtime.Tests.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [Test]
    public async Task StartsHeadlessHostAndRunsOwnerCallbacksWithHostedWorlds()
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
            }).ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(ownerThreadId, Is.Not.EqualTo(callingThreadId));
                Assert.That(host.Completion.IsCompleted, Is.False);
                Assert.That(host.Diagnostics, Is.Not.Null);
                Assert.That(clipboardStatus, Is.EqualTo(ClipboardServiceStatus.NotRequested));
                Assert.That(world, Is.Not.Null);
            });

            _ = Assert.Throws<ObjectDisposedException>(
                () => _ = context!.CreateWorld(new WorldOptions(worldRegistration)));

            _ = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await host.InvokeAsync(_ => throw new InvalidOperationException("Expected owner callback failure."))
                    .ConfigureAwait(false));
            await host.InvokeAsync(static _ => { }).ConfigureAwait(false);
            await host.StopAsync().ConfigureAwait(false);

            Assert.Multiple(() => {
                Assert.That(host.Completion.IsCompletedSuccessfully, Is.True);
                Assert.That(() => _ = world!.LastFault, Throws.TypeOf<ObjectDisposedException>());
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
