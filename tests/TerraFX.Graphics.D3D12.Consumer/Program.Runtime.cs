// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading.Tasks;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;
using TerraFX.Hosting;
using TerraFX.Numerics;
using TerraFX.Platform;
using TerraFX.Rendering;
using TerraFX.Scenes;

namespace TerraFX.Graphics.D3D12.Consumer;

internal static partial class Program
{
    private static WorldPersistenceMetadata CreatePersistenceMetadata()
    {
        var digest = new byte[32];
        var application = new WorldPersistenceApplication(
            new Guid("20000000-0000-0000-0000-000000000002"),
            majorVersion: 1,
            minorVersion: 0,
            modules: []);
        var runtimeProfile = new WorldPersistenceRuntimeProfile(
            digest,
            digest,
            digest,
            WorldPersistenceOperatingSystem.Windows,
            RuntimeFeature.IsDynamicCodeSupported
                ? WorldPersistenceCodeGeneration.Jit
                : WorldPersistenceCodeGeneration.NativeAot,
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

    [SupportedOSPlatform("windows10.0.22000.0")]
    private static async Task RunInteractiveD3D12Async(string[] args)
    {
        if ((args.Length != 3)
            || (args[0] is not "interactive")
            || !Path.IsPathFullyQualified(args[1])
            || !Guid.TryParseExact(args[2], "N", out var rootEntityId))
        {
            throw new ArgumentException("Expected: interactive <absolute-package-path> <root-entity-guid-N>.", nameof(args));
        }

        var schemas = CreateSchemaRegistry();
        var registrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(registrationBuilder);
        _ = Renderer.RegisterAssetCodecs(registrationBuilder);
        var packageBytes = await File.ReadAllBytesAsync(args[1]).ConfigureAwait(false);
        var assetStoreOptions = new AssetStoreOptions(
            args[1],
            SHA256.HashData(packageBytes),
            registrationBuilder.Build());
        var options = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            D3D12GraphicsBackend.CreateRegistration(),
            assetStoreOptions) {
            CpuWorkerCount = 0,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var host = await EngineHost.StartAsync(options).ConfigureAwait(false);
        await using var hostDisposal = host.ConfigureAwait(false);

        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            VerifyGameInputUnavailableDiagnostic(host);
        }

        var windowReceipt = await host.CreateWindowAsync(new WindowOptions("TerraFX D3D12 NativeAOT witness") {
            ClientSize = new Vector2(320.0f, 180.0f),
            Visible = false,
        }).ConfigureAwait(false);

        if (windowReceipt.Status is not WindowCreationStatus.Created)
        {
            throw new InvalidOperationException($"D3D12 window creation returned {windowReceipt.Status}.");
        }

        var window = windowReceipt.Window
            ?? throw new InvalidOperationException("D3D12 window creation omitted its WindowHandle.");
        var secondWindowReceipt = await host.CreateWindowAsync(new WindowOptions("TerraFX D3D12 NativeAOT witness 2") {
            ClientSize = new Vector2(320.0f, 180.0f),
            Visible = false,
        }).ConfigureAwait(false);

        if (secondWindowReceipt.Status is not WindowCreationStatus.Created)
        {
            throw new InvalidOperationException($"D3D12 second window creation returned {secondWindowReceipt.Status}.");
        }

        var secondWindow = secondWindowReceipt.Window
            ?? throw new InvalidOperationException("D3D12 second window creation omitted its WindowHandle.");

        if (secondWindow == window)
        {
            throw new InvalidOperationException("D3D12 Interactive window creation reused a live WindowHandle.");
        }

        const string ExpectedCallbackFailure = "D3D12 Interactive callback unwind witness.";
        var callbackFailureObserved = false;

        try
        {
            await host.InvokeAsync(_ => throw new InvalidOperationException(ExpectedCallbackFailure)).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
            when (StringComparer.Ordinal.Equals(exception.Message, ExpectedCallbackFailure))
        {
            callbackFailureObserved = true;
        }

        if (!callbackFailureObserved || host.Completion.IsCompleted)
        {
            throw new InvalidOperationException("D3D12 Interactive owner callback failure was not isolated from the host.");
        }

        var callbackRecovered = false;
        await host.InvokeAsync(_ => {
            var firstCallbackSurface = host.GetSurface(window);
            var secondCallbackSurface = host.GetSurface(secondWindow);

            if (ReferenceEquals(firstCallbackSurface, secondCallbackSurface))
            {
                throw new InvalidOperationException("D3D12 Interactive callback recovery reused one Surface across windows.");
            }

            callbackRecovered = true;
        }).ConfigureAwait(false);

        if (!callbackRecovered)
        {
            throw new InvalidOperationException("D3D12 Interactive owner callbacks did not resume after an exception.");
        }

        var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
        var worldRegistration = worldRegistrationBuilder.Build();
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
            new AssetId(3),
            new SceneLoadOptions()).ConfigureAwait(false);
        var activation = await loader.ActivateAsync(prepared)
            .WaitAsync(TimeSpan.FromSeconds(10))
            .ConfigureAwait(false);
        var sceneInstance = activation.Instance
            ?? throw new InvalidOperationException("D3D12 Scene activation returned no SceneInstance.");

        if (activation.Status is not SceneActivationStatus.Activated)
        {
            throw new InvalidOperationException($"D3D12 Scene activation returned {activation.Status}.");
        }

        await host.InvokeAsync(_ => completedTick = world!.Step().CompletedTick).ConfigureAwait(false);

        Surface? surface = null;
        PixelSize clientPixelSize = default;
        await host.InvokeAsync(_ => {
            surface = host.GetSurface(window);
            clientPixelSize = host.Platform.GetMetrics(window).ClientPixelSize;
        }).ConfigureAwait(false);
        var activeSurface = surface
            ?? throw new InvalidOperationException("D3D12 Interactive host published no Surface.");
        Surface? secondSurface = null;
        PixelSize secondClientPixelSize = default;
        await host.InvokeAsync(_ => {
            secondSurface = host.GetSurface(secondWindow);
            secondClientPixelSize = host.Platform.GetMetrics(secondWindow).ClientPixelSize;
        }).ConfigureAwait(false);
        var activeSecondSurface = secondSurface
            ?? throw new InvalidOperationException("D3D12 Interactive host published no second Surface.");

        if ((clientPixelSize.Width <= 0)
            || (clientPixelSize.Height <= 0)
            || (secondClientPixelSize.Width <= 0)
            || (secondClientPixelSize.Height <= 0))
        {
            throw new InvalidOperationException("A D3D12 Interactive window has no positive client extent.");
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
                    $"D3D12 snapshot extraction returned {extraction.Status}: {string.Join("; ", extraction.Diagnostics)}");
            }

            snapshot = extraction.Snapshot
                ?? throw new InvalidOperationException("D3D12 snapshot extraction omitted its RenderSnapshot.");
        }).ConfigureAwait(false);

        using var renderSnapshot = snapshot
            ?? throw new InvalidOperationException("D3D12 host produced no RenderSnapshot.");

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
                    $"D3D12 Surface graph compilation returned {compile.Status}: {string.Join("; ", compile.Diagnostics)}");
            }

            return compile.Plan
                ?? throw new InvalidOperationException("D3D12 Surface graph compilation omitted its plan.");
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
                ?? throw new InvalidOperationException("D3D12 Interactive submission omitted its presentation sequence.");
            var presentation = await targetSurface.WaitForPresentationAsync(presentationSequence)
                .WaitAsync(TimeSpan.FromSeconds(15))
                .ConfigureAwait(false);

            if (presentation.Status is not (PresentationStatus.Presented or PresentationStatus.Occluded))
            {
                throw new InvalidOperationException(
                    $"D3D12 Interactive presentation returned {presentation.Status} with native result {presentation.Diagnostic?.NativeResult}.");
            }

            return presentation.Status;
        }

        var firstPresentation = await PresentAsync(
            renderSnapshot,
            activeSurface,
            clientPixelSize,
            "d3d12-nativeaot-first-window")
            .ConfigureAwait(false);
        var secondPresentation = await PresentAsync(
            renderSnapshot,
            activeSecondSurface,
            secondClientPixelSize,
            "d3d12-nativeaot-second-window")
            .ConfigureAwait(false);

        RenderSnapshot? liveSnapshot = null;
        await host.InvokeAsync(_ => {
            var stepReceipt = world!.Step();

            if (stepReceipt.CompletedTick <= completedTick)
            {
                throw new InvalidOperationException("D3D12 Interactive World stepping did not advance its committed tick.");
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
                    $"D3D12 live snapshot extraction returned {extraction.Status}: {string.Join("; ", extraction.Diagnostics)}");
            }

            liveSnapshot = extraction.Snapshot
                ?? throw new InvalidOperationException("D3D12 live snapshot extraction omitted its RenderSnapshot.");
        }).ConfigureAwait(false);
        using var liveRenderSnapshot = liveSnapshot
            ?? throw new InvalidOperationException("D3D12 Interactive host produced no live RenderSnapshot.");
        var firstClose = await host.Platform.CloseWindowAsync(window).ConfigureAwait(false);

        if (firstClose.Status is not WindowCommandStatus.Applied)
        {
            throw new InvalidOperationException(
                $"D3D12 first-window close returned {firstClose.Status}.");
        }

        var remainingWindowPresentation = await PresentAsync(
            liveRenderSnapshot,
            activeSecondSurface,
            secondClientPixelSize,
            "d3d12-nativeaot-window-after-peer-close")
            .ConfigureAwait(false);
        var secondClose = await host.Platform.CloseWindowAsync(secondWindow).ConfigureAwait(false);

        if (secondClose.Status is not WindowCommandStatus.Applied)
        {
            throw new InvalidOperationException(
                $"D3D12 second-window close returned {secondClose.Status}.");
        }

        Console.WriteLine(
            $"D3D12 Interactive graph presented with {firstPresentation}, {secondPresentation}, and {remainingWindowPresentation} receipts across two windows.");
    }

    [SupportedOSPlatform("windows10.0.22000.0")]
    [SuppressMessage("Globalization", "CA1303", Justification = "The output is a machine-checked consumer witness marker.")]
    private static async Task RunOffscreenD3D12Async(string[] args)
    {
        if ((args.Length != 4)
            || (args[0] is not "offscreen")
            || (args[1] is not "headless")
            || !Path.IsPathFullyQualified(args[2])
            || !Guid.TryParseExact(args[3], "N", out var rootEntityId))
        {
            throw new ArgumentException("Expected: offscreen headless <absolute-package-path> <root-entity-guid-N>.", nameof(args));
        }

        var schemas = CreateSchemaRegistry();
        var assetStoreRegistrationBuilder = new AssetStoreRegistrationBuilder(schemas);
        _ = SceneSupport.RegisterAssetCodecs(assetStoreRegistrationBuilder);
        _ = Renderer.RegisterAssetCodecs(assetStoreRegistrationBuilder);
        var packageBytes = await File.ReadAllBytesAsync(args[2]).ConfigureAwait(false);
        var assetStoreOptions = new AssetStoreOptions(
            args[2],
            SHA256.HashData(packageBytes),
            assetStoreRegistrationBuilder.Build());
        var options = new EngineHostOptions(
            HostProfile.OffscreenNative,
            PlatformBackend.Headless,
            GraphicsBackend.D3D12,
            D3D12GraphicsBackend.CreateRegistration(),
            assetStoreOptions) {
            CpuWorkerCount = 0,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        var host = await EngineHost.StartAsync(options).ConfigureAwait(false);
        await using var hostDisposal = host.ConfigureAwait(false);

        var worldRegistrationBuilder = new WorldRegistrationBuilder(schemas);
        _ = worldRegistrationBuilder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        _ = worldRegistrationBuilder.RegisterCpuExtractionSupport(Renderer.CreateCpuExtractionSupportRegistration());
        var worldRegistration = worldRegistrationBuilder.Build();
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
            new AssetId(3),
            new SceneLoadOptions()).ConfigureAwait(false);
        var activation = await loader.ActivateAsync(prepared)
            .WaitAsync(TimeSpan.FromSeconds(10))
            .ConfigureAwait(false);
        var sceneInstance = activation.Instance
            ?? throw new InvalidOperationException("D3D12 Offscreen Scene activation returned no SceneInstance.");

        if (activation.Status is not SceneActivationStatus.Activated)
        {
            throw new InvalidOperationException($"D3D12 Offscreen Scene activation returned {activation.Status}.");
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
                    $"D3D12 Offscreen snapshot extraction returned {extraction.Status}: {string.Join("; ", extraction.Diagnostics)}");
            }

            snapshot = extraction.Snapshot
                ?? throw new InvalidOperationException("D3D12 Offscreen snapshot extraction omitted its RenderSnapshot.");
        }).ConfigureAwait(false);

        using var renderSnapshot = snapshot
            ?? throw new InvalidOperationException("D3D12 Offscreen host produced no RenderSnapshot.");
        var graphicsDevice = host.GraphicsDevice
            ?? throw new InvalidOperationException("D3D12 OffscreenNative host returned no GraphicsDevice.");
        using var target = await graphicsDevice.CreateTextureAsync(new TextureDescription(
            TextureDimension.Dimension2D,
            targetExtent.Width,
            targetExtent.Height,
            depth: 1,
            arrayLayers: 1,
            mipLevels: 1,
            TextureFormat.Rgba8Unorm,
            TextureUsage.ColorAttachment | TextureUsage.CopySource | TextureUsage.CopyDestination,
            "d3d12-nativeaot-offscreen-target")).ConfigureAwait(false);
        var graph = host.Renderer.BuildGraph(renderSnapshot, target);
        var pass = graph.AddRasterPass(new RenderPassDescription(
            "d3d12-nativeaot-offscreen-clear",
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
                $"D3D12 Offscreen graph compilation returned {compile.Status}: {string.Join("; ", compile.Diagnostics)}");
        }

        var plan = compile.Plan
            ?? throw new InvalidOperationException("D3D12 Offscreen graph compilation omitted its plan.");
        var submission = await host.Renderer.SubmitAsync(renderSnapshot, plan)
            .WaitAsync(TimeSpan.FromSeconds(15))
            .ConfigureAwait(false);

        if ((submission.PresentationSequence is not null)
            || (submission.SurfaceEpoch != 0)
            || (submission.SwapchainEpoch != 0))
        {
            throw new InvalidOperationException("D3D12 Offscreen submission reported WSI presentation metadata.");
        }

        using var readback = await graphicsDevice.ReadbackTextureAsync(
            target,
            new TextureSubresource(0, 0, TextureAspect.Color)).ConfigureAwait(false);
        var readbackBytes = new byte[checked((int)readback.ByteLength)];

        using (var readLease = readback.AcquireReadLease())
        {
            var copyAction = new ReadbackCopyAction(readbackBytes);
            readLease.UseWindow(0, readbackBytes.Length, ref copyAction);
        }

        var centerPixelOffset = checked(
            (int)((readback.RowPitch * (readback.Height / 2)) + (readback.Width / 2 * 4)));

        if ((readbackBytes[centerPixelOffset] != byte.MaxValue)
            || (readbackBytes[centerPixelOffset + 1] != 0)
            || (readbackBytes[centerPixelOffset + 2] != byte.MaxValue)
            || (readbackBytes[centerPixelOffset + 3] != byte.MaxValue))
        {
            throw new InvalidOperationException("D3D12 Offscreen readback did not contain the expected cleared pixel.");
        }

        Console.WriteLine("D3D12 Offscreen graph readback passed.");
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
