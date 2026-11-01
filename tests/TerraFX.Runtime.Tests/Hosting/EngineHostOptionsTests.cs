// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;
using TerraFX.Hosting;
using TerraFX.Platform;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
internal sealed class EngineHostOptionsTests
{
    private static AssetStoreOptions CreateAssetStoreOptions()
    {
        var assignment = new ContentSchemaIdentityAssignment("test/native-root");
        var nativeRoot = new DataSchema(
            new ContentSchemaReference(assignment.SchemaId, 1, 0, ContentSchemaScope.Content),
            assignment,
            ContentSchemaRole.NativeAsset,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            []);
        var schemas = new CompiledContentSchemaRegistry([.. NativeAssetSchemaCatalogue.Schemas, nativeRoot]);
        var registration = new AssetStoreRegistrationBuilder(schemas).Build();

        return new AssetStoreOptions(Path.GetFullPath("content.epkg"), new byte[32], registration);
    }

    private static EngineHostOptions CreateHeadlessOptions(
        AssetStoreOptions assetStoreOptions,
        int? cpuWorkerCount = null,
        SimulationOverrunPolicy? overrunPolicy = null,
        GraphicsFailurePolicy? graphicsFailurePolicy = null,
        ClipboardServicePolicy? clipboardServicePolicy = null,
        TimeSpan? shutdownTimeout = null)
        => new(
            HostProfile.Headless,
            PlatformBackend.Headless,
            graphicsBackend: null,
            graphicsBackendRegistration: null,
            assetStoreOptions: assetStoreOptions) {
            CpuWorkerCount = cpuWorkerCount ?? Math.Min(64, Math.Max(1, Environment.ProcessorCount - 2)),
            OverrunPolicy = overrunPolicy ?? SimulationOverrunPolicy.Pause,
            GraphicsFailurePolicy = graphicsFailurePolicy ?? GraphicsFailurePolicy.StopHost,
            ClipboardServicePolicy = clipboardServicePolicy ?? ClipboardServicePolicy.NotRequested,
            ShutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(5),
        };

