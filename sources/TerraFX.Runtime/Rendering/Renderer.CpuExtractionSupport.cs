// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Scenes;

namespace TerraFX.Rendering;

/// <summary>Creates immutable CPU render snapshots from committed World state.</summary>
public sealed partial class Renderer
{
    private readonly DiagnosticsService? _diagnostics;
    private readonly GpuResidencyService? _gpuResidencyService;
    private readonly GraphicsDevice? _graphicsDevice;
    private readonly uint _hostId;

    private Renderer(
        GraphicsDevice? graphicsDevice,
        GpuResidencyService? gpuResidencyService,
        DiagnosticsService? diagnostics,
        uint hostId)
    {
        if ((graphicsDevice is null) != (gpuResidencyService is null))
        {
            throw new ArgumentException("GPU residency and GraphicsDevice ownership must be selected together.", nameof(gpuResidencyService));
        }

        if ((diagnostics is null) != (hostId == 0))
        {
            throw new ArgumentException("A host-bound diagnostic publisher requires the exact nonzero host identity.", nameof(hostId));
        }

        if ((graphicsDevice is not null) && (diagnostics is null))
        {
            throw new ArgumentNullException(nameof(diagnostics), "A native Renderer requires its borrowed host Diagnostics service.");
        }

        _diagnostics = diagnostics;
        _gpuResidencyService = gpuResidencyService;
        _graphicsDevice = graphicsDevice;
        _hostId = hostId;
    }

    /// <summary>Creates the opaque per-World CPU extraction registration.</summary>
    /// <returns>The registration to enlist before building a World registration.</returns>
    public static CpuExtractionSupportRegistration CreateCpuExtractionSupportRegistration()
        => new(new BuiltInCpuExtractionSupportRegistration());

    /// <summary>Creates a Renderer that performs CPU extraction without activating a graphics device.</summary>
    /// <returns>The headless Renderer service.</returns>
    public static Renderer CreateHeadless()
        => new(graphicsDevice: null, gpuResidencyService: null, diagnostics: null, hostId: 0);

    /// <summary>Registers the built-in native Mesh, Texture, Atlas, and Material asset codecs with an Assets builder.</summary>
    /// <param name="builder">The asset-store builder whose schema registry contains the Rendering schemas.</param>
    /// <returns>The builder with the supported rendering asset codecs registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException">The builder does not contain the exact compiled rendering schema tuples or registration conflicts.</exception>
    public static AssetStoreRegistrationBuilder RegisterAssetCodecs(AssetStoreRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.RegisterCodec(new RenderMeshAssetCodec());
        _ = builder.RegisterCodec(new RenderTextureAssetCodec());
        _ = builder.RegisterCodec(new RenderAtlasAssetCodec());
        return builder.RegisterCodec(new RenderMaterialAssetCodec());
    }

    internal static Renderer Create(
        GraphicsDevice? graphicsDevice,
        GpuResidencyService? gpuResidencyService,
        DiagnosticsService diagnostics,
        uint hostId)
        => new(graphicsDevice, gpuResidencyService, diagnostics, hostId);

    private sealed class BuiltInCpuExtractionSupportRegistration : IWorldCpuExtractionSupportRegistration
    {
        private const long ObjectStorageByteReservation = 256;

        public IWorldCpuExtractionParticipant CreateParticipant(
            World world,
            WorldOptions options,
            IWorldSceneParticipant sceneParticipant)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(sceneParticipant);
            return new CpuExtractionWorldParticipant(
                world,
                sceneParticipant,
                GetWorldStorageByteReservation(options));
        }

        public long GetWorldStorageByteReservation(WorldOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return ObjectStorageByteReservation;
        }

        public void RegisterComponents(
            WorldRegistrationBuilder builder,
            CompiledContentSchemaRegistry schemas)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(schemas);
            _ = RenderComponentDefinitions.RegisterMesh(builder, schemas);
            _ = RenderComponentDefinitions.RegisterSprite(builder, schemas);
            _ = RenderComponentDefinitions.RegisterPointLight(builder, schemas);
            _ = RenderComponentDefinitions.RegisterDirectionalLight(builder, schemas);
            _ = RenderComponentDefinitions.RegisterSpotLight(builder, schemas);
        }
    }

    private sealed class CpuExtractionWorldParticipant :
        ISceneExpansionResourcePreparer,
        IWorldCpuExtractionParticipant
    {
        private int _isDisposed;
        private readonly long _ownedByteReservation;
        private readonly IWorldSceneParticipant _sceneParticipant;
        private readonly SceneWorldRegistry _sceneRegistry;
        private readonly World _world;

        internal CpuExtractionWorldParticipant(
            World world,
            IWorldSceneParticipant sceneParticipant,
            long ownedByteReservation)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(sceneParticipant);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownedByteReservation);

            if (sceneParticipant is not SceneWorldRegistry sceneRegistry)
            {
                throw new ArgumentException("CPU extraction requires the exact built-in Scene registry.", nameof(sceneParticipant));
            }

            _world = world;
            _sceneParticipant = sceneParticipant;
            _sceneRegistry = sceneRegistry;
            _ownedByteReservation = ownedByteReservation;
        }

        public long OwnedByteReservation => _ownedByteReservation;

        public WorldFrameReadLease AcquireFrameReadLease(IWorldSceneParticipant sceneParticipant)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

            if (!ReferenceEquals(sceneParticipant, _sceneParticipant))
            {
                throw new ArgumentException("The Scene participant is not the exact prerequisite captured by CPU extraction support.", nameof(sceneParticipant));
            }

            return _world.AcquireFrameReadLease(this, sceneParticipant);
        }

        public void Dispose()
        {
            _ = Interlocked.Exchange(ref _isDisposed, 1);
        }

        internal bool TryGetSceneInstance(World world, Entity entity, out SceneInstance? instance)
        {
            ArgumentNullException.ThrowIfNull(world);

            if (!ReferenceEquals(world, _world))
            {
                throw new ArgumentException("The CPU extraction participant belongs to another World.", nameof(world));
            }

            return _sceneRegistry.TryGetInstanceForEntity(world, entity, out instance);
        }

        internal bool TryGetSceneInstance(World world, Guid sceneInstanceId, out SceneInstance? instance)
        {
            ArgumentNullException.ThrowIfNull(world);

            if (!ReferenceEquals(world, _world))
            {
                throw new ArgumentException("The CPU extraction participant belongs to another World.", nameof(world));
            }

            return _sceneRegistry.TryGetInstance(sceneInstanceId, out instance);
        }

        Task<long> ISceneExpansionResourcePreparer.MeasureStorageByteReservationAsync(
            SceneExpansionPlan plan,
            CancellationToken cancellationToken)
            => RenderSceneAssetBundle.MeasureStorageByteReservationAsync(plan, cancellationToken);

        async Task<IDisposable?> ISceneExpansionResourcePreparer.PrepareAsync(
            SceneExpansionPlan plan,
            Func<long, WorldSceneStorageReservation?> reserveStorage,
            Func<long, WorldSceneStorageReservation?> reserveWorkspace,
            CancellationToken cancellationToken)
            => await RenderSceneAssetBundle.PrepareAsync(
                plan,
                reserveStorage,
                reserveWorkspace,
                cancellationToken).ConfigureAwait(false);

        async Task<IDisposable?> ISceneExpansionResourcePreparer.PrepareOwnedAsync(
            SceneExpansionPlan plan,
            ScenePreparationFunding funding,
            CancellationToken cancellationToken)
            => await RenderSceneAssetBundle.PrepareOwnedAsync(
                plan,
                funding,
                cancellationToken).ConfigureAwait(false);
    }
}
