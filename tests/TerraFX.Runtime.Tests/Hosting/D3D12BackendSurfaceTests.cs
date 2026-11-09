// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Content.Tools;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;
using TerraFX.Hosting;
using TerraFX.Platform;
using TerraFX.Numerics;
using TerraFX.Rendering;
using TerraFX.Runtime.Tests.Scenes;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public void D3D12BackendCreatesAndCompilesWin32FlipDiscardSurface()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The D3D12 surface witness requires Windows 11.");
            return;
        }

        var registration = D3D12GraphicsBackend.CreateRegistration();
        var options = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            registration,
            CreateAssetStoreOptions()) {
            CpuWorkerCount = 0,
            GraphicsFrameSlotCount = 2,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var platformOwner = PlatformHostOwner.Create(options);
        GraphicsDevice? graphicsDevice = null;
        Surface? surface = null;

        try
        {
            var window = platformOwner.CreateWindow(new WindowOptions("D3D12 surface witness") {
                ClientSize = new Vector2(640, 360),
                Visible = false,
            });
            var binding = platformOwner.GetGraphicsNativeSurfaceBinding(window, surfaceEpoch: 1);

            try
            {
                graphicsDevice = GraphicsDevice.CreateAsync(
                    platformOwner.OwnerId,
                    registration,
                    GraphicsDeviceProfile.Interactive,
                    options.GraphicsFrameSlotCount,
                    GraphicsPlatformKind.Win32,
                    CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No D3D12 adapter satisfies Interactive presentation: {exception.Message}");
                return;
            }

            surface = graphicsDevice.CreateSurfaceAsync(binding, new PixelSize(640, 360), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var activeSurface = surface
                ?? throw new InvalidOperationException("The D3D12 backend returned no Surface.");

            Assert.That(activeSurface.ClientPixelSize, Is.EqualTo(new PixelSize(640, 360)));
            Assert.That(activeSurface.Capabilities.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(activeSurface.Capabilities.SwapchainEpoch, Is.EqualTo(1));
            Assert.That(activeSurface.Capabilities.PresentModes, Does.Contain(GraphicsPresentMode.Fifo));
            Assert.That(
                activeSurface.Capabilities.PresentModes.Contains(GraphicsPresentMode.Immediate),
                Is.EqualTo(activeSurface.Capabilities.SupportsTearing));
            Assert.That(activeSurface.Capabilities.Formats, Has.Length.EqualTo(1));
            Assert.That(activeSurface.Capabilities.Formats[0].Format, Is.EqualTo(activeSurface.ColorFormat));
            Assert.That(activeSurface.ColorFormat, Is.AnyOf(TextureFormat.Rgba8Unorm, TextureFormat.Bgra8Unorm));
            Assert.That(activeSurface.PresentationSourceFormat, Is.AnyOf(TextureFormat.Rgba8Srgb, TextureFormat.Bgra8Srgb));
            Assert.That(
                SurfaceFormatCompatibility.IsSrgbPresentationSourceCompatible(
                    activeSurface.PresentationSourceFormat,
                    activeSurface.ColorFormat),
                Is.True);

            RenderGraph CreateSurfaceGraph(string name)
            {
                var graph = new RenderGraph(new RenderGraphOptions(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    0,
                    RenderFrameDimension.TwoDimensional,
                    SpatialProfileId.Default,
                    1,
                    [Guid.NewGuid()],
                    new PixelSize(640, 360),
                    activeSurface));
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
                    $"{name}-source",
                    order: 0,
                    predecessors: [],
                    hasExternalSideEffects: false));
                var output = graph.WriteColorAttachment(
                    pass,
                    source,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                graph.SetPresentationSource(output);
                return graph;
            }

            var surfaceGraph = CreateSurfaceGraph("d3d12-surface-output");
            _ = Assert.Throws<InvalidOperationException>(() => _ = surfaceGraph.OffscreenColorTargetVersion);
            var surfaceGraphResponse = surfaceGraph.Compile();
            Assert.That(surfaceGraphResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
            var surfacePlan = surfaceGraphResponse.Plan
                ?? throw new InvalidOperationException("The Surface graph compile returned no plan.");
            Assert.That(surfacePlan.OffscreenColorTarget, Is.Null);
            Assert.That(surfacePlan.SurfaceTarget, Is.SameAs(activeSurface));
            Assert.That(surfacePlan.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(surfacePlan.SwapchainEpoch, Is.EqualTo(1));
            Assert.That(surfacePlan.OrderedPassKinds[^1], Is.EqualTo(RenderGraphPassKind.Presentation));

            var staleSurfaceGraph = CreateSurfaceGraph("d3d12-stale-surface-output");
            var resizedReceipt = activeSurface.ResizeAsync(new PixelSize(800, 600)).GetAwaiter().GetResult();

            Assert.That(activeSurface.ClientPixelSize, Is.EqualTo(new PixelSize(800, 600)));
            Assert.That(resizedReceipt.Status, Is.EqualTo(SurfaceResizeStatus.Resized));
            Assert.That(resizedReceipt.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(resizedReceipt.SwapchainEpoch, Is.EqualTo(2));
            var staleGraphResponse = staleSurfaceGraph.Compile();
            Assert.That(staleGraphResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Rejected));
            Assert.That(staleGraphResponse.Diagnostics, Has.Length.EqualTo(1));
            Assert.That(staleGraphResponse.Diagnostics[0].Code, Is.EqualTo("render-graph.presentation-source-invalid"));

            var suspendedReceipt = activeSurface.ResizeAsync(new PixelSize(0, 0)).GetAwaiter().GetResult();

            Assert.That(activeSurface.ClientPixelSize, Is.EqualTo(new PixelSize(0, 0)));
            Assert.That(suspendedReceipt.Status, Is.EqualTo(SurfaceResizeStatus.Suspended));
            Assert.That(suspendedReceipt.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(suspendedReceipt.SwapchainEpoch, Is.Zero);

            var restoredReceipt = activeSurface.ResizeAsync(new PixelSize(1024, 768)).GetAwaiter().GetResult();

            Assert.That(activeSurface.ClientPixelSize, Is.EqualTo(new PixelSize(1024, 768)));
            Assert.That(restoredReceipt.Status, Is.EqualTo(SurfaceResizeStatus.Resized));
            Assert.That(restoredReceipt.SurfaceEpoch, Is.EqualTo(1));
            Assert.That(restoredReceipt.SwapchainEpoch, Is.EqualTo(3));
        }
        finally
        {
            try
            {
                if ((graphicsDevice is not null) && (surface is not null))
                {
                    graphicsDevice.RetireSurfaceAsync(surface).GetAwaiter().GetResult();
                }
            }
            finally
            {
                try
                {
                    graphicsDevice?.ShutdownAsync().GetAwaiter().GetResult();
                }
                finally
                {
                    platformOwner.DisposeFromOwner();
                }
            }
        }
    }

    [TestCase(false, false, GraphicsFailurePolicy.StopHost, true, false)]
    [TestCase(true, false, GraphicsFailurePolicy.StopHost, true, false)]
    [TestCase(false, true, GraphicsFailurePolicy.StopHost, true, false)]
    [TestCase(false, true, GraphicsFailurePolicy.SuspendPresentation, true, false)]
    [TestCase(false, false, GraphicsFailurePolicy.StopHost, false, false)]
    [TestCase(false, false, GraphicsFailurePolicy.SuspendPresentation, true, true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2025:Ensure tasks using IDisposable instances complete before the instances are disposed",
        Justification = "The synchronous graph submission and presentation wait complete before the snapshot is disposed.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The EngineHost is disposed in the enclosing finally block after native window closure.")]
    public void EngineHostInteractiveStartupMapsSurfaceBeforeWindowReceipt(
        bool injectOcclusionResult,
        bool injectDeviceRemovedResult,
        GraphicsFailurePolicy graphicsFailurePolicy,
        bool windowVisible,
        bool injectUnknownPresentResult)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            Assert.Ignore("The Interactive EngineHost witness requires Windows 11.");
            return;
        }

        const int DxgiStatusOccludedForTesting = 0x087A0001;
        const int DxgiErrorDriverInternalErrorForTesting = unchecked((int)0x887A0020);
        var overrideNextPresent = injectOcclusionResult || injectDeviceRemovedResult || injectUnknownPresentResult;
        var presentResultCallCount = 0;

        int? OverridePresentResult()
        {
            if (injectUnknownPresentResult)
            {
                presentResultCallCount++;
                return (presentResultCallCount == 1) ? null : DxgiErrorDriverInternalErrorForTesting;
            }

            if (overrideNextPresent)
            {
                overrideNextPresent = false;

                if (injectDeviceRemovedResult)
                {
                    return unchecked((int)0x887A0005);
                }

                return injectUnknownPresentResult
                    ? DxgiErrorDriverInternalErrorForTesting
                    : DxgiStatusOccludedForTesting;
            }

            return null;
        }

        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var packagePath = SceneAssetStoreTests.Package.WritePackage(packageBytes);
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);
        var assetStoreRegistration = CreateAssetStoreOptions().Registration;
        var assetStoreOptions = new AssetStoreOptions(
            packagePath,
            SHA256.HashData(packageBytes),
            assetStoreRegistration);
        var registration = (injectOcclusionResult || injectDeviceRemovedResult || injectUnknownPresentResult)
            ? new GraphicsBackendRegistration(new D3D12GraphicsBackend.D3D12BackendFactory(
                surfacePresentResultOverride: OverridePresentResult))
            : D3D12GraphicsBackend.CreateRegistration();
        var options = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            registration,
            assetStoreOptions) {
            CpuWorkerCount = 0,
            GraphicsFrameSlotCount = 2,
            GraphicsFailurePolicy = graphicsFailurePolicy,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        EngineHost? host = null;
        World? hostedPolicyWorld = null;
        World? renderWorld = null;
        Exception? testFailure = null;

        try
        {
            try
            {
                host = EngineHost.StartAsync(options).GetAwaiter().GetResult();
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No D3D12 adapter satisfies Interactive hosting: {exception.Message}");
                return;
            }

            var runningHost = host
                ?? throw new InvalidOperationException("Interactive EngineHost startup returned no host.");
            var receipt = runningHost.CreateWindowAsync(new WindowOptions("Interactive surface witness") {
                ClientSize = new Vector2(640, 360),
                Visible = windowVisible,
            }).GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(WindowCreationStatus.Created));
            var window = receipt.Window
                ?? throw new InvalidOperationException("Interactive window creation returned no WindowHandle.");
            _ = Assert.Throws<ArgumentException>(() => runningHost.GetSurface(default));
            _ = Assert.Throws<InvalidOperationException>(() => runningHost.GetSurface(window));
            Surface? firstSurface = null;
            Surface? secondSurface = null;
            runningHost.InvokeAsync(_ => firstSurface = runningHost.GetSurface(window)).GetAwaiter().GetResult();
            runningHost.InvokeAsync(_ => secondSurface = runningHost.GetSurface(window)).GetAwaiter().GetResult();
            Assert.That(secondSurface, Is.SameAs(firstSurface));
            var surface = secondSurface
                ?? throw new InvalidOperationException("EngineHost.GetSurface returned no Surface.");

            var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            var worldRegistration = worldRegistrationBuilder.Build();

            if (graphicsFailurePolicy is GraphicsFailurePolicy.SuspendPresentation)
            {
                var hostedWorldRegistration = new WorldRegistrationBuilder(assetStoreRegistration.Schemas).Build();
                runningHost.InvokeAsync(context =>
                    hostedPolicyWorld = context.CreateWorld(new WorldOptions(hostedWorldRegistration) {
                        CpuWorkerCount = 0,
                        InitialReplayLineageId = Guid.NewGuid(),
                        WorldGuid = Guid.NewGuid(),
                    })).GetAwaiter().GetResult();
            }

            renderWorld = World.Create(new WorldOptions(worldRegistration) {
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
                ?? throw new InvalidOperationException("The successful extraction omitted its render snapshot.");

            RenderGraphPlan CreateSurfaceGraphPlan(string name)
            {
                var graph = runningHost.Renderer.BuildGraph(renderSnapshot, surface);
                var graphSource = graph.CreateTransientTexture(new TextureDescription(
                    TextureDimension.Dimension2D,
                    640,
                    360,
                    depth: 1,
                    arrayLayers: 1,
                    mipLevels: 1,
                    format: surface.PresentationSourceFormat,
                    usage: TextureUsage.ColorAttachment | TextureUsage.CopySource,
                    debugName: name));
                var graphPass = graph.AddRasterPass(new RenderPassDescription(
                    $"{name}-clear",
                    order: 0,
                    predecessors: [],
                    hasExternalSideEffects: false));
                var graphOutput = graph.WriteColorAttachment(
                    graphPass,
                    graphSource,
                    new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                    AttachmentLoadAction.Clear,
                    AttachmentStoreAction.Store,
                    new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                graph.SetPresentationSource(graphOutput);
                var graphResponse = graph.Compile();
                Assert.That(graphResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
                var graphPlan = graphResponse.Plan
                    ?? throw new InvalidOperationException("The Interactive Surface graph returned no plan.");
                Assert.That(graphPlan.SurfaceEpoch, Is.EqualTo(surface.SurfaceEpoch));
                Assert.That(graphPlan.SwapchainEpoch, Is.EqualTo(surface.SwapchainEpoch));
                return graphPlan;
            }

            var firstSubmission = runningHost.Renderer.SubmitAsync(
                renderSnapshot,
                CreateSurfaceGraphPlan("interactive-surface-first-frame"))
                .GetAwaiter()
                .GetResult();
            var firstSequence = firstSubmission.PresentationSequence
                ?? throw new InvalidOperationException("The first Surface submission omitted its presentation sequence.");
            using var canceledPresentationWait = new CancellationTokenSource();
            canceledPresentationWait.Cancel();
            _ = Assert.Throws<OperationCanceledException>(
                () => surface.WaitForPresentationAsync(firstSequence, canceledPresentationWait.Token).GetAwaiter().GetResult());

            if (injectUnknownPresentResult)
            {
                var nativeFailure = Assert.Catch<Exception>(
                    () => surface.WaitForPresentationAsync(firstSequence).GetAwaiter().GetResult());
                Assert.That(nativeFailure?.HResult, Is.EqualTo(DxgiErrorDriverInternalErrorForTesting));
                Assert.That(surface.IsQuarantined, Is.True);
                Assert.That(runningHost.GraphicsDevice?.Status, Is.EqualTo(GraphicsDeviceStatus.Lost));
                Assert.That(runningHost.Completion.IsCompleted, Is.False);
                var diagnostics = runningHost.Diagnostics.GetSnapshot(
                    new DiagnosticQuery(32, DiagnosticSeverity.Error, worldGuid: null, minimumTick: null));
                var deviceLostDiagnostic = diagnostics.Records.Single(
                    static diagnostic => diagnostic.Code == "graphics-device-lost");
                Assert.That(deviceLostDiagnostic.NativeResult, Is.EqualTo(DxgiErrorDriverInternalErrorForTesting));
                _ = Assert.Throws<ObjectDisposedException>(
                    () => surface.ResizeAsync(new PixelSize(800, 600)).GetAwaiter().GetResult());
                renderSnapshot.Dispose();
                renderWorld.Dispose();
                renderWorld = null;
                var shutdownFailure = Assert.Catch<AggregateException>(
                    () => runningHost.DisposeAsync().AsTask().GetAwaiter().GetResult());
                host = null;
                Assert.That(
                    shutdownFailure?.Flatten().InnerExceptions.Select(static exception => exception.HResult),
                    Does.Contain(DxgiErrorDriverInternalErrorForTesting));
                return;
            }

            if (injectOcclusionResult || injectDeviceRemovedResult)
            {
                var injectedPresentation = surface.WaitForPresentationAsync(firstSequence).GetAwaiter().GetResult();
                var expectedStatus = injectDeviceRemovedResult
                    ? PresentationStatus.DeviceLost
                    : PresentationStatus.Occluded;
                Assert.That(injectedPresentation.Status, Is.EqualTo(expectedStatus));

                if (injectDeviceRemovedResult)
                {
                    Assert.That(injectedPresentation.Diagnostic?.Code, Is.EqualTo("graphics.presentation.device-lost"));
                    Assert.That(injectedPresentation.Diagnostic?.NativeResult, Is.EqualTo(unchecked((int)0x887A0005)));
                    Assert.That(runningHost.GraphicsDevice?.Status, Is.EqualTo(GraphicsDeviceStatus.Lost));
                    var diagnostics = runningHost.Diagnostics.GetSnapshot(
                        new DiagnosticQuery(32, DiagnosticSeverity.Error, worldGuid: null, minimumTick: null));
                    var deviceLostDiagnostic = diagnostics.Records.Single(
                        static diagnostic => diagnostic.Code == "graphics-device-lost");
                    Assert.That(deviceLostDiagnostic.NativeResult, Is.EqualTo(unchecked((int)0x887A0005)));

                    if (graphicsFailurePolicy is GraphicsFailurePolicy.StopHost)
                    {
                        runningHost.Completion.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                        return;
                    }

                    Assert.That(runningHost.Completion.IsCompleted, Is.False);
                    long tickBeforeWait = 0;
                    runningHost.InvokeAsync(_ => tickBeforeWait = hostedPolicyWorld?.NextTickIndex
                        ?? throw new InvalidOperationException("The suspended-presentation witness has no hosted World."))
                        .GetAwaiter()
                        .GetResult();
                    Thread.Sleep(100);
                    long tickAfterWait = 0;
                    runningHost.InvokeAsync(_ => tickAfterWait = hostedPolicyWorld?.NextTickIndex
                        ?? throw new InvalidOperationException("The suspended-presentation witness has no hosted World."))
                        .GetAwaiter()
                        .GetResult();
                    Assert.That(tickAfterWait, Is.GreaterThan(tickBeforeWait));
                    _ = Assert.Throws<InvalidOperationException>(
                        () => runningHost.Renderer.SubmitAsync(
                            renderSnapshot,
                            CreateSurfaceGraphPlan("interactive-surface-after-device-loss"))
                            .GetAwaiter()
                            .GetResult());
                }

                renderSnapshot.Dispose();
                renderWorld.Dispose();
                renderWorld = null;
                var closeAfterTerminalPresentation = runningHost.Platform.CloseWindowAsync(window).GetAwaiter().GetResult();
                Assert.That(closeAfterTerminalPresentation.Status, Is.EqualTo(WindowCommandStatus.Applied));
                var terminalPresentationHistory = surface.WaitForPresentationAsync(firstSequence).GetAwaiter().GetResult();
                Assert.That(terminalPresentationHistory.Status, Is.EqualTo(expectedStatus));
                runningHost.StopAsync().GetAwaiter().GetResult();
                return;
            }

            var secondSubmission = runningHost.Renderer.SubmitAsync(
                renderSnapshot,
                CreateSurfaceGraphPlan("interactive-surface-second-frame"))
                .GetAwaiter()
                .GetResult();
            var secondSequence = secondSubmission.PresentationSequence
                ?? throw new InvalidOperationException("The second Surface submission omitted its presentation sequence.");
            Assert.That(secondSequence, Is.GreaterThan(firstSequence));
            Assert.That(secondSubmission.SurfaceEpoch, Is.EqualTo(surface.SurfaceEpoch));
            Assert.That(secondSubmission.SwapchainEpoch, Is.EqualTo(surface.SwapchainEpoch));

            var secondPresentation = surface.WaitForPresentationAsync(
                secondSequence)
                .GetAwaiter()
                .GetResult();
            var firstPresentation = surface.WaitForPresentationAsync(
                firstSequence)
                .GetAwaiter()
                .GetResult();
            Assert.That(firstPresentation.PresentationSequence, Is.EqualTo(firstSubmission.PresentationSequence));
            Assert.That(secondPresentation.PresentationSequence, Is.EqualTo(secondSubmission.PresentationSequence));
            Assert.That(firstPresentation.Status, Is.AnyOf(PresentationStatus.Presented, PresentationStatus.Occluded));
            Assert.That(secondPresentation.Status, Is.AnyOf(PresentationStatus.Presented, PresentationStatus.Occluded));

            if (!windowVisible)
            {
                Assert.That(firstPresentation.Status, Is.EqualTo(PresentationStatus.Occluded));
                Assert.That(secondPresentation.Status, Is.EqualTo(PresentationStatus.Occluded));
            }

            renderSnapshot.Dispose();
            renderWorld.Dispose();
            renderWorld = null;

            var resizeReceipt = surface.ResizeAsync(new PixelSize(800, 600)).GetAwaiter().GetResult();
            Assert.That(resizeReceipt.Status, Is.EqualTo(SurfaceResizeStatus.Resized));
            Assert.That(resizeReceipt.SwapchainEpoch, Is.EqualTo(2));

            var closeReceipt = runningHost.Platform.CloseWindowAsync(window).GetAwaiter().GetResult();
            Assert.That(closeReceipt.Status, Is.EqualTo(WindowCommandStatus.Applied));
            var retiredPresentation = surface.WaitForPresentationAsync(
                firstSequence)
                .GetAwaiter()
                .GetResult();
            Assert.That(retiredPresentation.PresentationSequence, Is.EqualTo(firstSequence));
            _ = Assert.Throws<InvalidOperationException>(
                () => runningHost.InvokeAsync(_ => runningHost.GetSurface(window)).GetAwaiter().GetResult());

            var unavailableMonitorReceipt = runningHost.CreateWindowAsync(new WindowOptions("stale monitor witness") {
                Monitor = new MonitorHandle(runningHost.Platform.OwnerId, uint.MaxValue, generation: 1),
                Visible = false,
            }).GetAwaiter().GetResult();
            Assert.That(unavailableMonitorReceipt.Status, Is.EqualTo(WindowCreationStatus.Rejected));
            Assert.That(unavailableMonitorReceipt.Window, Is.Null);
            Assert.That(unavailableMonitorReceipt.Rejection?.Code, Is.EqualTo("MonitorUnavailable"));
            runningHost.StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                try
                {
                    renderWorld?.Dispose();
                }
                finally
                {
                    host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }

                File.Delete(packagePath);
            }
            catch (Exception cleanupException) when (testFailure is not null)
            {
                TestContext.Out.WriteLine($"Surface witness cleanup also failed: {cleanupException}");
            }
        }
    }
}
