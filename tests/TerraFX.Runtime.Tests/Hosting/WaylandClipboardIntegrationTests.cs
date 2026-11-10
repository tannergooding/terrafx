// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Input;
using TerraFX.Numerics;
using TerraFX.Platform;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [Test]
    public async Task WaylandClipboardServiceTransfersTextWithNativeGestureOnIsolatedCompositor()
    {
        if (!OperatingSystem.IsLinux()
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_WAYLAND_X11") != "1")
            || (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != "wayland-m04-desktop")
            || (Environment.GetEnvironmentVariable("DISPLAY") != ":124"))
        {
            Assert.Ignore("The Wayland clipboard integration witness requires the private Xvfb-backed Weston compositor.");
            return;
        }

        var requireClipboard = Environment.GetEnvironmentVariable("TERRAFX_TEST_WAYLAND_NO_CLIPBOARD") != "1";
        var (options, _, schemas) = CreateVulkanSurfaceTestSetup(
            PlatformBackend.WaylandOnly,
            clipboardServicePolicy: requireClipboard
                ? ClipboardServicePolicy.Required
                : ClipboardServicePolicy.NotRequested);
        EngineHost? host = null;
        nint x11Display = 0;
        Process? externalClipboardSource = null;

        try
        {
            try
            {
                host = await EngineHost.StartAsync(options)
                    .WaitAsync(TimeSpan.FromSeconds(15))
                    .ConfigureAwait(false);
            }
            catch (DllNotFoundException exception)
            {
                Assert.Ignore($"A required Vulkan library is unavailable: {exception.Message}");
                return;
            }
            catch (EntryPointNotFoundException exception)
            {
                Assert.Ignore($"A required Vulkan entry point is unavailable: {exception.Message}");
                return;
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No Vulkan device satisfies Interactive Wayland presentation: {exception.Message}");
                return;
            }

            var engineHost = host
                ?? throw new InvalidOperationException("Interactive Wayland startup returned no EngineHost.");
            await TestContext.Progress.WriteLineAsync("Wayland EngineHost started.").ConfigureAwait(false);

            if (requireClipboard)
            {
                Assert.That(engineHost.Platform.ClipboardStatus, Is.EqualTo(ClipboardServiceStatus.Ready));
            }

            var windowReceipt = await engineHost.CreateWindowAsync(new WindowOptions("Wayland clipboard integration") {
                ClientSize = new Vector2(640.0f, 360.0f),
                Mode = WindowMode.BorderlessFullscreen,
                Visible = true,
            }).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(windowReceipt.Status, Is.EqualTo(WindowCreationStatus.Created));
            var window = windowReceipt.Window
                ?? throw new InvalidOperationException("Interactive Wayland window creation omitted its handle.");
            await TestContext.Progress.WriteLineAsync("Wayland window created.").ConfigureAwait(false);
            Surface? surface = null;
            await engineHost.InvokeAsync(_ => surface = engineHost.GetSurface(window))
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            var activeSurface = surface
                ?? throw new InvalidOperationException("Interactive Wayland window creation omitted its Surface.");
            var targetExtent = activeSurface.ClientPixelSize;
            await TestContext.Progress.WriteLineAsync("Wayland Surface acquired.").ConfigureAwait(false);

            using var renderSnapshot = CreateRenderSnapshot(activeSurface);

            var graph = engineHost.Renderer.BuildGraph(renderSnapshot, activeSurface);
            var source = graph.CreateTransientTexture(new TextureDescription(
                TextureDimension.Dimension2D,
                targetExtent.Width,
                targetExtent.Height,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                format: activeSurface.PresentationSourceFormat,
                usage: TextureUsage.ColorAttachment | TextureUsage.CopySource,
                debugName: "wayland-clipboard-surface"));
            var pass = graph.AddRasterPass(new RenderPassDescription(
                "wayland-clipboard-surface-clear",
                order: 0,
                predecessors: [],
                hasExternalSideEffects: false));
            var output = graph.WriteColorAttachment(
                pass,
                source,
                new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
                AttachmentLoadAction.Clear,
                AttachmentStoreAction.Store,
                renderSnapshot.Cameras[0].ClearColor);
            graph.SetPresentationSource(output);
            var compileResponse = graph.Compile();
            Assert.That(compileResponse.Status, Is.EqualTo(RenderGraphCompileStatus.Compiled));
            var plan = compileResponse.Plan
                ?? throw new InvalidOperationException("The compiled Wayland presentation omitted its graph plan.");
            var submission = await engineHost.Renderer.SubmitAsync(renderSnapshot, plan)
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            var presentationSequence = submission.PresentationSequence
                ?? throw new InvalidOperationException("The Wayland submission omitted its presentation sequence.");
            var presentation = await activeSurface.WaitForPresentationAsync(presentationSequence)
                .WaitAsync(TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);
            Assert.That(presentation.Status, Is.EqualTo(PresentationStatus.Presented));
            await TestContext.Progress.WriteLineAsync("Wayland presentation observed.").ConfigureAwait(false);

            if (!requireClipboard)
            {
                await TestContext.Progress.WriteLineAsync("Skipping clipboard operations.").ConfigureAwait(false);
                return;
            }

            x11Display = WaylandClipboardTestSupport.OpenDisplay();
            var clipboard = engineHost.Platform.ClipboardText
                ?? throw new InvalidOperationException("The Wayland clipboard service was not initialized.");
            const string RuntimeText = "private Wayland service write";
            var writeGesture = WaylandClipboardTestSupport.CaptureWaylandPointerGesture(engineHost, window, x11Display);
            Assert.That(clipboard.TryCreateUserGestureToken(writeGesture, out var writeToken), Is.True);
            var writeResponse = await clipboard.WriteTextAsync(
                    new ClipboardWriteRequest(window, RuntimeText, maximumUtf8Bytes: 1024, gestureToken: writeToken))
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(writeResponse.Status, Is.EqualTo(ClipboardStatus.Completed));
            await TestContext.Progress.WriteLineAsync("Wayland service write completed.").ConfigureAwait(false);

            var pastedText = WaylandClipboardTestSupport.RunWaylandClipboardClient("--no-newline");
            Assert.That(pastedText, Is.EqualTo(RuntimeText));

            const string ExternalText = "private Wayland service read";
            var clipboardSource = WaylandClipboardTestSupport.StartWaylandClipboardSource(ExternalText);
            externalClipboardSource = clipboardSource;
            await Task.Delay(100).ConfigureAwait(false);
            var readGesture = WaylandClipboardTestSupport.CaptureWaylandPointerGesture(engineHost, window, x11Display);
            Assert.That(clipboard.TryCreateUserGestureToken(readGesture, out var readToken), Is.True);
            var readResponse = await clipboard.ReadTextAsync(
                    new ClipboardReadRequest(window, maximumUtf8Bytes: 1024, gestureToken: readToken))
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(readResponse.Status, Is.EqualTo(ClipboardStatus.Completed));
            Assert.That(readResponse.Text, Is.EqualTo(ExternalText));
            await TestContext.Progress.WriteLineAsync("Wayland service read completed.").ConfigureAwait(false);
            await clipboardSource.WaitForExitAsync()
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            Assert.That(clipboardSource.HasExited, Is.True);
        }
        finally
        {
            if (externalClipboardSource is { HasExited: false } clipboardSource)
            {
                clipboardSource.Kill(entireProcessTree: true);
                await clipboardSource.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }

            externalClipboardSource?.Dispose();

            if (x11Display != 0)
            {
                WaylandClipboardTestSupport.CloseDisplay(x11Display);
            }

            if (host is not null)
            {
                await TestContext.Progress.WriteLineAsync("Disposing Wayland EngineHost.").ConfigureAwait(false);
                await host.DisposeAsync().AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
                await TestContext.Progress.WriteLineAsync("Wayland EngineHost disposed.").ConfigureAwait(false);
            }
        }

        RenderSnapshot CreateRenderSnapshot(Surface surface)
        {
            var registrationBuilder = new WorldRegistrationBuilder(schemas);
            _ = registrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = registrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
            using var renderWorld = World.Create(new WorldOptions(registrationBuilder.Build()) {
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
            var targetExtent = surface.ClientPixelSize;
            var extraction = Renderer.CreateHeadless().Extract(
                renderWorld,
                new RenderExtractionOptions3D(publication.Frame3, targetExtent, [cameraId]));
            Assert.That(extraction.Status, Is.EqualTo(RenderExtractionStatus.Extracted));
            return extraction.Snapshot
                ?? throw new InvalidOperationException("The successful Wayland extraction omitted its snapshot.");
        }
    }
}