    [Test]
    public void AcceptsExplicitD3D12RegistrationsWithoutActivatingDevices()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 registration package targets Windows 11.");
            return;
        }

        var registration = D3D12GraphicsBackend.CreateRegistration();
        var assetStoreOptions = CreateAssetStoreOptions();
        var offscreen = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.D3D12,
            registration,
            assetStoreOptions);
        var interactive = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            registration,
            assetStoreOptions);

        Assert.That(offscreen.GraphicsBackendRegistration, Is.SameAs(registration));
        Assert.That(interactive.Platform, Is.EqualTo(PlatformBackend.WindowsNative));
        Assert.That(registration.Backend, Is.EqualTo(GraphicsBackend.D3D12));
    }

    [Test]
    public void CreatesAndRetiresHeadlessPlatformOwnerUsingCoreIssuedIdentity()
    {
        var options = CreateHeadlessOptions(CreateAssetStoreOptions());
        var owner = PlatformHostOwner.Create(options);

        try
        {
            Assert.That(owner.OwnerId, Is.GreaterThan(0));
            Assert.That(owner.Platform.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.NotRequested));
            Assert.That(owner.Platform.GetMonitorHandles(), Is.Empty);
            owner.Pump();

            _ = Assert.Throws<InvalidOperationException>(
                () => Task.Run(owner.Pump).GetAwaiter().GetResult());
            _ = Assert.Throws<InvalidOperationException>(
                () => Task.Run(owner.DisposeFromOwner).GetAwaiter().GetResult());

            owner.DisposeFromOwner();
            owner.DisposeFromOwner();
            _ = Assert.Throws<ObjectDisposedException>(owner.Pump);
        }
        finally
        {
            owner.DisposeFromOwner();
        }
    }

    [Test]
    public void DefaultsBoundedHostPoliciesAndRetainsAssetOptions()
    {
        var assetStoreOptions = CreateAssetStoreOptions();
        var options = new EngineHostOptions(
            HostProfile.Headless,
            PlatformBackend.Headless,
            graphicsBackend: null,
            graphicsBackendRegistration: null,
            assetStoreOptions: assetStoreOptions);

        Assert.That(options.AssetStoreOptions, Is.SameAs(assetStoreOptions));
        Assert.That(options.CpuWorkerCount, Is.EqualTo(Math.Min(64, Math.Max(1, Environment.ProcessorCount - 2))));
        Assert.That(options.OverrunPolicy, Is.EqualTo(SimulationOverrunPolicy.Pause));
        Assert.That(options.GraphicsFailurePolicy, Is.EqualTo(GraphicsFailurePolicy.StopHost));
        Assert.That(options.ClipboardServicePolicy, Is.EqualTo(ClipboardServicePolicy.NotRequested));
        Assert.That(options.ShutdownTimeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
        Assert.That((int)HostProfile.None, Is.EqualTo(0));
        Assert.That((int)HostProfile.Headless, Is.EqualTo(1));
        Assert.That((int)HostProfile.OffscreenNative, Is.EqualTo(2));
        Assert.That((int)HostProfile.Interactive, Is.EqualTo(3));
        Assert.That((int)SimulationOverrunPolicy.Pause, Is.EqualTo(0));
        Assert.That((int)SimulationOverrunPolicy.DropDebt, Is.EqualTo(1));
        Assert.That((int)GraphicsFailurePolicy.StopHost, Is.EqualTo(0));
        Assert.That(CreateHeadlessOptions(assetStoreOptions, cpuWorkerCount: 0).CpuWorkerCount, Is.Zero);
    }

    [Test]
    public void RejectsInvalidInitOnlyOptionValues()
    {
        var assetStoreOptions = CreateAssetStoreOptions();

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateHeadlessOptions(assetStoreOptions, cpuWorkerCount: -1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateHeadlessOptions(assetStoreOptions, cpuWorkerCount: 65));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateHeadlessOptions(assetStoreOptions, overrunPolicy: (SimulationOverrunPolicy)2));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateHeadlessOptions(assetStoreOptions, graphicsFailurePolicy: (GraphicsFailurePolicy)1));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateHeadlessOptions(assetStoreOptions, clipboardServicePolicy: (ClipboardServicePolicy)3));
        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = CreateHeadlessOptions(assetStoreOptions, shutdownTimeout: TimeSpan.Zero));
    }

    [Test]
    public void RejectsInvalidProfileAndPlatformCombinations()
    {
        var assetStoreOptions = CreateAssetStoreOptions();

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new EngineHostOptions(
                (HostProfile)int.MaxValue,
                PlatformBackend.Headless,
                graphicsBackend: null,
                graphicsBackendRegistration: null,
                assetStoreOptions: assetStoreOptions));
        _ = Assert.Throws<ArgumentException>(
            () => _ = new EngineHostOptions(
                HostProfile.Headless,
                PlatformBackend.WindowsNative,
                graphicsBackend: null,
                graphicsBackendRegistration: null,
                assetStoreOptions: assetStoreOptions));
        _ = Assert.Throws<ArgumentException>(
            () => _ = new EngineHostOptions(
                HostProfile.Headless,
                PlatformBackend.Headless,
                GraphicsBackend.D3D12,
                graphicsBackendRegistration: null,
                assetStoreOptions: assetStoreOptions));
        _ = Assert.Throws<ArgumentException>(
            () => _ = new EngineHostOptions(
                HostProfile.OffscreenNative,
                PlatformBackend.WaylandOnly,
                graphicsBackend: null,
                graphicsBackendRegistration: null,
                assetStoreOptions: assetStoreOptions));
        _ = Assert.Throws<ArgumentException>(
            () => _ = new EngineHostOptions(
                HostProfile.Interactive,
                PlatformBackend.Headless,
                graphicsBackend: null,
                graphicsBackendRegistration: null,
                assetStoreOptions: assetStoreOptions));
    }

    [Test]
    public void RejectsMismatchedRegistrationAndD3D12WaylandPairing()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 registration package targets Windows 11.");
            return;
        }

        var registration = D3D12GraphicsBackend.CreateRegistration();
        var assetStoreOptions = CreateAssetStoreOptions();

        _ = Assert.Throws<ArgumentException>(
            () => _ = new EngineHostOptions(
                HostProfile.OffscreenNative,
                PlatformBackend.Headless,
                GraphicsBackend.Vulkan,
                registration,
                assetStoreOptions));
        _ = Assert.Throws<ArgumentException>(
            () => _ = new EngineHostOptions(
                HostProfile.Interactive,
                PlatformBackend.WaylandOnly,
                GraphicsBackend.D3D12,
                registration,
                assetStoreOptions));
    }

    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The dedicated STA test thread must transfer native failures after owner cleanup.")]
    public async Task WindowsPlatformOwnerIssuesHandlesForEnumeratedMonitors()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            || (RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            Assert.Ignore("The Windows Platform backend requires Windows 11 x64.");
            return;
        }

        var options = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            D3D12GraphicsBackend.CreateRegistration(),
            CreateAssetStoreOptions());
        var completion = new TaskCompletionSource<(uint OwnerId, ImmutableArray<MonitorHandle> Monitors)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            PlatformHostOwner? owner = null;
            var ownerId = 0U;
            var monitors = ImmutableArray<MonitorHandle>.Empty;
            Exception? failure = null;

            try
            {
                owner = PlatformHostOwner.Create(options);
                ownerId = owner.OwnerId;
                monitors = owner.Platform.GetMonitorHandles();
                owner.Pump();

                foreach (var monitor in monitors)
                {
                    _ = owner.GetNativeMonitorHandle(monitor);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                owner?.DisposeFromOwner();
            }
            catch (Exception cleanupException)
            {
                failure = failure is null
                    ? cleanupException
                    : new AggregateException(failure, cleanupException);
            }

            if (failure is null)
            {
                _ = completion.TrySetResult((ownerId, monitors));
            }
            else
            {
                _ = completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var (completedOwnerId, monitorHandles) = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.That(thread.Join(TimeSpan.FromSeconds(5)), Is.True);

        if (monitorHandles.IsDefaultOrEmpty)
        {
            Assert.Ignore("The Windows test host has no active display monitors.");
            return;
        }

        Assert.That(completedOwnerId, Is.GreaterThan(0));
        var indices = new HashSet<uint>();

        foreach (var monitor in monitorHandles)
        {
            Assert.That(monitor.IsValid, Is.True);
            Assert.That(monitor.OwnerId, Is.EqualTo(completedOwnerId));
            Assert.That(indices.Add(monitor.Index), Is.True);
        }
    }
}
