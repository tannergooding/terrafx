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
using TerraFX.Graphics.Vulkan;
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
            _ = owner.Pump();

            _ = Assert.Throws<InvalidOperationException>(
                () => Task.Run(() => _ = owner.Pump()).GetAwaiter().GetResult());
            _ = Assert.Throws<InvalidOperationException>(
                () => Task.Run(owner.DisposeFromOwner).GetAwaiter().GetResult());

            owner.DisposeFromOwner();
            owner.DisposeFromOwner();
            _ = Assert.Throws<ObjectDisposedException>(() => _ = owner.Pump());
        }
        finally
        {
            owner.DisposeFromOwner();
        }
    }

    [Test]
    public void CreatesAndShutsDownD3D12GraphicsDeviceUsingHostIssuedIdentity()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 backend targets Windows 11.");
            return;
        }

        var registration = D3D12GraphicsBackend.CreateRegistration();
        var options = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.D3D12,
            registration,
            CreateAssetStoreOptions());
        var platformOwner = PlatformHostOwner.Create(options);
        GraphicsDevice? device = null;

        try
        {
            try
            {
                device = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.OffscreenNative,
                    GraphicsPlatformKind.None,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"A required D3D12 system library is unavailable: {exception.Message}");
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"A required D3D12 system entry point is unavailable: {exception.Message}");
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No available D3D12 device satisfies OffscreenNative: {exception.Message}");
            }

            var graphicsDevice = device
                ?? throw new InvalidOperationException("D3D12 GraphicsDevice startup returned no device.");
            var capabilities = graphicsDevice.Capabilities;
            Assert.That(graphicsDevice.OwnerId, Is.EqualTo(platformOwner.OwnerId));
            Assert.That(graphicsDevice.Status, Is.EqualTo(GraphicsDeviceStatus.Ready));
            Assert.That(capabilities.Backend, Is.EqualTo(GraphicsBackend.D3D12));
            Assert.That(capabilities.Profile, Is.EqualTo(GraphicsDeviceProfile.OffscreenNative));
            Assert.That(capabilities.AdapterName, Is.Not.Empty);
            Assert.That(capabilities.DriverName, Is.Not.Empty);
            Assert.That(capabilities.DriverVersion, Is.Not.Empty);
            Assert.That(capabilities.ApiVersion.Major, Is.GreaterThan(0));
            TestContext.WriteLine(
                $"Backend={capabilities.Backend}; Profile={capabilities.Profile}; Adapter={capabilities.AdapterName}; "
                + $"Vendor={capabilities.VendorId:X4}; Device={capabilities.DeviceId:X4}; "
                + $"Driver={capabilities.DriverName} {capabilities.DriverVersion}; "
                + $"API={capabilities.ApiVersion.Major}.{capabilities.ApiVersion.Minor}.{capabilities.ApiVersion.Patch}");
        }
        finally
        {
            try
            {
                if (device is not null)
                {
                    device.ShutdownAsync().GetAwaiter().GetResult();
                    Assert.That(device.Status, Is.EqualTo(GraphicsDeviceStatus.Disposed));
                }
            }
            finally
            {
                platformOwner.DisposeFromOwner();
            }
        }
    }

    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The synchronous waits complete native requests before resource disposal, and the PlatformHostOwner must be used on its creating test thread.")]
    public void CreatesAndShutsDownVulkanGraphicsDeviceUsingHostIssuedIdentity()
    {
        var registration = VulkanGraphicsBackend.CreateRegistration();
        var options = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.Vulkan,
            registration,
            CreateAssetStoreOptions());
        var platformOwner = PlatformHostOwner.Create(options);
        GraphicsDevice? device = null;

        try
        {
            try
            {
                device = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.OffscreenNative,
                    GraphicsPlatformKind.None,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"No Vulkan loader is installed: {exception.Message}");
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"The installed Vulkan loader lacks a required entry point: {exception.Message}");
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No available Vulkan device satisfies OffscreenNative: {exception.Message}");
            }

            var graphicsDevice = device
                ?? throw new InvalidOperationException("Vulkan GraphicsDevice startup returned no device.");
            var capabilities = graphicsDevice.Capabilities;
            Assert.That(graphicsDevice.OwnerId, Is.EqualTo(platformOwner.OwnerId));
            Assert.That(graphicsDevice.Status, Is.EqualTo(GraphicsDeviceStatus.Ready));
            Assert.That(capabilities.Backend, Is.EqualTo(GraphicsBackend.Vulkan));
            Assert.That(capabilities.Profile, Is.EqualTo(GraphicsDeviceProfile.OffscreenNative));
            Assert.That(capabilities.AdapterName, Is.Not.Empty);
            Assert.That(capabilities.DriverName, Is.Not.Empty);
            Assert.That(capabilities.DriverVersion, Is.Not.Empty);
            Assert.That(capabilities.ApiVersion.Major, Is.EqualTo(1));
            Assert.That(capabilities.ApiVersion.Minor, Is.GreaterThanOrEqualTo(3));
            TestContext.WriteLine(
                $"Backend={capabilities.Backend}; Profile={capabilities.Profile}; Adapter={capabilities.AdapterName}; "
                + $"Vendor={capabilities.VendorId:X4}; Device={capabilities.DeviceId:X4}; "
                + $"Driver={capabilities.DriverName} {capabilities.DriverVersion}; "
                + $"API={capabilities.ApiVersion.Major}.{capabilities.ApiVersion.Minor}.{capabilities.ApiVersion.Patch}; "
                + $"Features={string.Join(",", capabilities.ImplementedFeatures)}");
            TestContext.WriteLine(
                $"VULKAN_SDK={Environment.GetEnvironmentVariable("VULKAN_SDK") ?? "(unset)"}; "
                + $"VK_INSTANCE_LAYERS={Environment.GetEnvironmentVariable("VK_INSTANCE_LAYERS") ?? "(unset)"}");

            GpuBuffer? buffer = null;

            try
            {
                buffer = graphicsDevice.CreateBufferAsync(
                    new BufferDescription(
                        256,
                        BufferUsage.CopySource | BufferUsage.CopyDestination,
                        GpuMemoryKind.DeviceLocal,
                        structuredStride: null,
                        debugName: "graphics-device-startup-roundtrip"))
                    .GetAwaiter()
                    .GetResult();
                Assert.That(buffer.Handle.IsValid, Is.True);
                Assert.That(buffer.Handle.OwnerId, Is.EqualTo(platformOwner.OwnerId));
                Assert.That(buffer.Handle.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
                var payload = new byte[32];

                for (var index = 0; index < payload.Length; index++)
                {
                    payload[index] = unchecked((byte)((index * 29) + 7));
                }

                var upload = graphicsDevice.UploadBufferAsync(buffer, 64, payload).GetAwaiter().GetResult();
                Assert.That(upload.Status, Is.EqualTo(UploadStatus.Ready));
                Assert.That(upload.DeviceEpoch, Is.EqualTo(graphicsDevice.DeviceEpoch));
                var completedFence = upload.CompletedFence
                    ?? throw new InvalidOperationException("A ready Vulkan upload has no completed fence.");
                Assert.That(completedFence, Is.GreaterThan(0));

                using var readback = graphicsDevice.ReadbackBufferAsync(buffer, 64, payload.Length).GetAwaiter().GetResult();
                using var readLease = readback.AcquireReadLease(0, payload.Length);
                var actual = new byte[payload.Length];
                var copyAction = new ReadbackCopyAction(actual);
                readLease.UseWindow(0, payload.Length, ref copyAction);
                Assert.That(actual, Is.EqualTo(payload));
            }
            finally
            {
                buffer?.Dispose();
            }
        }
        finally
        {
            try
            {
                if (device is not null)
                {
                    device.ShutdownAsync().GetAwaiter().GetResult();
                    Assert.That(device.Status, Is.EqualTo(GraphicsDeviceStatus.Disposed));
                }
            }
            finally
            {
                platformOwner.DisposeFromOwner();
            }
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
                _ = owner.Pump();

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

    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The dedicated STA test thread must transfer native failures after owner cleanup.")]
    public async Task WindowsPlatformOwnerWitnessesHiddenWindowCreationAndRetirement()
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
        var completion = new TaskCompletionSource<(WindowHandle Window, uint OwnerId, bool StaleCloseRejected)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => {
            PlatformHostOwner? owner = null;
            var window = default(WindowHandle);
            var ownerId = 0U;
            var staleCloseRejected = false;
            Exception? failure = null;

            try
            {
                owner = PlatformHostOwner.Create(options);
                ownerId = owner.OwnerId;
                window = owner.CreateWindow(new WindowOptions("TerraFX hidden window witness") { Visible = false });
                _ = owner.Pump();
                owner.CloseWindow(window);
                _ = owner.Pump();

                try
                {
                    owner.CloseWindow(window);
                }
                catch (InvalidOperationException)
                {
                    staleCloseRejected = true;
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
                _ = completion.TrySetResult((window, ownerId, staleCloseRejected));
            }
            else
            {
                _ = completion.TrySetException(failure);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var (window, ownerId, staleCloseRejected) = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        Assert.That(thread.Join(TimeSpan.FromSeconds(5)), Is.True);
        Assert.That(ownerId, Is.GreaterThan(0));
        Assert.That(window.IsValid, Is.True);
        Assert.That(window.OwnerId, Is.EqualTo(ownerId));
        Assert.That(staleCloseRejected, Is.True);
    }

    private readonly struct ReadbackCopyAction : IAction<ReadOnlySpan<byte>>
    {
        private readonly byte[] _destination;

        internal ReadbackCopyAction(byte[] destination)
        {
            _destination = destination;
        }

        public readonly void Invoke(scoped ReadOnlySpan<byte> argument)
            => argument.CopyTo(_destination);
    }
}
