// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Security.Cryptography;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Content.Tools;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Hosting;
using TerraFX.Interop.Vulkan;
using TerraFX.Numerics;
using TerraFX.Platform;
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Scenes;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    private static (
        EngineHostOptions Options,
        GraphicsBackendRegistration Registration,
        CompiledContentSchemaRegistry Schemas) CreateVulkanSurfaceTestSetup(
            PlatformBackend platformBackend,
            Action? surfaceImageAcquiredObserver = null,
            Func<VkResult?>? surfaceAcquireResultOverride = null,
            Func<VkResult, VkResult>? surfacePresentResultOverride = null,
            Func<ulong, VkResult, VkResult>? surfaceFenceWaitResultOverride = null,
            ulong surfaceFenceWaitTimeoutNanoseconds = 5_000_000_000,
            ClipboardServicePolicy clipboardServicePolicy = ClipboardServicePolicy.NotRequested)
    {
        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);
        var registration = (surfaceImageAcquiredObserver is null)
            && (surfaceAcquireResultOverride is null)
            && (surfacePresentResultOverride is null)
            && (surfaceFenceWaitResultOverride is null)
            ? VulkanGraphicsBackend.CreateRegistration()
            : VulkanGraphicsBackend.CreateRegistrationForTesting(
                graphFenceWaitObserver: null,
                completedGraphFenceValueOverride: null,
                graphSubmissionObserver: null,
                surfaceImageAcquiredObserver: surfaceImageAcquiredObserver,
                surfaceAcquireResultOverride: surfaceAcquireResultOverride,
                surfacePresentResultOverride: surfacePresentResultOverride,
                surfaceFenceWaitResultOverride: surfaceFenceWaitResultOverride,
                surfaceFenceWaitTimeoutNanoseconds: surfaceFenceWaitTimeoutNanoseconds);
        var options = new EngineHostOptions(
            HostProfile.Interactive,
            platformBackend,
            GraphicsBackend.Vulkan,
            registration,
            new AssetStoreOptions(
                packagePath,
                SHA256.HashData(packageBytes),
                CreateAssetStoreOptions().Registration)) {
            CpuWorkerCount = 0,
            GraphicsFrameSlotCount = 2,
            ClipboardServicePolicy = clipboardServicePolicy,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        return (options, registration, schemas);
    }

    private static PlatformBackend? GetInteractivePlatformBackend()
    {
        if (OperatingSystem.IsWindows())
        {
            return PlatformBackend.WindowsNative;
        }

        if (OperatingSystem.IsLinux()
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return PlatformBackend.WaylandOnly;
        }

        if (OperatingSystem.IsLinux()
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"))
            && (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_X11") == "1"))
        {
            return PlatformBackend.X11Only;
        }

        return null;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "Both render submissions and presentation waits complete before the render snapshot is disposed.")]
    private static void RunVulkanSurfaceWithDirectOwners(
        EngineHostOptions options,
        PlatformBackend platformBackend,
        GraphicsBackendRegistration registration,
        CompiledContentSchemaRegistry schemas,
        bool expectAcquiredImageFailure = false,
        PresentationStatus expectedFirstPresentationStatus = PresentationStatus.Presented,
        VkResult? expectedFirstAcquireResult = null,
        bool expectUncertainPresentFailure = false,
        bool expectPresentationFenceTimeout = false,
        VkResult? expectedFirstPresentationNativeResult = null,
        Action<PlatformHostOwner, WindowHandle>? beforeFirstPresentation = null,
        bool exerciseNativeWindowReplacement = false,
        bool expectWaylandCompositorDisconnect = false)
    {
        var platformOwner = PlatformHostOwner.Create(options);
        GraphicsDevice? graphicsDevice = null;
        GpuResidencyService? residencyService = null;
        WorldDiagnosticsServiceOwner? diagnosticsServiceOwner = null;
        Renderer? rendererForCleanup = null;
        Surface? surface = null;
        World? renderWorld = null;
        WindowHandle? window = null;

        try
        {
            window = platformOwner.CreateWindow(new WindowOptions("Vulkan WSI witness") {
                ClientSize = new Vector2(640, 360),
                Visible = false,
            });
            var clientPixelSize = platformOwner.Platform.GetMetrics(window.Value).ClientPixelSize;
            var binding = platformOwner.GetGraphicsNativeSurfaceBinding(window.Value, surfaceEpoch: 1);

            try
            {
                graphicsDevice = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.Interactive,
                    options.GraphicsFrameSlotCount,
                    platformOwner.GraphicsPlatformKind,
                    binding,
                    default).GetAwaiter().GetResult();
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No Vulkan adapter satisfies Interactive presentation on {platformBackend}: {exception.Message}");
                return;
            }

            surface = graphicsDevice.CreateSurfaceAsync(binding, clientPixelSize, default).GetAwaiter().GetResult();
            var activeSurface = surface
                ?? throw new InvalidOperationException("Vulkan surface creation returned no Surface.");

            if (exerciseNativeWindowReplacement || expectWaylandCompositorDisconnect)
            {
                var capabilities = graphicsDevice.Capabilities;
                TestContext.WriteLine(
                    $"Platform={platformBackend}; OS={Environment.OSVersion.VersionString}; "
                    + $"Backend={capabilities.Backend}; Profile={capabilities.Profile}; Adapter={capabilities.AdapterName}; "
                    + $"Vendor={capabilities.VendorId:X4}; Device={capabilities.DeviceId:X4}; "
                    + $"Driver={capabilities.DriverName} {capabilities.DriverVersion}; "
                    + $"API={capabilities.ApiVersion.Major}.{capabilities.ApiVersion.Minor}.{capabilities.ApiVersion.Patch}; "
                    + $"Features={string.Join(",", capabilities.ImplementedFeatures)}");
            }

            Assert.That(activeSurface.Capabilities.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(activeSurface.Capabilities.SwapchainEpoch, Is.EqualTo(1));
            Assert.That(activeSurface.Capabilities.PresentModes, Does.Contain(GraphicsPresentMode.Fifo));
            Assert.That(
                activeSurface.ColorFormat,
                Is.AnyOf(
                    TextureFormat.Bgra8Srgb,
                    TextureFormat.Rgba8Srgb,
                    TextureFormat.Bgra8Unorm,
                    TextureFormat.Rgba8Unorm));
            Assert.That(activeSurface.PresentationSourceFormat, Is.AnyOf(TextureFormat.Bgra8Srgb, TextureFormat.Rgba8Srgb));
            Assert.That(
                SurfaceFormatCompatibility.IsSrgbPresentationSourceCompatible(
                    activeSurface.PresentationSourceFormat,
                    activeSurface.ColorFormat),
                Is.True);
            residencyService = new GpuResidencyService(graphicsDevice);
            diagnosticsServiceOwner = DiagnosticsServiceTestHelper.CreateHostOwner().Owner;
            var renderer = Renderer.Create(graphicsDevice, residencyService, diagnosticsServiceOwner, platformOwner.OwnerId);
            rendererForCleanup = renderer;
            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            renderWorld = World.Create(new WorldOptions(worldRegistrationBuilder.Build()) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = Guid.NewGuid(),
            });
            var camera = new SceneComponentDefinitions.CameraComponent(
                (byte)CameraProjectionKind.Perspective,
                float.Pi / 3.0f,
                10.0f,
                0.1f,
                (byte)CameraFarMode.Finite,
                new SceneComponentDefinitions.OptionalSingle(1000.0f),
                default,
                new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
                (byte)(CameraClearFlags.Color | CameraClearFlags.Depth),
                new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                ulong.MaxValue,
                order: 0);
            var transform = new SceneComponentDefinitions.LocalTransformComponent(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One);
            var rootPosition = new SceneComponentDefinitions.RootPositionComponent(default);
            var cameraEntity = renderWorld.CreateEntity(new EntityDescription(components: [
                ComponentInitialization.Value(in camera),
                ComponentInitialization.Value(in transform),
                ComponentInitialization.Value(in rootPosition),
            ]));
            var cameraId = renderWorld.GetEntityId(cameraEntity);
            var publication = renderWorld.Step().Publication;
            var extraction = Renderer.CreateHeadless().Extract(
                renderWorld,
                new RenderExtractionOptions3D(publication.Frame3, clientPixelSize, [cameraId]));
            Assert.That(extraction.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            using var renderSnapshot = extraction.Snapshot
                ?? throw new InvalidOperationException("Successful Vulkan extraction omitted its render snapshot.");

            RenderGraphPlan CreateSurfaceGraphPlan(
                string name,
                Surface? targetSurface = null,
                PixelSize? targetPixelSize = null)
            {
                var selectedSurface = targetSurface ?? activeSurface;
                var selectedPixelSize = targetPixelSize ?? clientPixelSize;
                var graph = renderer.BuildGraph(renderSnapshot, selectedSurface);
                var source = graph.CreateTransientTexture(new TextureDescription(
                    TextureDimension.Dimension2D,
                    selectedPixelSize.Width,
                    selectedPixelSize.Height,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    format: selectedSurface.PresentationSourceFormat,
                    usage: TextureUsage.ColorAttachment | TextureUsage.CopySource,
                    debugName: name));
                var pass = graph.AddRasterPass(new RenderPassDescription(
                    $"{name}-clear",
                    order: 0,
                    predecessors: [],
                    hasExternalSideEffects: false));
                var output = graph.WriteColorAttachment(
                    pass,
                    source,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    new Vector4(0.1f, 0.2f, 0.3f, 1.0f));
                graph.SetPresentationSource(output);
                var response = graph.Compile();
                Assert.That(response.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
                return response.Plan
                    ?? throw new InvalidOperationException("The compiled Vulkan Surface graph omitted its plan.");
            }

            beforeFirstPresentation?.Invoke(platformOwner, window.Value);

            if (exerciseNativeWindowReplacement)
            {
                var surfaceLost = false;
                PresentationStatus? nativePresentationStatus = null;
                int? nativePresentationResult = null;
                string? nativePresentationFailure = null;

                try
                {
                    var lostSubmission = renderer.SubmitAsync(
                        renderSnapshot,
                        CreateSurfaceGraphPlan("vulkan-native-surface-loss"))
                        .GetAwaiter()
                        .GetResult();
                    var lostSequence = lostSubmission.PresentationSequence
                        ?? throw new InvalidOperationException("The native surface-loss submission omitted its presentation sequence.");
                    var lostPresentation = activeSurface.WaitForPresentationAsync(lostSequence).GetAwaiter().GetResult();
                    nativePresentationStatus = lostPresentation.Status;
                    nativePresentationResult = lostPresentation.Diagnostic?.NativeResult;
                    surfaceLost = nativePresentationStatus is PresentationStatus.SurfaceLost;
                }
                catch (InvalidOperationException exception) when (
                    exception.Message.Contains("native surface was lost", StringComparison.Ordinal)
                    || exception.Message.Contains("VK_ERROR_SURFACE_LOST_KHR", StringComparison.Ordinal))
                {
                    surfaceLost = true;
                    nativePresentationFailure = exception.Message;
                }

                TestContext.WriteLine(
                    $"Native surface disruption produced SurfaceLost={surfaceLost}; "
                    + $"presentation status={nativePresentationStatus?.ToString() ?? "lost during acquire"}; "
                    + $"native result={nativePresentationResult?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? nativePresentationFailure ?? "unavailable"}.");
                Assert.That(surfaceLost || nativePresentationStatus is not null, Is.True);

                if (nativePresentationStatus is PresentationStatus presentationStatus)
                {
                    Assert.That(
                        presentationStatus,
                        Is.AnyOf(PresentationStatus.Presented, PresentationStatus.SurfaceLost, PresentationStatus.SwapchainLost));
                }

                if (expectWaylandCompositorDisconnect)
                {
                    Assert.That(
                        surfaceLost || nativePresentationStatus is PresentationStatus.SwapchainLost,
                        Is.True);

                    if (nativePresentationStatus is PresentationStatus.SwapchainLost)
                    {
                        Assert.That(
                            nativePresentationResult,
                            Is.EqualTo((int)VkResult.VK_ERROR_OUT_OF_DATE_KHR));
                    }

                    graphicsDevice.RetireSurfaceAsync(activeSurface).GetAwaiter().GetResult();
                    surface = null;
                    platformOwner.CloseWindow(window.Value);
                    window = null;
                    return;
                }

                if (surfaceLost)
                {
                    _ = Assert.Throws<InvalidOperationException>(
                        () => activeSurface.ResizeAsync(clientPixelSize).GetAwaiter().GetResult());
                }

                graphicsDevice.RetireSurfaceAsync(activeSurface).GetAwaiter().GetResult();
                surface = null;
                platformOwner.CloseWindow(window.Value);
                window = null;

                var replacementWindow = platformOwner.CreateWindow(new WindowOptions("Vulkan replacement Xlib surface") {
                    ClientSize = new Vector2(640, 360),
                    Visible = false,
                });

                try
                {
                    var replacementPixelSize = platformOwner.Platform.GetMetrics(replacementWindow).ClientPixelSize;
                    var replacementBinding = platformOwner.GetGraphicsNativeSurfaceBinding(
                        replacementWindow,
                        surfaceEpoch: 1);
                    var replacementSurface = graphicsDevice.CreateSurfaceAsync(
                        replacementBinding,
                        replacementPixelSize,
                        default).GetAwaiter().GetResult();

                    try
                    {
                        var replacementSubmission = renderer.SubmitAsync(
                            renderSnapshot,
                            CreateSurfaceGraphPlan(
                                "vulkan-replacement-xlib-surface",
                                replacementSurface,
                                replacementPixelSize))
                            .GetAwaiter()
                            .GetResult();
                        var replacementSequence = replacementSubmission.PresentationSequence
                            ?? throw new InvalidOperationException("The replacement Xlib surface omitted its presentation sequence.");
                        var replacementPresentation = replacementSurface
                            .WaitForPresentationAsync(replacementSequence)
                            .GetAwaiter()
                            .GetResult();
                        Assert.That(replacementPresentation.Status, Is.EqualTo(PresentationStatus.Presented));
                    }
                    finally
                    {
                        graphicsDevice.RetireSurfaceAsync(replacementSurface).GetAwaiter().GetResult();
                    }
                }
                finally
                {
                    platformOwner.CloseWindow(replacementWindow);
                }

                return;
            }

            if (expectedFirstAcquireResult == VkResult.VK_ERROR_SURFACE_LOST_KHR)
            {
                _ = Assert.Throws<InvalidOperationException>(
                    () => renderer.SubmitAsync(
                        renderSnapshot,
                        CreateSurfaceGraphPlan("vulkan-surface-acquire-loss"))
                        .GetAwaiter()
                        .GetResult());
                _ = Assert.Throws<InvalidOperationException>(
                    () => activeSurface.ResizeAsync(clientPixelSize).GetAwaiter().GetResult());
                graphicsDevice.RetireSurfaceAsync(activeSurface).GetAwaiter().GetResult();
                platformOwner.CloseWindow(window.Value);
                window = null;
                return;
            }

            if (expectedFirstAcquireResult == VkResult.VK_ERROR_OUT_OF_DATE_KHR)
            {
                _ = Assert.Throws<InvalidOperationException>(
                    () => renderer.SubmitAsync(
                        renderSnapshot,
                        CreateSurfaceGraphPlan("vulkan-surface-stale-after-acquire-recreate"))
                        .GetAwaiter()
                        .GetResult());
                Assert.That(activeSurface.Capabilities.SwapchainEpoch, Is.EqualTo(2));
            }

            if (expectedFirstAcquireResult is VkResult.VK_TIMEOUT or VkResult.VK_NOT_READY)
            {
                _ = Assert.Throws<TimeoutException>(
                    () => renderer.SubmitAsync(
                        renderSnapshot,
                        CreateSurfaceGraphPlan("vulkan-surface-acquire-no-image"))
                        .GetAwaiter()
                        .GetResult());
            }

            if (expectUncertainPresentFailure)
            {
                _ = Assert.Throws<InvalidOperationException>(
                    () => renderer.SubmitAsync(
                        renderSnapshot,
                        CreateSurfaceGraphPlan("vulkan-surface-unknown-present-error"))
                        .GetAwaiter()
                        .GetResult());
                Assert.That(activeSurface.IsQuarantined, Is.True);
                return;
            }

            if (expectPresentationFenceTimeout)
            {
                var timeoutSubmission = renderer.SubmitAsync(
                    renderSnapshot,
                    CreateSurfaceGraphPlan("vulkan-surface-present-fence-timeout"))
                    .GetAwaiter()
                    .GetResult();
                var timeoutSequence = timeoutSubmission.PresentationSequence
                    ?? throw new InvalidOperationException("The timeout Vulkan Surface submission omitted its presentation sequence.");
                _ = Assert.Throws<TimeoutException>(
                    () => activeSurface.WaitForPresentationAsync(timeoutSequence).GetAwaiter().GetResult());
                _ = Assert.Throws<TimeoutException>(
                    () => graphicsDevice.RetireSurfaceAsync(activeSurface).GetAwaiter().GetResult());
                Assert.That(activeSurface.IsQuarantined, Is.True);
                return;
            }

            if (expectAcquiredImageFailure)
            {
                _ = Assert.Throws<InvalidOperationException>(
                    () => renderer.SubmitAsync(
                        renderSnapshot,
                        CreateSurfaceGraphPlan("vulkan-surface-acquire-cleanup"))
                        .GetAwaiter()
                        .GetResult());
            }

            var firstSubmission = renderer.SubmitAsync(
                renderSnapshot,
                CreateSurfaceGraphPlan("vulkan-surface-first-frame"))
                .GetAwaiter()
                .GetResult();
            var firstSequence = firstSubmission.PresentationSequence
                ?? throw new InvalidOperationException("The first Vulkan Surface submission omitted its presentation sequence.");
            var firstPresentation = activeSurface.WaitForPresentationAsync(firstSequence).GetAwaiter().GetResult();

            if (expectedFirstPresentationNativeResult is VkResult expectedNativeResult)
            {
                Assert.That(firstPresentation.Diagnostic?.NativeResult, Is.EqualTo((int)expectedNativeResult));
            }

            if (expectedFirstPresentationStatus is PresentationStatus.SurfaceLost)
            {
                Assert.That(firstPresentation.Status, Is.EqualTo(PresentationStatus.SurfaceLost));
                _ = Assert.Throws<InvalidOperationException>(
                    () => activeSurface.ResizeAsync(clientPixelSize).GetAwaiter().GetResult());
                graphicsDevice.RetireSurfaceAsync(activeSurface).GetAwaiter().GetResult();
                platformOwner.CloseWindow(window.Value);
                window = null;
                var surfaceLostHistory = activeSurface.WaitForPresentationAsync(firstSequence).GetAwaiter().GetResult();
                Assert.That(surfaceLostHistory.Status, Is.EqualTo(PresentationStatus.SurfaceLost));
                return;
            }

            if (expectedFirstPresentationStatus is PresentationStatus.SwapchainLost)
            {
                var stalePlan = CreateSurfaceGraphPlan("vulkan-surface-stale-after-recreate");
                _ = Assert.Throws<InvalidOperationException>(
                    () => renderer.SubmitAsync(renderSnapshot, stalePlan).GetAwaiter().GetResult());
            }

            var secondSubmission = renderer.SubmitAsync(
                renderSnapshot,
                CreateSurfaceGraphPlan("vulkan-surface-second-frame"))
                .GetAwaiter()
                .GetResult();
            var secondSequence = secondSubmission.PresentationSequence
                ?? throw new InvalidOperationException("The second Vulkan Surface submission omitted its presentation sequence.");
            Assert.That(secondSequence, Is.GreaterThan(firstSequence));
            var secondPresentation = activeSurface.WaitForPresentationAsync(secondSequence).GetAwaiter().GetResult();
            Assert.That(firstPresentation.Status, Is.EqualTo(expectedFirstPresentationStatus));
            Assert.That(secondPresentation.Status, Is.EqualTo(PresentationStatus.Presented));
            var swapchainEpochBeforeResize = activeSurface.Capabilities.SwapchainEpoch;

            var resized = activeSurface.ResizeAsync(clientPixelSize).GetAwaiter().GetResult();
            Assert.That(resized.Status, Is.EqualTo(SurfaceResizeStatus.Resized));
            Assert.That(resized.SwapchainEpoch, Is.EqualTo(swapchainEpochBeforeResize + 1));
            var suspended = activeSurface.ResizeAsync(new PixelSize(0, 0)).GetAwaiter().GetResult();
            Assert.That(suspended.Status, Is.EqualTo(SurfaceResizeStatus.Suspended));
            Assert.That(suspended.SwapchainEpoch, Is.Zero);
            var restored = activeSurface.ResizeAsync(clientPixelSize).GetAwaiter().GetResult();
            Assert.That(restored.SwapchainEpoch, Is.EqualTo(swapchainEpochBeforeResize + 2));

            graphicsDevice.RetireSurfaceAsync(activeSurface).GetAwaiter().GetResult();
            platformOwner.CloseWindow(window.Value);
            window = null;
            var historicalPresentation = activeSurface.WaitForPresentationAsync(firstSequence).GetAwaiter().GetResult();
            Assert.That(historicalPresentation.Status, Is.EqualTo(expectedFirstPresentationStatus));
        }
        finally
        {
            try
            {
                rendererForCleanup?.StopAndDrainSubmissionsAsync().GetAwaiter().GetResult();
                renderWorld?.Dispose();

                if ((surface is not null)
                    && !expectUncertainPresentFailure
                    && !expectPresentationFenceTimeout)
                {
                    graphicsDevice?.RetireSurfaceAsync(surface).GetAwaiter().GetResult();
                }
            }
            finally
            {
                try
                {
                    if (window is WindowHandle activeWindow)
                    {
                        platformOwner.CloseWindow(activeWindow);
                    }
                }
                finally
                {
                    try
                    {
                        residencyService?.StopAsync().GetAwaiter().GetResult();
                    }
                    finally
                    {
                        try
                        {
                            if (expectUncertainPresentFailure)
                            {
                                _ = Assert.Throws<InvalidOperationException>(
                                    () => graphicsDevice?.ShutdownAsync().GetAwaiter().GetResult());
                            }
                            else if (expectPresentationFenceTimeout)
                            {
                                _ = Assert.Throws<TimeoutException>(
                                    () => graphicsDevice?.ShutdownAsync().GetAwaiter().GetResult());
                            }
                            else
                            {
                                graphicsDevice?.ShutdownAsync().GetAwaiter().GetResult();
                            }
                        }
                        finally
                        {
                            try
                            {
                                platformOwner.DisposeFromOwner();
                            }
                            finally
                            {
                                diagnosticsServiceOwner?.CloseService();
                            }
                        }
                    }
                }
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "Both render submissions and presentation waits complete before the render snapshot is disposed.")]
    [Test]
    public void VulkanBackendPresentsInteractiveSurfaceImagesAndDrainsRetirement()
    {
        var platformBackend = GetInteractivePlatformBackend();

        if (platformBackend is null)
        {
            Assert.Ignore("The Vulkan Surface witness requires a Windows or Linux interactive display.");
            return;
        }

        var failNextAcquiredImage = true;
        void FailNextAcquiredImage()
        {
            if (!failNextAcquiredImage)
            {
                return;
            }

            failNextAcquiredImage = false;
            throw new InvalidOperationException("Injected failure after Vulkan image acquisition.");
        }

        var (options, registration, schemas) = CreateVulkanSurfaceTestSetup(
            platformBackend.Value,
            FailNextAcquiredImage);

        if (OperatingSystem.IsLinux())
        {
            RunVulkanSurfaceWithDirectOwners(
                options,
                platformBackend.Value,
                registration,
                schemas,
                expectAcquiredImageFailure: true);
            return;
        }

        EngineHost? host = null;
        World? renderWorld = null;

        try
        {
            try
            {
                host = EngineHost.StartAsync(options).GetAwaiter().GetResult();
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No Vulkan adapter satisfies Interactive presentation: {exception.Message}");
                return;
            }

            var runningHost = host
                ?? throw new InvalidOperationException("Interactive EngineHost startup returned no host.");
            var windowReceipt = runningHost.CreateWindowAsync(new WindowOptions("Vulkan WSI witness") {
                ClientSize = new Vector2(640, 360),
                Visible = false,
            }).GetAwaiter().GetResult();
            Assert.That(windowReceipt.Status, Is.EqualTo(WindowCreationStatus.Created));
            var window = windowReceipt.Window
                ?? throw new InvalidOperationException("Vulkan window creation returned no WindowHandle.");
            Surface? surface = null;
            runningHost.InvokeAsync(_ => surface = runningHost.GetSurface(window)).GetAwaiter().GetResult();
            var activeSurface = surface
                ?? throw new InvalidOperationException("Vulkan EngineHost.GetSurface returned no Surface.");
            Assert.That(activeSurface.Capabilities.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(activeSurface.Capabilities.SwapchainEpoch, Is.EqualTo(1));
            Assert.That(activeSurface.Capabilities.PresentModes, Does.Contain(GraphicsPresentMode.Fifo));
            Assert.That(
                activeSurface.ColorFormat,
                Is.AnyOf(
                    TextureFormat.Bgra8Srgb,
                    TextureFormat.Rgba8Srgb,
                    TextureFormat.Bgra8Unorm,
                    TextureFormat.Rgba8Unorm));
            Assert.That(activeSurface.PresentationSourceFormat, Is.AnyOf(TextureFormat.Bgra8Srgb, TextureFormat.Rgba8Srgb));
            Assert.That(
                SurfaceFormatCompatibility.IsSrgbPresentationSourceCompatible(
                    activeSurface.PresentationSourceFormat,
                    activeSurface.ColorFormat),
                Is.True);

            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            renderWorld = World.Create(new WorldOptions(worldRegistrationBuilder.Build()) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = Guid.NewGuid(),
            });
            var camera = new SceneComponentDefinitions.CameraComponent(
                (byte)CameraProjectionKind.Perspective,
                float.Pi / 3.0f,
                10.0f,
                0.1f,
                (byte)CameraFarMode.Finite,
                new SceneComponentDefinitions.OptionalSingle(1000.0f),
                default,
                new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
                (byte)(CameraClearFlags.Color | CameraClearFlags.Depth),
                new Vector4(0.0f, 0.0f, 0.0f, 1.0f),
                ulong.MaxValue,
                order: 0);
            var transform = new SceneComponentDefinitions.LocalTransformComponent(
                Vector3.Zero,
                Quaternion.Identity,
                Vector3.One);
            var rootPosition = new SceneComponentDefinitions.RootPositionComponent(default);
            var cameraEntity = renderWorld.CreateEntity(new EntityDescription(components: [
                ComponentInitialization.Value(in camera),
                ComponentInitialization.Value(in transform),
                ComponentInitialization.Value(in rootPosition),
            ]));
            var cameraId = renderWorld.GetEntityId(cameraEntity);
            var publication = renderWorld.Step().Publication;
            var extraction = Renderer.CreateHeadless().Extract(
                renderWorld,
                new RenderExtractionOptions3D(publication.Frame3, new PixelSize(640, 360), [cameraId]));
            Assert.That(extraction.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            using var renderSnapshot = extraction.Snapshot
                ?? throw new InvalidOperationException("Successful Vulkan extraction omitted its render snapshot.");

            RenderGraphPlan CreateSurfaceGraphPlan(string name)
            {
                var graph = runningHost.Renderer.BuildGraph(renderSnapshot, activeSurface);
                var source = graph.CreateTransientTexture(new TextureDescription(
                    TextureDimension.Dimension2D,
                    640,
                    360,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    format: activeSurface.PresentationSourceFormat,
                    usage: TextureUsage.ColorAttachment | TextureUsage.CopySource,
                    debugName: name));
                var pass = graph.AddRasterPass(new RenderPassDescription(
                    $"{name}-clear",
                    order: 0,
                    predecessors: [],
                    hasExternalSideEffects: false));
                var output = graph.WriteColorAttachment(
                    pass,
                    source,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    new Vector4(0.1f, 0.2f, 0.3f, 1.0f));
                graph.SetPresentationSource(output);
                var response = graph.Compile();
                Assert.That(response.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
                return response.Plan
                    ?? throw new InvalidOperationException("The compiled Vulkan Surface graph omitted its plan.");
            }

            _ = Assert.Throws<InvalidOperationException>(
                () => runningHost.Renderer.SubmitAsync(
                    renderSnapshot,
                    CreateSurfaceGraphPlan("vulkan-surface-acquire-cleanup"))
                    .GetAwaiter()
                    .GetResult());

            var firstSubmission = runningHost.Renderer.SubmitAsync(
                renderSnapshot,
                CreateSurfaceGraphPlan("vulkan-surface-first-frame"))
                .GetAwaiter()
                .GetResult();
            var secondSubmission = runningHost.Renderer.SubmitAsync(
                renderSnapshot,
                CreateSurfaceGraphPlan("vulkan-surface-second-frame"))
                .GetAwaiter()
                .GetResult();
            var firstSequence = firstSubmission.PresentationSequence
                ?? throw new InvalidOperationException("The first Vulkan Surface submission omitted its presentation sequence.");
            var secondSequence = secondSubmission.PresentationSequence
                ?? throw new InvalidOperationException("The second Vulkan Surface submission omitted its presentation sequence.");
            Assert.That(secondSequence, Is.GreaterThan(firstSequence));
            Assert.That(firstSubmission.SurfaceEpoch, Is.EqualTo(activeSurface.SurfaceEpoch));
            Assert.That(secondSubmission.SwapchainEpoch, Is.EqualTo(activeSurface.SwapchainEpoch));
            var firstPresentation = activeSurface.WaitForPresentationAsync(firstSequence).GetAwaiter().GetResult();
            var secondPresentation = activeSurface.WaitForPresentationAsync(secondSequence).GetAwaiter().GetResult();
            Assert.That(firstPresentation.Status, Is.EqualTo(PresentationStatus.Presented));
            Assert.That(secondPresentation.Status, Is.EqualTo(PresentationStatus.Presented));

            var resized = activeSurface.ResizeAsync(new PixelSize(640, 360)).GetAwaiter().GetResult();
            Assert.That(resized.Status, Is.EqualTo(SurfaceResizeStatus.Resized));
            Assert.That(resized.SwapchainEpoch, Is.EqualTo(2));
            var suspended = activeSurface.ResizeAsync(new PixelSize(0, 0)).GetAwaiter().GetResult();
            Assert.That(suspended.Status, Is.EqualTo(SurfaceResizeStatus.Suspended));
            Assert.That(suspended.SwapchainEpoch, Is.Zero);
            var restored = activeSurface.ResizeAsync(new PixelSize(640, 360)).GetAwaiter().GetResult();
            Assert.That(restored.SwapchainEpoch, Is.EqualTo(3));

            _ = runningHost.Platform.CloseWindowAsync(window).GetAwaiter().GetResult();
            var historicalPresentation = activeSurface.WaitForPresentationAsync(firstSequence).GetAwaiter().GetResult();
            Assert.That(historicalPresentation.PresentationSequence, Is.EqualTo(firstSequence));
            Assert.That(historicalPresentation.Status, Is.EqualTo(PresentationStatus.Presented));
        }
        finally
        {
            try
            {
                renderWorld?.Dispose();
            }
            finally
            {
                host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }

    [Test]
    public void VulkanBackendPresentsXlibSurfaceOnLinux()
    {
        if (!OperatingSystem.IsLinux()
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_X11") != "1")
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            Assert.Ignore("The Vulkan Xlib witness requires an isolated Linux X11 display.");
            return;
        }

        var (options, registration, schemas) = CreateVulkanSurfaceTestSetup(PlatformBackend.X11Only);
        RunVulkanSurfaceWithDirectOwners(options, PlatformBackend.X11Only, registration, schemas);
    }

    [Test]
    public void VulkanBackendQuarantinesPresentationFenceTimeout()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("The Vulkan present-fence timeout witness uses the Linux direct-owner path.");
            return;
        }

        var platformBackend = GetInteractivePlatformBackend();

        if (platformBackend is null)
        {
            Assert.Ignore("The Vulkan present-fence timeout witness requires a Linux direct-owner display.");
            return;
        }

        var maximumRequestedWaitNanoseconds = 0UL;
        var didObserveFenceWait = false;

        VkResult OverrideFenceWaitResult(ulong waitTimeoutNanoseconds, VkResult nativeResult)
        {
            didObserveFenceWait = true;
            maximumRequestedWaitNanoseconds = Math.Max(maximumRequestedWaitNanoseconds, waitTimeoutNanoseconds);
            return nativeResult == VkResult.VK_SUCCESS ? VkResult.VK_TIMEOUT : nativeResult;
        }

        var (options, registration, schemas) = CreateVulkanSurfaceTestSetup(
            platformBackend.Value,
            surfaceFenceWaitResultOverride: OverrideFenceWaitResult,
            surfaceFenceWaitTimeoutNanoseconds: 1_000_000);
        RunVulkanSurfaceWithDirectOwners(
            options,
            platformBackend.Value,
            registration,
            schemas,
            expectPresentationFenceTimeout: true);
        Assert.That(didObserveFenceWait, Is.True);
        Assert.That(maximumRequestedWaitNanoseconds, Is.LessThanOrEqualTo(1_000_000UL));
    }

    [Test]
    public void VulkanBackendQuarantinesUnknownPresentResult()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("The Vulkan unknown-present witness uses the Linux direct-owner path.");
            return;
        }

        var platformBackend = GetInteractivePlatformBackend();

        if (platformBackend is null)
        {
            Assert.Ignore("The Vulkan unknown-present witness requires a Linux interactive display.");
            return;
        }

        var overrideNextPresent = true;

        VkResult OverridePresentResult(VkResult nativeResult)
        {
            if (overrideNextPresent && (nativeResult == VkResult.VK_SUCCESS))
            {
                overrideNextPresent = false;
                return VkResult.VK_ERROR_UNKNOWN;
            }

            return nativeResult;
        }

        var (options, registration, schemas) = CreateVulkanSurfaceTestSetup(
            platformBackend.Value,
            surfacePresentResultOverride: OverridePresentResult);
        RunVulkanSurfaceWithDirectOwners(
            options,
            platformBackend.Value,
            registration,
            schemas,
            expectUncertainPresentFailure: true);
    }

    [Test]
    [TestCase(VkResult.VK_ERROR_OUT_OF_DATE_KHR)]
    [TestCase(VkResult.VK_ERROR_SURFACE_LOST_KHR)]
    [TestCase(VkResult.VK_TIMEOUT)]
    [TestCase(VkResult.VK_NOT_READY)]
    public void VulkanBackendTracksAcquireErrorLifecycle(VkResult injectedResult)
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("The Vulkan acquire-result witness uses the Linux direct-owner path.");
            return;
        }

        var platformBackend = GetInteractivePlatformBackend();

        if (platformBackend is null)
        {
            Assert.Ignore("The Vulkan Surface acquire-result witness requires a Linux interactive display.");
            return;
        }

        var overrideNextAcquire = true;

        VkResult? OverrideAcquireResult()
        {
            if (overrideNextAcquire)
            {
                overrideNextAcquire = false;
                return injectedResult;
            }

            return null;
        }

        var (options, registration, schemas) = CreateVulkanSurfaceTestSetup(
            platformBackend.Value,
            surfaceAcquireResultOverride: OverrideAcquireResult);
        RunVulkanSurfaceWithDirectOwners(
            options,
            platformBackend.Value,
            registration,
            schemas,
            expectedFirstAcquireResult: injectedResult);
    }

    [Test]
    [TestCase(VkResult.VK_ERROR_OUT_OF_DATE_KHR, PresentationStatus.SwapchainLost)]
    [TestCase(VkResult.VK_ERROR_SURFACE_LOST_KHR, PresentationStatus.SurfaceLost)]
    public void VulkanBackendTracksPresentErrorFencesAndLifecycle(
        VkResult injectedResult,
        PresentationStatus expectedStatus)
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("The Vulkan error-result witness uses the Linux direct-owner path.");
            return;
        }

        var platformBackend = GetInteractivePlatformBackend();

        if (platformBackend is null)
        {
            Assert.Ignore("The Vulkan Surface error witness requires a Linux interactive display.");
            return;
        }

        var overrideNextPresent = true;

        VkResult OverridePresentResult(VkResult nativeResult)
        {
            if (overrideNextPresent && (nativeResult == VkResult.VK_SUCCESS))
            {
                overrideNextPresent = false;
                return injectedResult;
            }

            return nativeResult;
        }

        var (options, registration, schemas) = CreateVulkanSurfaceTestSetup(
            platformBackend.Value,
            surfacePresentResultOverride: OverridePresentResult);
        RunVulkanSurfaceWithDirectOwners(
            options,
            platformBackend.Value,
            registration,
            schemas,
            expectedFirstPresentationStatus: expectedStatus,
            expectedFirstPresentationNativeResult: injectedResult);
    }

    [Test]
    public void VulkanInteractiveEngineHostAdmitsAndRetiresWaylandProbeSurface()
    {
        if (!OperatingSystem.IsLinux()
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            Assert.Ignore("The Vulkan EngineHost probe witness requires a Wayland display.");
            return;
        }

        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var options = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WaylandOnly,
            GraphicsBackend.Vulkan,
            VulkanGraphicsBackend.CreateRegistration(),
            new AssetStoreOptions(
                packagePath,
                SHA256.HashData(packageBytes),
                CreateAssetStoreOptions().Registration)) {
            CpuWorkerCount = 0,
            GraphicsFrameSlotCount = 2,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var host = EngineHost.StartAsync(options).GetAwaiter().GetResult();
        host.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Test]
    public void VulkanObservesWaylandWsiLossWhenPrivateCompositorExits()
    {
        if (!OperatingSystem.IsLinux()
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_WAYLAND_WSI") != "1")
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            Assert.Ignore("The Wayland surface-loss witness requires a test-owned compositor.");
            return;
        }

        var swayProcessId = int.Parse(
            Environment.GetEnvironmentVariable("TERRAFX_TEST_SWAY_PID")
                ?? throw new InvalidOperationException("The private Sway process id was not provided."),
            System.Globalization.CultureInfo.InvariantCulture);
        var (options, registration, schemas) = CreateVulkanSurfaceTestSetup(PlatformBackend.WaylandOnly);
        RunVulkanSurfaceWithDirectOwners(
            options,
            PlatformBackend.WaylandOnly,
            registration,
            schemas,
            beforeFirstPresentation: (_, _) => {
                using var swayProcess = System.Diagnostics.Process.GetProcessById(swayProcessId);
                Assert.That(swayProcess.ProcessName, Is.EqualTo("sway"));
                swayProcess.Kill();
                Assert.That(swayProcess.WaitForExit(milliseconds: 5_000), Is.True);
            },
            exerciseNativeWindowReplacement: true,
            expectWaylandCompositorDisconnect: true);
    }

    [Test]
    public void VulkanReplacesXlibSurfaceAfterNativeWindowDestruction()
    {
        if (!OperatingSystem.IsLinux()
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_X11") != "1")
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            Assert.Ignore("The native Xlib surface-loss witness requires an isolated Linux X11 display.");
            return;
        }

        var (options, registration, schemas) = CreateVulkanSurfaceTestSetup(PlatformBackend.X11Only);
        RunVulkanSurfaceWithDirectOwners(
            options,
            PlatformBackend.X11Only,
            registration,
            schemas,
            beforeFirstPresentation: static (platformOwner, window) => {
                var binding = platformOwner.GetNativeWindowBinding(window);
                X11NativeSurfaceTest.DestroyWindowExternally(
                    checked((nuint)binding.NativeWindowOrSurface));
                platformOwner.Platform.Pump();
                Assert.That(
                    platformOwner.Platform.TryGetNativeWindowBinding(window, out _),
                    Is.False);
            },
            exerciseNativeWindowReplacement: true);
    }
}
