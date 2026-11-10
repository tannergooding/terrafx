// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Graphics.Vulkan;
using TerraFX.Hosting;
using TerraFX.Numerics;
using TerraFX.Platform;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Vulkan.Consumer;

internal static class Program
{
    private const int SceneAssetId = 3;

    private static AssetStoreRegistration CreateAssetStoreRegistration(CompiledContentSchemaRegistry schemas)
    {
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(registrationBuilder);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);
        return registrationBuilder.Build();
    }

    private static WorldPersistenceMetadata CreatePersistenceMetadata()
    {
        var digest = new byte[32];
        var application = new WorldPersistenceApplication(
            new Guid("20000000-0000-0000-0000-000000000001"),
            majorVersion: 1,
            minorVersion: 0,
            modules: []);
        var operatingSystem = OperatingSystem.IsWindows()
            ? WorldPersistenceOperatingSystem.Windows
            : OperatingSystem.IsLinux()
                ? WorldPersistenceOperatingSystem.Linux
                : throw new PlatformNotSupportedException("The State2 runtime profile supports Windows and Linux only.");
        var codeGeneration = RuntimeFeature.IsDynamicCodeSupported
            ? WorldPersistenceCodeGeneration.Jit
            : WorldPersistenceCodeGeneration.NativeAot;
        var runtimeProfile = new WorldPersistenceRuntimeProfile(
            digest,
            digest,
            digest,
            operatingSystem,
            codeGeneration,
            WorldPersistenceInstructionSet.None,
            digest,
            WorldPersistenceFmaPolicy.Disallowed,
            maximumSimdBits: 0,
            modules: []);
        return new WorldPersistenceMetadata(application, runtimeProfile);
    }

    private static CompiledContentSchemaRegistry CreateSchemaRegistry()
        => new([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
            .. RenderingSchemaCatalogue.Schemas,
        ]);

    private static WorldRegistration CreateWorldRegistration(CompiledContentSchemaRegistry schemas)
    {
        var registrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = registrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        _ = registrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
        return registrationBuilder.Build();
    }

    private static int Main(string[] args)
        => RunAsync(args).GetAwaiter().GetResult();

    private static Task<int> RunAsync(string[] args)
        => (args.Length > 0) && (args[0] is "offscreen")
            ? RunOffscreenAsync(args)
            : RunInteractiveAsync(args);

    private static async Task<int> RunInteractiveAsync(string[] args)
    {
        if ((args.Length != 4)
            || (args[0] is not "interactive")
            || (args[1] is not "wayland" and not "x11" and not "win32")
            || !Path.IsPathFullyQualified(args[2])
            || !Guid.TryParseExact(args[3], "N", out var rootEntityId))
        {
            throw new ArgumentException("Expected: interactive <wayland|x11|win32> <absolute-package-path> <root-entity-guid-N>.", nameof(args));
        }

        var schemas = CreateSchemaRegistry();
        var packageBytes = await File.ReadAllBytesAsync(args[2]).ConfigureAwait(false);
        var assetStoreOptions = new AssetStoreOptions(
            args[2],
            SHA256.HashData(packageBytes),
            CreateAssetStoreRegistration(schemas));
        var platformBackend = args[1] switch {
            "wayland" => PlatformBackend.WaylandOnly,
            "x11" => PlatformBackend.X11Only,
            "win32" => PlatformBackend.WindowsNative,
            _ => throw new ArgumentOutOfRangeException(nameof(args)),
        };
        var options = new EngineHostOptions(
            HostProfile.Interactive,
            platformBackend,
            GraphicsBackend.Vulkan,
            VulkanGraphicsBackend.CreateRegistration(),
            assetStoreOptions) {
            CpuWorkerCount = 0,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };

        var host = await EngineHost.StartAsync(options).ConfigureAwait(false);
        await using var hostDisposal = host.ConfigureAwait(false);

        if (OperatingSystem.IsWindows() && !RuntimeFeature.IsDynamicCodeSupported)
        {
            VerifyGameInputUnavailableDiagnostic(host);
        }

        var windowReceipt = await host.CreateWindowAsync(new WindowOptions("TerraFX Vulkan NativeAOT witness") {
            ClientSize = new Vector2(320.0f, 180.0f),
            Visible = false,
        }).ConfigureAwait(false);

        if (windowReceipt.Status is not WindowCreationStatus.Created)
        {
            throw new InvalidOperationException($"Vulkan window creation returned {windowReceipt.Status}.");
        }

        var window = windowReceipt.Window
            ?? throw new InvalidOperationException("Vulkan window creation omitted its WindowHandle.");
        var secondWindowReceipt = await host.CreateWindowAsync(new WindowOptions("TerraFX Vulkan NativeAOT witness 2") {
            ClientSize = new Vector2(320.0f, 180.0f),
            Visible = false,
        }).ConfigureAwait(false);

        if (secondWindowReceipt.Status is not WindowCreationStatus.Created)
        {
            throw new InvalidOperationException($"Vulkan second window creation returned {secondWindowReceipt.Status}.");
        }

        var secondWindow = secondWindowReceipt.Window
            ?? throw new InvalidOperationException("Vulkan second window creation omitted its WindowHandle.");

        if (secondWindow == window)
        {
            throw new InvalidOperationException("Vulkan Interactive window creation reused a live WindowHandle.");
        }

        var worldRegistration = CreateWorldRegistration(schemas);
        World? world = null;
        SceneLoader? loader = null;
        long completedTick = -1;

        await host.InvokeAsync(context => {
            world = context.CreateWorld(new WorldOptions(worldRegistration) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = Guid.NewGuid(),
            });
            loader = new SceneLoader(world, host.Assets);
        }).ConfigureAwait(false);

        using var prepared = await loader!.PrepareAsync(
            new AssetId(SceneAssetId),
            new SceneLoadOptions()).ConfigureAwait(false);
        var activation = await loader.ActivateAsync(prepared)
            .WaitAsync(TimeSpan.FromSeconds(10))
            .ConfigureAwait(false);
        var sceneInstance = activation.Instance
            ?? throw new InvalidOperationException("Vulkan Scene activation returned no SceneInstance.");

        if (activation.Status is not SceneActivationStatus.Activated)
        {
            throw new InvalidOperationException($"Vulkan Scene activation returned {activation.Status}.");
        }

        await host.InvokeAsync(_ => completedTick = world!.Step().CompletedTick).ConfigureAwait(false);

        Surface? surface = null;
        PixelSize clientPixelSize = default;
        await host.InvokeAsync(_ => {
            surface = host.GetSurface(window);
            clientPixelSize = host.Platform.GetMetrics(window).ClientPixelSize;
        }).ConfigureAwait(false);
        var activeSurface = surface
            ?? throw new InvalidOperationException("Vulkan Interactive host published no Surface.");
        Surface? secondSurface = null;
        PixelSize secondClientPixelSize = default;
        await host.InvokeAsync(_ => {
            secondSurface = host.GetSurface(secondWindow);
            secondClientPixelSize = host.Platform.GetMetrics(secondWindow).ClientPixelSize;
        }).ConfigureAwait(false);
        var activeSecondSurface = secondSurface
            ?? throw new InvalidOperationException("Vulkan Interactive host published no second Surface.");

        if ((clientPixelSize.Width <= 0)
            || (clientPixelSize.Height <= 0)
            || (secondClientPixelSize.Width <= 0)
            || (secondClientPixelSize.Height <= 0))
        {
            throw new InvalidOperationException("A Vulkan Interactive window has no positive client extent.");
        }

        RenderSnapshot? snapshot = null;
        await host.InvokeAsync(_ => {
            if (!sceneInstance.TryGetEntity(rootEntityId, out var cameraEntity))
            {
                throw new InvalidOperationException("The activated Scene did not contain the expected camera Entity.");
            }

            var cameraId = world!.GetEntityId(cameraEntity);
            var extraction = host.Renderer.Extract(
                world,
                new RenderExtractionOptions3D(world.CurrentFrame3, clientPixelSize, [cameraId]));

            if (extraction.Status is not RenderExtractionStatus.Extracted)
            {
                throw new InvalidOperationException(
                    $"Vulkan snapshot extraction returned {extraction.Status}: {string.Join("; ", extraction.Diagnostics)}");
            }

            snapshot = extraction.Snapshot
                ?? throw new InvalidOperationException("Vulkan snapshot extraction omitted its RenderSnapshot.");
        }).ConfigureAwait(false);

        using var renderSnapshot = snapshot
            ?? throw new InvalidOperationException("Vulkan host produced no RenderSnapshot.");

        RenderGraphPlan CreateSurfacePlan(
            RenderSnapshot targetSnapshot,
            Surface targetSurface,
            PixelSize targetPixelSize,
            string name)
        {
            var graph = host.Renderer.BuildGraph(targetSnapshot, targetSurface);
            var source = graph.CreateTransientTexture(new TextureDescription(
                TextureDimension.Dimension2D,
                targetPixelSize.Width,
                targetPixelSize.Height,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                targetSurface.PresentationSourceFormat,
                TextureUsage.ColorAttachment | TextureUsage.CopySource,
                $"{name}-source"));
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
                new Vector4(0.125f, 0.25f, 0.5f, 1.0f));
            graph.SetPresentationSource(output);
            var compile = graph.Compile();

            if (compile.Status is not RenderGraphCompileStatus.Compiled)
            {
                throw new InvalidOperationException(
                    $"Vulkan Surface graph compilation returned {compile.Status}: {string.Join("; ", compile.Diagnostics)}");
            }

            return compile.Plan
                ?? throw new InvalidOperationException("Vulkan Surface graph compilation omitted its plan.");
        }

        async Task<PresentationStatus> PresentAsync(
            RenderSnapshot targetSnapshot,
            Surface targetSurface,
            PixelSize targetPixelSize,
            string name)
        {
            var submission = await host.Renderer.SubmitAsync(
                targetSnapshot,
                CreateSurfacePlan(targetSnapshot, targetSurface, targetPixelSize, name))
                .WaitAsync(TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);
            var presentationSequence = submission.PresentationSequence
                ?? throw new InvalidOperationException("Vulkan Interactive submission omitted its presentation sequence.");
            var presentation = await targetSurface.WaitForPresentationAsync(presentationSequence)
                .WaitAsync(TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);

            if (presentation.Status is not (PresentationStatus.Presented or PresentationStatus.Occluded))
            {
                throw new InvalidOperationException(
                    $"Vulkan Interactive presentation returned {presentation.Status} with native result {presentation.Diagnostic?.NativeResult}.");
            }

            return presentation.Status;
        }

        var firstPresentation = await PresentAsync(
            renderSnapshot,
            activeSurface,
            clientPixelSize,
            $"vulkan-nativeaot-{args[1]}-first-window")
            .ConfigureAwait(false);
        var secondPresentation = await PresentAsync(
            renderSnapshot,
            activeSecondSurface,
            secondClientPixelSize,
            $"vulkan-nativeaot-{args[1]}-second-window")
            .ConfigureAwait(false);

        RenderSnapshot? liveSnapshot = null;
        await host.InvokeAsync(_ => {
            var stepReceipt = world!.Step();

            if (stepReceipt.CompletedTick <= completedTick)
            {
                throw new InvalidOperationException("Vulkan Interactive World stepping did not advance its committed tick.");
            }

            completedTick = stepReceipt.CompletedTick;

            if (!sceneInstance.TryGetEntity(rootEntityId, out var cameraEntity))
            {
                throw new InvalidOperationException("The activated Scene lost its camera Entity during Interactive World stepping.");
            }

            var extraction = host.Renderer.Extract(
                world,
                new RenderExtractionOptions3D(
                    stepReceipt.Frame3,
                    secondClientPixelSize,
                    [world.GetEntityId(cameraEntity)]));

            if (extraction.Status is not RenderExtractionStatus.Extracted)
            {
                throw new InvalidOperationException(
                    $"Vulkan live snapshot extraction returned {extraction.Status}: {string.Join("; ", extraction.Diagnostics)}");
            }

            liveSnapshot = extraction.Snapshot
                ?? throw new InvalidOperationException("Vulkan live snapshot extraction omitted its RenderSnapshot.");
        }).ConfigureAwait(false);
        using var liveRenderSnapshot = liveSnapshot
            ?? throw new InvalidOperationException("Vulkan Interactive host produced no live RenderSnapshot.");
        var firstClose = await host.Platform.CloseWindowAsync(window).ConfigureAwait(false);

        if (firstClose.Status is not WindowCommandStatus.Applied)
        {
            throw new InvalidOperationException(
                $"Vulkan first-window close returned {firstClose.Status}.");
        }

        var remainingWindowPresentation = await PresentAsync(
            liveRenderSnapshot,
            activeSecondSurface,
            secondClientPixelSize,
            $"vulkan-nativeaot-{args[1]}-window-after-peer-close")
            .ConfigureAwait(false);
        var secondClose = await host.Platform.CloseWindowAsync(secondWindow).ConfigureAwait(false);

        if (secondClose.Status is not WindowCommandStatus.Applied)
        {
            throw new InvalidOperationException(
                $"Vulkan second-window close returned {secondClose.Status}.");
        }

        Console.WriteLine(
            $"Vulkan {args[1]} Interactive graph presented with {firstPresentation}, {secondPresentation}, and {remainingWindowPresentation} receipts across two windows.");
        return 0;
    }

    private static async Task<int> RunOffscreenAsync(string[] args)
    {
        if ((args.Length != 4)
            || (args[1] is not "headless")
            || !Path.IsPathFullyQualified(args[2])
            || !Guid.TryParseExact(args[3], "N", out var rootEntityId))
        {
            throw new ArgumentException("Expected: offscreen headless <absolute-package-path> <root-entity-guid-N>.", nameof(args));
        }

        var schemas = CreateSchemaRegistry();
        var packageBytes = await File.ReadAllBytesAsync(args[2]).ConfigureAwait(false);
        var assetStoreOptions = new AssetStoreOptions(
            args[2],
            SHA256.HashData(packageBytes),
            CreateAssetStoreRegistration(schemas));
        var options = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.Vulkan,
            VulkanGraphicsBackend.CreateRegistration(),
            assetStoreOptions) {
            CpuWorkerCount = 0,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var host = await EngineHost.StartAsync(options).ConfigureAwait(false);
        await using var hostDisposal = host.ConfigureAwait(false);
        var worldRegistration = CreateWorldRegistration(schemas);
        World? world = null;
        SceneLoader? loader = null;

        await host.InvokeAsync(context => {
            world = context.CreateWorld(new WorldOptions(worldRegistration) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                WorldGuid = Guid.NewGuid(),
            });
            loader = new SceneLoader(world, host.Assets);
        }).ConfigureAwait(false);

        using var prepared = await loader!.PrepareAsync(
            new AssetId(SceneAssetId),
            new SceneLoadOptions()).ConfigureAwait(false);
        var activation = await loader.ActivateAsync(prepared)
            .WaitAsync(TimeSpan.FromSeconds(10))
            .ConfigureAwait(false);
        var sceneInstance = activation.Instance
            ?? throw new InvalidOperationException("Vulkan Scene activation returned no SceneInstance.");

        if (activation.Status is not SceneActivationStatus.Activated)
        {
            throw new InvalidOperationException($"Vulkan Scene activation returned {activation.Status}.");
        }

        await host.InvokeAsync(_ => world!.Step()).ConfigureAwait(false);

        var targetExtent = new PixelSize(32, 32);
        RenderSnapshot? snapshot = null;
        await host.InvokeAsync(_ => {
            if (!sceneInstance.TryGetEntity(rootEntityId, out var cameraEntity))
            {
                throw new InvalidOperationException("The activated Scene did not contain the expected camera Entity.");
            }

            var cameraId = world!.GetEntityId(cameraEntity);
            var extraction = host.Renderer.Extract(
                world,
                new RenderExtractionOptions3D(world.CurrentFrame3, targetExtent, [cameraId]));

            if (extraction.Status is not RenderExtractionStatus.Extracted)
            {
                throw new InvalidOperationException(
                    $"Vulkan snapshot extraction returned {extraction.Status}: {string.Join("; ", extraction.Diagnostics)}");
            }

            snapshot = extraction.Snapshot
                ?? throw new InvalidOperationException("Vulkan snapshot extraction omitted its RenderSnapshot.");
        }).ConfigureAwait(false);

        using var renderSnapshot = snapshot
            ?? throw new InvalidOperationException("Vulkan host produced no RenderSnapshot.");
        var graphicsDevice = host.GraphicsDevice
            ?? throw new InvalidOperationException("Vulkan OffscreenNative host returned no GraphicsDevice.");
        using var target = await graphicsDevice.CreateTextureAsync(new TextureDescription(
            TextureDimension.Dimension2D,
            targetExtent.Width,
            targetExtent.Height,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.ColorAttachment | TextureUsage.CopySource | TextureUsage.CopyDestination,
            "vulkan-nativeaot-offscreen-target")).ConfigureAwait(false);
        var graph = host.Renderer.BuildGraph(renderSnapshot, target);
        var pass = graph.AddRasterPass(new RenderPassDescription(
            "vulkan-nativeaot-offscreen-clear",
            order: 0,
            predecessors: [],
            hasExternalSideEffects: false));
        var output = graph.WriteColorAttachment(
            pass,
            graph.OffscreenColorTargetVersion,
            new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color),
            AttachmentLoadAction.Clear,
            AttachmentStoreAction.Store,
            new Vector4(1.0f, 0.0f, 1.0f, 1.0f));
        graph.SetPresentationSource(output);
        var compile = graph.Compile();

        if (compile.Status is not RenderGraphCompileStatus.Compiled)
        {
            throw new InvalidOperationException(
                $"Vulkan Offscreen graph compilation returned {compile.Status}: {string.Join("; ", compile.Diagnostics)}");
        }

        var plan = compile.Plan
            ?? throw new InvalidOperationException("Vulkan Offscreen graph compilation omitted its plan.");
        var submission = await host.Renderer.SubmitAsync(renderSnapshot, plan)
            .WaitAsync(TimeSpan.FromSeconds(15))
            .ConfigureAwait(false);

        if ((submission.PresentationSequence is not null)
            || (submission.SurfaceEpoch != 0)
            || (submission.SwapchainEpoch != 0))
        {
            throw new InvalidOperationException("Vulkan Offscreen submission reported WSI presentation metadata.");
        }

        using var readback = await graphicsDevice.ReadbackTextureAsync(
            target,
            new TextureSubresource(0, 0, TextureAspect.Color)).ConfigureAwait(false);
        var padded = new byte[checked((int)readback.ByteLength)];

        using (var readLease = readback.AcquireReadLease())
        {
            var copyAction = new ReadbackCopyAction(padded);
            readLease.UseWindow(0, padded.Length, ref copyAction);
        }

        var centerPixelOffset = checked(
            (int)((readback.RowPitch * (readback.Height / 2)) + (readback.Width / 2 * 4)));

        if ((padded[centerPixelOffset] != byte.MaxValue)
            || (padded[centerPixelOffset + 1] != 0)
            || (padded[centerPixelOffset + 2] != byte.MaxValue)
            || (padded[centerPixelOffset + 3] != byte.MaxValue))
        {
            throw new InvalidOperationException("Vulkan Offscreen readback did not contain the expected cleared pixel.");
        }

        Console.WriteLine($"Vulkan {args[0]} graph readback passed.");
        return 0;
    }

    private static void VerifyGameInputUnavailableDiagnostic(EngineHost host)
    {
        var diagnostics = host.Diagnostics.GetSnapshot(new DiagnosticQuery(
            32,
            DiagnosticSeverity.Warning,
            worldGuid: null,
            minimumTick: null));

        foreach (var diagnostic in diagnostics.Records)
        {
            if (StringComparer.Ordinal.Equals(diagnostic.Code, "platform-gamepad-unavailable")
                && diagnostic.Message.Contains("runtime-generated COM stubs", StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            "NativeAOT Windows startup did not diagnose the unavailable GameInput COM adapter.");
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
